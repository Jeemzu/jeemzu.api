namespace JeemzuApi.Models;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Unique display name chosen by the player. Indexed in DB.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Whether the player has opted into global leaderboards.</summary>
    public bool OptedIn { get; set; }

    /// <summary>
    /// Recovery address, stored lower-cased. Null for accounts created before
    /// email was collected at registration.
    /// </summary>
    public string? Email { get; set; }

    /// <summary>Set once the user follows their verification link. Password reset requires this.</summary>
    public DateTimeOffset? EmailVerifiedAt { get; set; }

    /// <summary>Whether the user opted into the notification mailing list.</summary>
    public bool EmailListSubscribed { get; set; }

    /// <summary>Role for authorization. Valid values: "User", "Admin". Default: "User".</summary>
    public string Role { get; set; } = "User";

    /// <summary>BCrypt hash of the user's password. Null for legacy/guest accounts.</summary>
    public string? PasswordHash { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
