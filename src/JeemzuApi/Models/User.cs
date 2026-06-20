namespace JeemzuApi.Models;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Unique display name chosen by the player. Indexed in DB.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Whether the player has opted into global leaderboards.</summary>
    public bool OptedIn { get; set; }

    /// <summary>BCrypt hash of the user's password. Null for legacy/guest accounts.</summary>
    public string? PasswordHash { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
