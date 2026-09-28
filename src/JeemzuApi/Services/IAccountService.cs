using JeemzuApi.DTOs;

namespace JeemzuApi.Services;

public enum VerifyEmailResult
{
    Success,
    InvalidOrExpired,
    /// <summary>Another account has already verified this address.</summary>
    AddressTaken,
}

public interface IAccountService
{
    /// <summary>Returns null when the username does not exist.</summary>
    Task<ProfileResponse?> GetProfileAsync(string username);

    /// <summary>Renames the account and reissues tokens. Throws InvalidOperationException("USERNAME_TAKEN").</summary>
    Task<TokenResponse> ChangeUsernameAsync(string username, ChangeUsernameRequest request, HttpResponse response);

    /// <summary>Returns null when the current password is wrong. Signs out all other sessions on success.</summary>
    Task<TokenResponse?> ChangePasswordAsync(string username, ChangePasswordRequest request, HttpResponse response);

    /// <summary>Issues a verification link. No-op when the account has no address or is already verified.</summary>
    Task SendVerificationEmailAsync(string username, CancellationToken ct = default);

    Task<VerifyEmailResult> VerifyEmailAsync(string token);

    /// <summary>Sends a reset link only for a verified address. Always completes quietly so callers can't enumerate accounts.</summary>
    Task RequestPasswordResetAsync(string email, CancellationToken ct = default);

    /// <summary>Returns false when the token is unknown, expired, or already used.</summary>
    Task<bool> ResetPasswordAsync(ResetPasswordRequest request);
}
