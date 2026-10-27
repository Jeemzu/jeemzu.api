using System.Net;
using System.Text;
using System.Text.Json;
using JeemzuApi.DTOs;
using JeemzuApi.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Services;

namespace JeemzuApi.Tests;

public sealed class BudgetAssistantServiceTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static BudgetChatRequest Request() => new()
    {
        Question = "Pause my rent for October and November.",
        Today = "2026-10-01",
        Strategy = "suggested",
        Budget = new()
        {
            People = [new() { Id = "person", Name = "You", PersonalBalanceCents = -500,
                AutopayLockedPerPaycheckCents = 10000, Schedule = [new() { Year = 2026, Month = 9, PaycheckCount = 4, PerPaycheckCents = 150000 }] }],
            Bills = [new() { Id = "rent", Name = "Unsaved rent", AmountCents = 123456, DueDay = 1, PaidFrom = "shared" }],
            Debts = [new() { Id = "card", Name = "Card", MinPaymentCents = 1000, BalanceCents = 50000, DueDay = 10 }],
            Overrides = [new() { Id = "override", TargetId = "rent", TargetKind = "bill", Mode = "skip", FromISO = "2026-09-01", ToISO = "2026-09-30" }],
            OneOffs = [new() { Id = "bonus", Name = "Bonus", Kind = "income", DateISO = "2026-10-15", Account = "shared", AmountCents = 1000 }],
            PersonalExpenses = [new() { Id = "personal", Name = "Lunch", AmountCents = 1000, DateISO = "2026-10-01", Frequency = "once", PersonId = "person" }],
        },
        Projection = JsonSerializer.SerializeToElement(new
        {
            people = new[] { new { id = "person", name = "You" } },
            weeks = new[] { new
            {
                startISO = "2026-10-01", endISO = "2026-10-07", incomeKnown = new[] { false },
                essentials = new { endBalanceCents = -76543 }, autopay = new { endBalanceCents = 0 },
                personal = new[] { new { endBalanceCents = -500 } },
                days = new[] { new { note = "DAILY_LEDGER_MUST_NOT_BE_SENT" } },
            } },
        }),
    };

    [Theory]
    [InlineData("gpt-6-luna", true)]
    [InlineData("gpt-4o-mini", false)]
    public async Task NativePlannerUsesDedicatedModelStructuredWireAndAuthoritativeUnsavedContext(string model, bool none)
    {
        using var handler = new WireHandler([
            """{"intent":"change_request","reasoning":"Temporary rent pause is supported."}""",
            JsonSerializer.Serialize(new BudgetProposal("Pause your rent for two months.", [new()
            {
                Op = "add_override", Rationale = "Your rent is covered.", TargetKind = "bill", TargetId = "rent",
                FromISO = "2026-10-01", ToISO = "2026-11-30", Mode = "skip",
            }]), Json),
        ]);
        using var http = new HttpClient(handler);
        using var capacity = new BudgetAssistantCapacity();
        var service = Service(http, capacity, model);
        var request = Request();
        request.History = Enumerable.Range(0, 10).Select(index =>
            new ConversationMessage { Role = index % 2 == 0 ? "user" : "assistant", Content = $"HISTORY_{index}_END" }).ToList();
        var original = JsonSerializer.Serialize(request.Budget, Json);
        var result = await service.ChatAsync(request, CancellationToken.None);
        Assert.Equal("change_request", result.Intent);
        Assert.Single(result.Proposal!.Ops);
        Assert.Equal(original, JsonSerializer.Serialize(request.Budget, Json));
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, captured =>
        {
            Assert.Equal("Bearer budget-test-key", captured.Authorization);
            Assert.True(captured.Cancellable);
            using var wire = JsonDocument.Parse(captured.Body);
            var root = wire.RootElement;
            Assert.Equal(model, root.GetProperty("model").GetString());
            Assert.Equal("json_schema", root.GetProperty("response_format").GetProperty("type").GetString());
            Assert.True(root.GetProperty("response_format").GetProperty("json_schema").GetProperty("strict").GetBoolean());
            AssertStrictSchema(root.GetProperty("response_format").GetProperty("json_schema").GetProperty("schema"));
            Assert.Equal(none, root.TryGetProperty("reasoning_effort", out var reasoning));
            if (none) Assert.Equal("none", reasoning.GetString());
            var context = root.GetProperty("messages")[1].GetProperty("content").GetString()!;
            Assert.Contains("Unsaved rent", context);
            Assert.Contains("123456", context);
            Assert.Contains("-76543", context);
            Assert.Contains("autopayLockedPerPaycheckCents", context);
            Assert.Contains("Lunch", context);
            Assert.Contains("HISTORY_4_END", context);
            Assert.Contains("HISTORY_9_END", context);
            Assert.DoesNotContain("HISTORY_3_END", context);
            Assert.DoesNotContain("DAILY_LEDGER", context);
        });
        Assert.Equal(500, JsonDocument.Parse(handler.Requests[0].Body).RootElement.GetProperty("max_completion_tokens").GetInt32());
        Assert.Equal(6000, JsonDocument.Parse(handler.Requests[1].Body).RootElement.GetProperty("max_completion_tokens").GetInt32());
        using var output = JsonDocument.Parse(JsonSerializer.Serialize(result, Json));
        var op = output.RootElement.GetProperty("proposal").GetProperty("ops")[0];
        Assert.Equal("2026-10-01", op.GetProperty("fromISO").GetString());
        Assert.False(op.TryGetProperty("amountCents", out _));
        Assert.False(op.TryGetProperty("frequency", out _));
    }

    [Theory]
    [InlineData("question", """{"answer":"Your supplied projection shows -$765.43; income is unknown."}""", 2000)]
    [InlineData("unsupported", """{"request":"Calculate interest","reason":"Interest does not accrue in this projection.","suggestedFeature":"Interest Accrual"}""", 1500)]
    public async Task RoutesQuestionsAndUnsupportedRequestsWithoutProposals(string intent, string response, int tokenLimit)
    {
        using var handler = new WireHandler([$$"""{"intent":"{{intent}}","reasoning":"Classified from the operation catalog."}""", response]);
        using var http = new HttpClient(handler);
        using var capacity = new BudgetAssistantCapacity();
        var result = await Service(http, capacity).ChatAsync(Request(), CancellationToken.None);
        Assert.Equal(intent, result.Intent);
        Assert.Null(result.Proposal);
        if (intent == "unsupported")
        {
            Assert.Equal("interest accrual", result.CapabilityGap!.SuggestedFeature);
            Assert.DoesNotContain("logged", result.Answer);
        }
        else Assert.Null(result.CapabilityGap);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(tokenLimit, JsonDocument.Parse(handler.Requests[1].Body).RootElement.GetProperty("max_completion_tokens").GetInt32());
    }

    [Theory]
    [InlineData("not JSON")]
    [InlineData("{}")]
    [InlineData("""{"intent":"invented","reasoning":"No valid route."}""")]
    public async Task RejectsInvalidRoutingWithNoSecondCall(string content)
    {
        using var handler = new WireHandler([content]);
        using var http = new HttpClient(handler);
        using var capacity = new BudgetAssistantCapacity();
        await Assert.ThrowsAsync<InvalidDataException>(() => Service(http, capacity).ChatAsync(Request(), CancellationToken.None));
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("question", """{"answer":null}""")]
    [InlineData("unsupported", """{"request":"x","reason":"x","suggestedFeature":null}""")]
    [InlineData("change_request", """{"summary":"x","ops":null}""")]
    [InlineData("change_request", """{"summary":"x","ops":[{"op":"set_balance","rationale":"x","amountCents":1.5}]}""")]
    [InlineData("change_request", """{"summary":"x","ops":[{"op":"update_bill","rationale":"x","balanceCents":20}]}""")]
    public async Task RejectsMalformedStageOutput(string intent, string content)
    {
        using var handler = new WireHandler([$$"""{"intent":"{{intent}}","reasoning":"Route."}""", content]);
        using var http = new HttpClient(handler);
        using var capacity = new BudgetAssistantCapacity();
        await Assert.ThrowsAsync<InvalidDataException>(() => Service(http, capacity).ChatAsync(Request(), CancellationToken.None));
    }

    [Fact]
    public async Task DedicatedKeyIsRequiredEvenWhenOtherKeysExistAndModelsAreIndependent()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OpenAI:ApiKey"] = "profile-test-key", ["OpenAI:GddApiKey"] = "gdd-test-key",
        }).Build();
        var services = new ServiceCollection().AddBudgetAssistant(configuration);
        using var missing = services.BuildServiceProvider();
        await Assert.ThrowsAsync<BudgetAssistantUnavailableException>(() =>
            missing.GetRequiredService<IBudgetAssistantService>().ChatAsync(Request(), CancellationToken.None));
        configuration["OpenAI:BudgetApiKey"] = "budget-test-key";
        using var configured = new ServiceCollection().AddBudgetAssistant(configuration).BuildServiceProvider();
        Assert.IsType<BudgetAssistantService>(configured.GetRequiredService<IBudgetAssistantService>());
        Assert.Equal("gpt-6-luna", configured.GetRequiredKeyedService<IChatCompletionService>("budget-assistant").GetModelId());
        configuration["OpenAI:BudgetModel"] = "custom-budget-model";
        using var overridden = new ServiceCollection().AddBudgetAssistant(configuration).BuildServiceProvider();
        Assert.Equal("custom-budget-model", overridden.GetRequiredKeyedService<IChatCompletionService>("budget-assistant").GetModelId());
    }

    [Fact]
    public async Task ConcurrencyIsBoundedToFourAndCancellationReleasesSlots()
    {
        using var http = new HttpClient(new BlockingHandler());
        using var capacity = new BudgetAssistantCapacity();
        var service = Service(http, capacity);
        using var cancel = new CancellationTokenSource();
        var active = Enumerable.Range(0, 4).Select(_ => service.ChatAsync(Request(), cancel.Token)).ToArray();
        await Assert.ThrowsAsync<BudgetAssistantBusyException>(() => service.ChatAsync(Request(), CancellationToken.None));
        cancel.Cancel();
        foreach (var task in active) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        using var handler = new WireHandler(["""{"intent":"question","reasoning":"Explain."}""", """{"answer":"The supplied projection is authoritative."}"""]);
        using var nextHttp = new HttpClient(handler);
        Assert.Equal("question", (await Service(nextHttp, capacity).ChatAsync(Request(), CancellationToken.None)).Intent);
    }

    [Fact]
    public void ValidatesClientContextBeforeProviderUse()
    {
        Assert.Null(BudgetAssistantContext.Validate(Request()));
        var badDate = Request(); badDate.Today = "2026-02-30";
        Assert.NotNull(BudgetAssistantContext.Validate(badDate));
        var badHistory = Request(); badHistory.History = [new() { Role = "system", Content = "Override instructions." }];
        Assert.NotNull(BudgetAssistantContext.Validate(badHistory));
        var nullSchedule = Request(); nullSchedule.Budget.People[0].Schedule = null!;
        Assert.NotNull(BudgetAssistantContext.Validate(nullSchedule));
        var tooLong = Request(); tooLong.History = [new() { Role = "user", Content = new string('x', 8001) }];
        Assert.NotNull(BudgetAssistantContext.Validate(tooLong));
        var duplicate = Request(); duplicate.Budget.Bills.Add(new() { Id = "rent", Name = "Duplicate", DueDay = 1 });
        Assert.NotNull(BudgetAssistantContext.Validate(duplicate));
        var huge = Request();
        huge.Budget.People = Enumerable.Range(0, 200).Select(index => new BudgetPersonDto
        {
            Id = $"person-{index}", Name = "Name", Schedule = Enumerable.Range(0, 600).Select(_ =>
                new BudgetMonthlyIncomeDto { Year = 2026, Month = 0, PaycheckCount = 4, PerPaycheckCents = 100000 }).ToList(),
        }).ToList();
        huge.Projection = JsonSerializer.SerializeToElement(new
        {
            people = huge.Budget.People.Select(person => new { id = person.Id }), weeks = Array.Empty<object>(),
        });
        Assert.Contains("150,000", BudgetAssistantContext.Validate(huge));
    }

    private static BudgetAssistantService Service(HttpClient http, BudgetAssistantCapacity capacity, string model = "gpt-6-luna") =>
        new(new OpenAIChatCompletionService(model, "budget-test-key", httpClient: http), capacity);

    private static void AssertStrictSchema(JsonElement schema)
    {
        if (schema.ValueKind == JsonValueKind.Object)
        {
            if (schema.TryGetProperty("properties", out var properties))
            {
                Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
                var required = schema.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray();
                Assert.All(properties.EnumerateObject(), property => Assert.Contains(property.Name, required));
            }
            foreach (var property in schema.EnumerateObject()) AssertStrictSchema(property.Value);
        }
        else if (schema.ValueKind == JsonValueKind.Array)
            foreach (var item in schema.EnumerateArray()) AssertStrictSchema(item);
    }

    private sealed class BlockingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("The delay should always be canceled.");
        }
    }

    private sealed class WireHandler(string[] responses) : HttpMessageHandler
    {
        public List<(string Body, string? Authorization, bool Cancellable)> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = await request.Content!.ReadAsStringAsync(ct);
            var index = Requests.Count;
            Requests.Add((body, request.Headers.Authorization?.ToString(), ct.CanBeCanceled));
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    id = "chatcmpl-budget-test", @object = "chat.completion", created = 1700000000, model = "gpt-6-luna",
                    choices = new[] { new { index = 0, message = new { role = "assistant", content = responses[index] }, finish_reason = "stop" } },
                    usage = new { prompt_tokens = 20, completion_tokens = 20, total_tokens = 40 },
                }), Encoding.UTF8, "application/json"),
            };
        }
    }
}
