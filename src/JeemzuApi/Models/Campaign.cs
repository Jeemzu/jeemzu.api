namespace JeemzuApi.Models;

/// <summary>
/// A host-owned campaign save file. The host has 100% authority over the save —
/// other players join the host's session to control characters during play.
/// Inspired by Divinity: Original Sin 2's save file ownership model.
/// </summary>
public class Campaign
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The user who owns this save file — only they can save, load, or delete it.</summary>
    public Guid HostUserId { get; set; }
    public User? HostUser { get; set; }

    /// <summary>Display name for the campaign (e.g. "Crypt Crawlers").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Full serialized game state from the Python RPG service (JSON blob).</summary>
    public string GameStateJson { get; set; } = string.Empty;

    /// <summary>JSON array of character summaries for the campaign list UI: [{name, class, level}].</summary>
    public string CharacterSummaryJson { get; set; } = "[]";

    /// <summary>Last known location for display in campaign list.</summary>
    public string CurrentLocation { get; set; } = string.Empty;

    /// <summary>Active or Completed.</summary>
    public string Status { get; set; } = "Active";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastPlayedAt { get; set; } = DateTimeOffset.UtcNow;
}
