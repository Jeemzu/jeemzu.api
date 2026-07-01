using System.Text.Json;
using JeemzuApi.DTOs;

namespace JeemzuApi.Services;

public interface IRpgProxyService
{
    Task<RpgNewGameResult> CreateGameAsync(List<RpgPlayerCreate> players);
    Task<RpgActionResult> SubmitActionAsync(string sessionId, string playerId, string action);
    Task DeleteSessionAsync(string sessionId);

    /// <summary>Exports the full game state from the Python RPG service for persistence.</summary>
    Task<JsonElement> ExportStateAsync(string sessionId);

    /// <summary>Imports a previously saved game state into the Python RPG service, creating a new session.</summary>
    Task<RpgNewGameResult> ImportStateAsync(JsonElement state);
}
