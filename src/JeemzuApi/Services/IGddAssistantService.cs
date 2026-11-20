using JeemzuApi.DTOs;

namespace JeemzuApi.Services;

public interface IGddAssistantService
{
    Task<GddAssistantResponse> CompleteAsync(GddAssistantRequest request, CancellationToken ct);
    Task<GddGuidedAssistantResponse> CompleteGuidedAsync(GddGuidedAssistantRequest request, CancellationToken ct);
}
