namespace JeemzuApi.Models;

/// <summary>A single player's membership and character within a Party.</summary>
public class PartyMember
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PartyId { get; set; }
    public Party? Party { get; set; }

    public Guid UserId { get; set; }

    /// <summary>Also serves as the player_id sent to the Python RPG service.</summary>
    public string Username { get; set; } = string.Empty;

    public string CharacterName { get; set; } = string.Empty;

    /// <summary>warrior, mage, or rogue.</summary>
    public string CharacterClass { get; set; } = string.Empty;

    public bool IsHost { get; set; }

    /// <summary>Position in turn order — assigned when the game starts.</summary>
    public int TurnOrder { get; set; }

    /// <summary>Current SignalR connection ID. Updated on connect/reconnect; maps actions back to a party.
    /// Empty string when the player is disconnected but still in the party.</summary>
    public string ConnectionId { get; set; } = string.Empty;

    /// <summary>
    /// Username of the player controlling this character while the owner is disconnected.
    /// Null when the owner is connected and playing normally.
    /// </summary>
    public string? ControlledByUsername { get; set; }

    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
}
