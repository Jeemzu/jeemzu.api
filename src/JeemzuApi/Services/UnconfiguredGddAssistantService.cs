using JeemzuApi.DTOs;

namespace JeemzuApi.Services;

public sealed class GddAssistantUnavailableException : Exception
{
    public GddAssistantUnavailableException() : base("The dedicated GDD API key is not configured.") { }
}

public sealed class UnconfiguredGddAssistantService : IGddAssistantService
{
    public Task<GddAssistantResponse> CompleteAsync(GddAssistantRequest request, CancellationToken ct) =>
        throw new GddAssistantUnavailableException();
}
