using System.Collections.Concurrent;
using JeemzuApi.Models;

namespace JeemzuApi.Services;

/// <summary>
/// Validates turn order and manages per-party turn timeout timers.
/// Registered as a singleton — timers must persist across the lifetime of scoped Hub
/// invocations. In-memory only: fine for a single-instance deployment (no SignalR backplane).
/// </summary>
public class TurnService : ITurnService
{
    private const int TurnTimeoutSeconds = 30;

    private readonly ConcurrentDictionary<Guid, Timer> _timers = new();

    public bool IsPlayerTurn(Party party, string username)
    {
        // Outside combat, anyone in the party may act.
        if (party.CurrentGamePhase != "combat") return true;

        // During combat, only the current turn's player may act. If we don't know whose
        // turn it is yet, fail open rather than blocking play unexpectedly.
        return party.CurrentTurnUsername is null || party.CurrentTurnUsername == username;
    }

    public void ScheduleTurnTimeout(Guid partyId, string username, Func<Guid, Task> onTimeout)
    {
        CancelTurnTimeout(partyId);

        var timer = new Timer(
            _ => _ = onTimeout(partyId),
            state: null,
            dueTime: TimeSpan.FromSeconds(TurnTimeoutSeconds),
            period: Timeout.InfiniteTimeSpan);

        _timers[partyId] = timer;
    }

    public void CancelTurnTimeout(Guid partyId)
    {
        if (_timers.TryRemove(partyId, out var timer))
        {
            timer.Dispose();
        }
    }
}
