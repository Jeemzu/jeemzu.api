using System.Text.Json;
using System.Text.Json.Serialization;
using JeemzuApi.DTOs;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Services;

namespace JeemzuApi.Services;

internal sealed record BudgetRouterDecision(string Intent, string Reasoning);
internal sealed record BudgetAnalystAnswer(string Answer);

public sealed class BudgetAssistantUnavailableException : Exception
{
    public BudgetAssistantUnavailableException() : base("The dedicated Budgetize key is not configured.") { }
}

public sealed class BudgetAssistantBusyException : Exception
{
    public BudgetAssistantBusyException() : base("Budgetize assistant capacity is currently full.") { }
}

public sealed class BudgetAssistantService(
    [FromKeyedServices("budget-assistant")] IChatCompletionService chat,
    BudgetAssistantCapacity capacity) : IBudgetAssistantService
{
    private static readonly JsonSerializerOptions ModelJson = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public async Task<BudgetChatResult> ChatAsync(BudgetChatRequest request, CancellationToken ct)
    {
        if (!await capacity.Gate.WaitAsync(0, ct)) throw new BudgetAssistantBusyException();
        try
        {
            var context = BudgetAssistantContext.Build(request);
            var route = await Complete<BudgetRouterDecision>(BudgetAssistantPrompts.Router, context, 500, ct);
            if (!BudgetProposalValidator.Text(route.Reasoning, 2000))
                throw new InvalidDataException("The model returned invalid routing metadata.");
            switch (route.Intent)
            {
                case "question":
                    var answer = await Complete<BudgetAnalystAnswer>(BudgetAssistantPrompts.Analyst, context, 2000, ct);
                    if (!BudgetProposalValidator.Text(answer.Answer, 8000)) throw new InvalidDataException("The model returned an empty or oversized answer.");
                    return new(answer.Answer, route.Intent, null, null);
                case "change_request":
                    var proposal = await Complete<BudgetProposal>(BudgetAssistantPrompts.Planner, context, 6000, ct);
                    return BudgetProposalValidator.Compose(proposal, request.Budget);
                case "unsupported":
                    var gap = await Complete<BudgetCapabilityGapResponse>(BudgetAssistantPrompts.Gap, context, 1500, ct);
                    if (!BudgetProposalValidator.Text(gap.Request, 2000) || !BudgetProposalValidator.Text(gap.Reason, 2000) ||
                        !BudgetProposalValidator.Text(gap.SuggestedFeature, 120))
                        throw new InvalidDataException("The model returned an invalid capability gap.");
                    gap = gap with { SuggestedFeature = gap.SuggestedFeature.Trim().ToLowerInvariant() };
                    return new(gap.Reason, route.Intent, null, gap);
                default:
                    throw new InvalidDataException("The model returned an unsupported route.");
            }
        }
        finally { capacity.Gate.Release(); }
    }

    private async Task<T> Complete<T>(string instructions, string context, int tokens, CancellationToken ct)
    {
        var history = new ChatHistory();
        history.AddSystemMessage($"{BudgetAssistantPrompts.Safety}\n{BudgetAssistantPrompts.Capabilities}\n{instructions}");
        history.AddUserMessage(context);
        var settings = new OpenAIPromptExecutionSettings { ResponseFormat = typeof(T), MaxTokens = tokens };
        if (chat.GetModelId()?.StartsWith("gpt-6", StringComparison.Ordinal) == true)
        {
#pragma warning disable OPENAI001 // The SDK's extensible reasoning value supports "none"; SK's string allowlist predates it.
            settings.ReasoningEffort = new OpenAI.Chat.ChatReasoningEffortLevel("none");
#pragma warning restore OPENAI001
        }
        var response = await chat.GetChatMessageContentAsync(history, settings, cancellationToken: ct);
        try
        {
            return JsonSerializer.Deserialize<T>(response.Content ?? "", ModelJson)
                ?? throw new InvalidDataException("The budget model returned no structured response.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The budget model returned invalid structured output.", exception);
        }
    }
}

public sealed class BudgetAssistantCapacity : IDisposable
{
    internal SemaphoreSlim Gate { get; } = new(4, 4);
    public void Dispose() => Gate.Dispose();
}

public sealed class UnconfiguredBudgetAssistantService : IBudgetAssistantService
{
    public Task<BudgetChatResult> ChatAsync(BudgetChatRequest request, CancellationToken ct) =>
        throw new BudgetAssistantUnavailableException();
}

public static class BudgetAssistantRegistration
{
    public static IServiceCollection AddBudgetAssistant(this IServiceCollection services, IConfiguration configuration)
    {
        var key = configuration["OpenAI:BudgetApiKey"];
        if (string.IsNullOrWhiteSpace(key))
            return services.AddScoped<IBudgetAssistantService, UnconfiguredBudgetAssistantService>();
        var model = configuration["OpenAI:BudgetModel"] ?? "gpt-6-luna";
        services.AddKeyedSingleton<IChatCompletionService>("budget-assistant", new OpenAIChatCompletionService(model, key));
        services.AddSingleton<BudgetAssistantCapacity>();
        return services.AddScoped<IBudgetAssistantService, BudgetAssistantService>();
    }
}
