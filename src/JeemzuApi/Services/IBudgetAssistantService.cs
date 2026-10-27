using JeemzuApi.DTOs;

namespace JeemzuApi.Services;

public interface IBudgetAssistantService
{
    Task<BudgetChatResult> ChatAsync(BudgetChatRequest request, CancellationToken ct);
}
