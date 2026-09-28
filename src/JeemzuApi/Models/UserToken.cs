namespace JeemzuApi.Models;

/// <summary>Purpose values for <see cref="UserToken"/>. A token is only valid for the purpose it was issued for.</summary>
public static class UserTokenPurpose
{
    public const string EmailVerification = "EmailVerification";
    public const string PasswordReset = "PasswordReset";
}

/// <summary>
/// Single-use, expiring token backing email verification and password reset.
/// Only the SHA-256 hash of the token is persisted — the raw value exists solely
/// in the email link, so a database leak cannot be replayed.
/// </summary>
public class UserToken
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public User? User { get; set; }

    /// <summary>Base64 SHA-256 hash of the raw token.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>One of <see cref="UserTokenPurpose"/>.</summary>
    public string Purpose { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Set when the token is redeemed. A consumed token can never be reused.</summary>
    public DateTimeOffset? ConsumedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
