namespace JeemzuApi.Models;

/// <summary>
/// A multiplayer RPG party — a lobby of 1-4 players that plays through an AI Game Master
/// session together. Maps 1:1 to a session in the Python RPG orchestration service.
/// </summary>
public class Party
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>6-character join code (e.g. "CRYPT7"). Unique among non-completed parties.</summary>
    public string Code { get; set; } = string.Empty;

    public Guid HostUserId { get; set; }

    /// <summary>Lobby, InGame, or Completed.</summary>
    public string Status { get; set; } = "Lobby";

    public int MaxPlayers { get; set; } = 4;

    /// <summary>Session ID in the Python RPG service. Set once the game starts.</summary>
    public string? RpgSessionId { get; set; }

    /// <summary>
    /// Cached from the most recent RPG service response so TurnService can validate
    /// actions without an extra round trip. "exploration", "dialogue", or "combat".
    /// </summary>
    public string CurrentGamePhase { get; set; } = "exploration";

    /// <summary>Username of the player whose turn it is. Null when any party member may act.</summary>
    public string? CurrentTurnUsername { get; set; }

    /// <summary>Links to the campaign save file, if any. Set when creating from a load or first save.</summary>
    public Guid? CampaignId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<PartyMember> Members { get; set; } = [];
}
