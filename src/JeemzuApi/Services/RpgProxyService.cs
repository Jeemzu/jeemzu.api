using System.Net.Http.Json;
using System.Text.Json;
using JeemzuApi.DTOs;

namespace JeemzuApi.Services;

/// <summary>
/// HTTP proxy to the Python RPG multi-agent service (LangGraph). Converts between
/// this API's PascalCase models and the RPG service's snake_case JSON contract.
/// </summary>
public class RpgProxyService : IRpgProxyService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly HttpClient _http;

    public RpgProxyService(HttpClient http)
    {
        _http = http;
    }

    public async Task<RpgNewGameResult> CreateGameAsync(List<RpgPlayerCreate> players)
    {
        var response = await _http.PostAsJsonAsync("/rpg/new", new NewGamePayload(players), JsonOptions);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<RpgNewGameResult>(JsonOptions)
            ?? throw new InvalidOperationException("RPG service returned an empty response for /rpg/new.");
    }

    public async Task<RpgActionResult> SubmitActionAsync(string sessionId, string playerId, string action)
    {
        var response = await _http.PostAsJsonAsync(
            $"/rpg/{sessionId}/action", new ActionPayload(playerId, action), JsonOptions);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<RpgActionResult>(JsonOptions)
            ?? throw new InvalidOperationException($"RPG service returned an empty response for /rpg/{sessionId}/action.");
    }

    public async Task DeleteSessionAsync(string sessionId)
    {
        await _http.DeleteAsync($"/rpg/{sessionId}");
    }

    public async Task<JsonElement> ExportStateAsync(string sessionId)
    {
        var response = await _http.GetAsync($"/rpg/{sessionId}/export");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public async Task<RpgNewGameResult> ImportStateAsync(JsonElement state)
    {
        var response = await _http.PostAsJsonAsync("/rpg/import", new { state });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RpgNewGameResult>(JsonOptions)
            ?? throw new InvalidOperationException("RPG service returned an empty response for /rpg/import.");
    }

    private record NewGamePayload(List<RpgPlayerCreate> Players);
    private record ActionPayload(string PlayerId, string Action);
}
