using JeemzuApi.Models;

namespace JeemzuApi.Services;

public interface ITurnService
{
    /// <summary>
    /// True if the given player may act right now. Outside combat (exploration/dialogue),
    /// any party member may act. During combat, only the player whose turn it is may act.
    /// </summary>
    bool IsPlayerTurn(Party party, string username);

    /// <summary>Starts a 30-second timeout for the given player's turn. Invokes onTimeout if not cancelled first.</summary>
    void ScheduleTurnTimeout(Guid partyId, string username, Func<Guid, Task> onTimeout);

    /// <summary>Cancels any pending timeout for the party. Call whenever an action is received.</summary>
    void CancelTurnTimeout(Guid partyId);
}
