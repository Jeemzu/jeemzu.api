namespace JeemzuApi.Models;

public class RefreshToken
{
    public int Id { get; set; }

    /// <summary>The opaque token value stored in the httpOnly cookie.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>The admin username this token was issued to.</summary>
    public string Username { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; set; }
    public bool IsRevoked { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
