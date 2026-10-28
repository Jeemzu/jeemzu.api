using System.Net;
using System.Text;
using System.Text.Json;
using JeemzuApi.DTOs;
using JeemzuApi.Models;
using JeemzuApi.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace JeemzuApi.Tests;

public sealed class GddAssistantServiceTests
{
    public static GddAssistantRequest Request() => new()
    {
        Checkpoint = "concept", Prompt = "A small exploration game.", Document = "# Game",
        History = [], References = [],
    };

    public static GddAssistantResponse Response() => new(
        "Review the concept.", ["What should the player feel?"], ["Optional mood board."],
        new("Test game", "A tiny game.", [new("Game direction", [new("Vision", "Explore a quiet world.")])]));

    [Theory]
    [InlineData("scratch", "Each proposed section REPLACES")]
    [InlineData("fill", "Each proposed item ADDS")]
    public async Task SendsModeSpecificProposalInstructions(string mode, string expectedInstruction)
    {
        using var handler = new CompletionHandler(JsonSerializer.Serialize(Response()));
        using var http = new HttpClient(handler);
        var service = new GddAssistantService(new OpenAIChatCompletionService("gpt-6-luna", "test-key", httpClient: http));
        await service.CompleteAsync(Request() with { Mode = mode }, CancellationToken.None);
        using var wire = JsonDocument.Parse(handler.Body!);
        var system = wire.RootElement.GetProperty("messages")[0].GetProperty("content").GetString();
        Assert.Contains(expectedInstruction, system);
        if (mode == "fill")
        {
            Assert.Contains("missingDetails", system);
            Assert.Contains("Omitted subsections and sections are preserved", system);
            Assert.DoesNotContain("Each proposed section REPLACES", system);
        }
    }

    [Theory]
    [InlineData("gpt-6-luna", true)]
    [InlineData("gpt-4o-mini", false)]
    public async Task SendsBoundedStructuredMultimodalRequestsWithTheConfiguredModel(string model, bool noReasoning)
    {
        using var handler = new CompletionHandler(JsonSerializer.Serialize(Response()));
        using var http = new HttpClient(handler);
        var chat = new OpenAIChatCompletionService(model, "test-not-a-real-key", httpClient: http);
        var service = new GddAssistantService(chat);
        var request = Request() with
        {
            History = [new("user", "Keep the scope small."), new("assistant", "One island.")],
            References = [new("Mood", "Muted palette.", "https://example.com", Convert.ToBase64String([0xff, 0xd8, 0xff, 0xd9]))],
        };
        using var cts = new CancellationTokenSource();
        var result = await service.CompleteAsync(request, cts.Token);
        Assert.Equal("Test game", result.Proposal.Title);
        using var wire = JsonDocument.Parse(handler.Body!);
        var root = wire.RootElement;
        Assert.Equal(model, root.GetProperty("model").GetString());
        Assert.Equal(noReasoning, root.TryGetProperty("reasoning_effort", out var reasoning));
        if (noReasoning) Assert.Equal("none", reasoning.GetString());
        Assert.Equal(10000, root.GetProperty("max_completion_tokens").GetInt32());
        Assert.Equal("json_schema", root.GetProperty("response_format").GetProperty("type").GetString());
        Assert.Contains("data:image/jpeg;base64,", handler.Body);
        Assert.Contains("Keep the scope small.", handler.Body);
        Assert.True(handler.Cancellable);
    }

