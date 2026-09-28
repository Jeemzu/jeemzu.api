using JeemzuApi.DTOs;

namespace JeemzuApi.Services;

public interface IAuthService
{
    // Users
    Task<(TokenResponse Token, bool WasCreated)> RegisterUserAsync(RegisterRequest request, HttpResponse response);
    Task<TokenResponse?> LoginUserAsync(LoginRequest request, HttpResponse response);

    // Shared
    Task<TokenResponse?> RefreshAsync(string refreshToken, HttpResponse response);
    Task LogoutAsync(string refreshToken, HttpResponse response);

    /// <summary>Issues a fresh access token and refresh cookie. Used after a username or password change.</summary>
    Task<TokenResponse> IssueTokensAsync(string username, string role, HttpResponse response);

    /// <summary>Revokes every outstanding refresh token for a username, signing out all other sessions.</summary>
    Task RevokeAllRefreshTokensAsync(string username);
}
