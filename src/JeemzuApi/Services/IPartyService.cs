using JeemzuApi.Models;

namespace JeemzuApi.Services;

public interface IPartyService
{
    Task<Party> CreatePartyAsync(Guid hostUserId, string hostUsername, string characterName, string characterClass, string connectionId);

    /// <summary>
    /// Throws InvalidOperationException("PARTY_ALREADY_STARTED") or ("PARTY_FULL") for known failure modes.
    /// Returns null if no party exists with the given code.
    /// </summary>
    Task<Party?> JoinPartyAsync(string code, Guid userId, string username, string characterName, string characterClass, string connectionId);

    Task<Party?> GetPartyAsync(Guid partyId);
    Task<Party?> GetPartyByConnectionIdAsync(string connectionId);

    /// <summary>Removes the member mapped to this connection. Returns the party and whether it was disbanded (emptied).</summary>
    Task<(Party? Party, bool Disbanded)> LeavePartyAsync(string connectionId);

    Task<Party> StartGameAsync(Guid partyId, string rpgSessionId, string initialGamePhase);

    Task UpdatePartyStateAsync(Guid partyId, string gamePhase, string? currentTurnUsername);
}