    [Theory]
    [InlineData("not JSON")]
    [InlineData("{}")]
    [InlineData("{\"Message\":\"partial\"}")]
    public async Task RejectsMalformedOrIncompleteProviderResponses(string body)
    {
        using var http = new HttpClient(new CompletionHandler(body));
        var service = new GddAssistantService(new OpenAIChatCompletionService("gpt-6-luna", "test-key", httpClient: http));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.CompleteAsync(Request(), CancellationToken.None));
    }

    [Theory]
    [InlineData("gpt-6-luna", true)]
    [InlineData("gpt-4o-mini", false)]
    public async Task ProfileChatPreservesGroundingAndSupportsIndependentModelOverrides(string model, bool noReasoning)
    {
        using var handler = new CompletionHandler("Grounded profile answer.");
        using var http = new HttpClient(handler);
        var chat = new OpenAIChatCompletionService(model, "test-key", httpClient: http);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OpenAI:ChatModel"] = model,
        }).Build();
        var service = new ChatService(new Embedding(), new VectorStore(), chat, configuration);
        Assert.Equal("Grounded profile answer.", await service.ChatAsync("What is known?", [], CancellationToken.None));
        using var wire = JsonDocument.Parse(handler.Body!);
        Assert.Equal(model, wire.RootElement.GetProperty("model").GetString());
        Assert.Equal(1200, wire.RootElement.GetProperty("max_completion_tokens").GetInt32());
        Assert.Equal(noReasoning, wire.RootElement.TryGetProperty("reasoning_effort", out var reasoning));
        if (noReasoning) Assert.Equal("none", reasoning.GetString());
        Assert.Contains("Use ONLY the information", handler.Body);
        Assert.Contains("Fictional grounding fact.", handler.Body);
    }

    [Fact]
    public async Task ProviderHttpFailuresUseTheControllersHandledExceptionType()
    {
        using var http = new HttpClient(new CompletionHandler("Error", HttpStatusCode.BadRequest));
        var service = new GddAssistantService(new OpenAIChatCompletionService("gpt-6-luna", "test-key", httpClient: http));
        await Assert.ThrowsAsync<HttpOperationException>(() => service.CompleteAsync(Request(), CancellationToken.None));
    }

    [Fact]
    public void RejectsUnsupportedCheckpointsAndOversizedReferences()
    {
        Assert.Equal("scratch", Request().Mode);
        Assert.NotNull(GddAssistantService.ValidateRequest(Request() with { Mode = "unknown" }));
        Assert.Null(GddAssistantService.ValidateRequest(Request() with { Mode = "fill" }));
        Assert.NotNull(GddAssistantService.ValidateRequest(Request() with { Checkpoint = "unknown" }));
        Assert.NotNull(GddAssistantService.ValidateRequest(Request() with
        {
            References = Enumerable.Range(0, 4).Select(_ => new GddAssistantReference("Notes", new string('a', 20000), "", "")).ToArray(),
        }));
        Assert.NotNull(GddAssistantService.ValidateRequest(Request() with { References = [new("Bad link", "", "file:///private", "")] }));
        Assert.NotNull(GddAssistantService.ValidateRequest(Request() with { References = [new("Bad image", "", "", "invalid base64")] }));
        Assert.NotNull(GddAssistantService.ValidateRequest(Request() with
        {
            References = [new("Too large", "", "", Convert.ToBase64String(new byte[262145]))],
        }));
        Assert.Null(GddAssistantService.ValidateRequest(Request()));
    }

    [Fact]
    public void RejectsOutOfCheckpointOrInvalidModelWrites()
    {
        Assert.True(GddAssistantService.ValidResponse(Response(), "concept"));
        Assert.False(GddAssistantService.ValidResponse(Response(), "gameplay"));
        Assert.False(GddAssistantService.ValidResponse(Response() with
        {
            Proposal = new("Game", "", [new("Game direction", [new("Vision", "First."), new("Vision", "Second.")])]),
        }, "concept"));
        Assert.False(GddAssistantService.ValidResponse(Response() with
        {
            Proposal = new("Game", "", [new("Game direction", [new("Vision", "## Bad heading")])]),
        }, "concept"));
        Assert.False(GddAssistantService.ValidResponse(Response() with
        {
            Proposal = new("Game", "", [new("Game direction", [new("Vision", "![Remote](https://example.com/image.jpg)")])]),
        }, "concept"));
    }

    private sealed class Embedding : IEmbeddingService
    {
        public Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(string text, CancellationToken ct = default) =>
            Task.FromResult<ReadOnlyMemory<float>>(new float[] { 1, 0 });
    }

    private sealed class VectorStore : IVectorStoreService
    {
        public Task<IEnumerable<KnowledgeChunk>> SearchAsync(ReadOnlyMemory<float> queryEmbedding, int topK = 5, CancellationToken ct = default) =>
            Task.FromResult<IEnumerable<KnowledgeChunk>>([new KnowledgeChunk { Content = "Fictional grounding fact." }]);
    }

    private sealed class CompletionHandler(string content, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public string? Body { get; private set; }
        public bool Cancellable { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Body = await request.Content!.ReadAsStringAsync(ct);
            Cancellable = ct.CanBeCanceled;
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    id = "chatcmpl-test", @object = "chat.completion", created = 1700000000, model = "gpt-6-luna",
                    choices = new[] { new { index = 0, message = new { role = "assistant", content }, finish_reason = "stop" } },
                    usage = new { prompt_tokens = 20, completion_tokens = 20, total_tokens = 40 },
                }), Encoding.UTF8, "application/json"),
            };
        }
    }
}
