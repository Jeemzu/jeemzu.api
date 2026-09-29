using JeemzuApi.DTOs;

namespace JeemzuApi.Services;

/// <summary>HTTP proxy to the Python Budgetize assistant service (LangGraph).</summary>
public interface IBudgetAgentProxyService
{
    Task<BudgetChatResult> ChatAsync(BudgetChatPayload payload, CancellationToken ct);
}
