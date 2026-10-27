using JeemzuApi.DTOs;
using JeemzuApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.SemanticKernel;

namespace JeemzuApi.Controllers;

[ApiController]
[Route("api/gdd-assistant")]
[Authorize]
[EnableRateLimiting("gdd-assistant")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class GddAssistantController(
    IGddAssistantService assistant, ILogger<GddAssistantController> logger) : ControllerBase
{
    /// <summary>Discusses one checkpoint and proposes text for explicit client-side approval.</summary>
    [HttpPost]
    [RequestSizeLimit(2 * 1024 * 1024)]
    [ProducesResponseType<GddAssistantResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status504GatewayTimeout)]
    public async Task<ActionResult<GddAssistantResponse>> Complete(GddAssistantRequest request, CancellationToken ct)
    {
        var error = GddAssistantService.ValidateRequest(request);
        if (error is not null) return Problem(statusCode: 400, detail: error);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        try
        {
            return Ok(await assistant.CompleteAsync(request, timeout.Token));
        }
        catch (GddAssistantUnavailableException exception)
        {
            logger.LogWarning(exception, "GDD assistant is unavailable because its dedicated API key is missing.");
            return Problem(statusCode: 503, detail: "The GDD assistant is not configured. Set its dedicated OpenAI key on the server. Your draft is unchanged.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("GDD assistant request timed out.");
            return Problem(statusCode: 504, detail: "The assistant timed out. Your draft is unchanged; try again.");
        }
        catch (Exception exception) when (exception is HttpOperationException or HttpRequestException or InvalidDataException)
        {
            logger.LogWarning(exception, "GDD assistant completion failed.");
            return Problem(statusCode: 502, detail: "The assistant could not produce a valid response. Your draft is unchanged; try again.");
        }
    }
}
