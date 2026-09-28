using System.Security.Cryptography;
using System.Text;
using JeemzuApi.Data;
using JeemzuApi.DTOs;
using JeemzuApi.Models;
using Microsoft.EntityFrameworkCore;

namespace JeemzuApi.Services;

public class AccountService : IAccountService
{
    private static readonly TimeSpan VerificationLifetime = TimeSpan.FromHours(24);
    private static readonly TimeSpan ResetLifetime = TimeSpan.FromHours(1);

    private readonly AppDbContext _db;
    private readonly IAuthService _auth;
    private readonly IEmailService _email;
    private readonly ILogger<AccountService> _logger;
    private readonly string _frontendBaseUrl;

    public AccountService(
        AppDbContext db,
        IAuthService auth,
        IEmailService email,
        IConfiguration config,
        ILogger<AccountService> logger)
    {
        _db = db;
        _auth = auth;
        _email = email;
        _logger = logger;
        _frontendBaseUrl = (config["Frontend:BaseUrl"] ?? "https://jeemzu.me").TrimEnd('/');
    }

    public async Task<ProfileResponse?> GetProfileAsync(string username)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == username);
        return user is null ? null : ToProfile(user);
    }

    public async Task<TokenResponse> ChangeUsernameAsync(
        string username, ChangeUsernameRequest request, HttpResponse response)
    {
        var newUsername = request.Username.Trim();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username)
            ?? throw new InvalidOperationException($"User '{username}' not found.");

        if (string.Equals(user.Username, newUsername, StringComparison.Ordinal))
            return await _auth.IssueTokensAsync(user.Username, user.Role, response);

        if (await _db.Users.AnyAsync(u => u.Username == newUsername))
            throw new InvalidOperationException("USERNAME_TAKEN");

        // Username is denormalised onto gameplay rows, so rename them in the same transaction.
        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            user.Username = newUsername;
            user.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync();

            await _db.Scores.Where(s => s.Username == username)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Username, newUsername));
            await _db.PartyMembers.Where(m => m.Username == username)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Username, newUsername));
            await _db.PartyMembers.Where(m => m.ControlledByUsername == username)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.ControlledByUsername, newUsername));
            await _db.Parties.Where(p => p.CurrentTurnUsername == username)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.CurrentTurnUsername, newUsername));

            await tx.CommitAsync();
        }
        catch (DbUpdateException)
        {
            await tx.RollbackAsync();
            throw new InvalidOperationException("USERNAME_TAKEN");
        }

        // Refresh tokens are keyed by username, so old sessions can no longer resolve this account.
        await _auth.RevokeAllRefreshTokensAsync(username);
        return await _auth.IssueTokensAsync(newUsername, user.Role, response);
    }

    public async Task<TokenResponse?> ChangePasswordAsync(
        string username, ChangePasswordRequest request, HttpResponse response)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username)
            ?? throw new InvalidOperationException($"User '{username}' not found.");

        if (user.PasswordHash is null ||
            !BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
            return null;

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword, workFactor: 12);
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();

        await _auth.RevokeAllRefreshTokensAsync(username);
        return await _auth.IssueTokensAsync(user.Username, user.Role, response);
    }

    public async Task SendVerificationEmailAsync(string username, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username, ct);

        if (user?.Email is null || user.EmailVerifiedAt is not null)
            return;

        var raw = await CreateTokenAsync(user.Id, UserTokenPurpose.EmailVerification, VerificationLifetime, ct);
        var link = BuildLink("verify-email", raw);

        await TrySendAsync(
            () => _email.SendEmailVerificationAsync(user.Email, user.Username, link, ct),
            "verification", user.Id);
    }

    public async Task<VerifyEmailResult> VerifyEmailAsync(string token)
    {
        var stored = await FindUsableTokenAsync(token, UserTokenPurpose.EmailVerification);
        if (stored?.User?.Email is null)
            return VerifyEmailResult.InvalidOrExpired;

        var user = stored.User;

        if (await _db.Users.AnyAsync(u =>
                u.Id != user.Id && u.Email == user.Email && u.EmailVerifiedAt != null))
            return VerifyEmailResult.AddressTaken;

        if (!await ConsumeTokenAsync(stored.Id))
            return VerifyEmailResult.InvalidOrExpired;

        user.EmailVerifiedAt = DateTimeOffset.UtcNow;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();

        return VerifyEmailResult.Success;
    }

    public async Task RequestPasswordResetAsync(string email, CancellationToken ct = default)
    {
        var normalized = email.Trim().ToLowerInvariant();

        // Unverified addresses are skipped: we can't prove the requester owns them.
        var user = await _db.Users.FirstOrDefaultAsync(
            u => u.Email == normalized && u.EmailVerifiedAt != null, ct);

        if (user?.Email is null)
            return;

        var raw = await CreateTokenAsync(user.Id, UserTokenPurpose.PasswordReset, ResetLifetime, ct);
        var link = BuildLink("reset-password", raw);

        await TrySendAsync(
            () => _email.SendPasswordResetAsync(user.Email, user.Username, link, ct),
            "password reset", user.Id);
    }

    public async Task<bool> ResetPasswordAsync(ResetPasswordRequest request)
    {
        var stored = await FindUsableTokenAsync(request.Token, UserTokenPurpose.PasswordReset);
        if (stored?.User is null)
            return false;

        if (!await ConsumeTokenAsync(stored.Id))
            return false;

        var user = stored.User;
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword, workFactor: 12);
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();

        await _auth.RevokeAllRefreshTokensAsync(user.Username);
        return true;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ProfileResponse ToProfile(User user) => new()
    {
        Username = user.Username,
        Email = user.Email,
        EmailVerified = user.EmailVerifiedAt is not null,
        OptedIn = user.OptedIn,
        EmailListSubscribed = user.EmailListSubscribed,
        Role = user.Role,
        CreatedAt = user.CreatedAt,
    };

    private string BuildLink(string path, string rawToken) =>
        $"{_frontendBaseUrl}/{path}?token={Uri.EscapeDataString(rawToken)}";

    /// <summary>Mail delivery never fails the caller — registration must succeed and reset replies stay uniform.</summary>
    private async Task TrySendAsync(Func<Task<bool>> send, string kind, Guid userId)
    {
        try
        {
            if (!await send())
                _logger.LogWarning("Could not send {Kind} email for user {UserId}.", kind, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending {Kind} email for user {UserId}.", kind, userId);
        }
    }

    private static string Hash(string rawToken) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

    /// <summary>Issues a new token and invalidates any earlier unused token of the same purpose.</summary>
    private async Task<string> CreateTokenAsync(
        Guid userId, string purpose, TimeSpan lifetime, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        await _db.UserTokens
            .Where(t => t.UserId == userId && t.Purpose == purpose && t.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConsumedAt, now), ct);

        var raw = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

        _db.UserTokens.Add(new UserToken
        {
            UserId = userId,
            TokenHash = Hash(raw),
            Purpose = purpose,
            ExpiresAt = now.Add(lifetime),
        });
        await _db.SaveChangesAsync(ct);

        return raw;
    }

    private async Task<UserToken?> FindUsableTokenAsync(string rawToken, string purpose)
    {
        var hash = Hash(rawToken);

        return await _db.UserTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t =>
                t.TokenHash == hash &&
                t.Purpose == purpose &&
                t.ConsumedAt == null &&
                t.ExpiresAt > DateTimeOffset.UtcNow);
    }

    /// <summary>Marks the token used. The ConsumedAt filter makes concurrent redemptions lose the race.</summary>
    private async Task<bool> ConsumeTokenAsync(Guid tokenId)
    {
        var now = DateTimeOffset.UtcNow;
        var affected = await _db.UserTokens
            .Where(t => t.Id == tokenId && t.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConsumedAt, now));

        return affected == 1;
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
