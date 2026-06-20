using JeemzuApi.DTOs;

namespace JeemzuApi.Services;

public interface IAuthService
{
    // Admin
    Task<TokenResponse?> LoginAsync(LoginRequest request, HttpResponse response);

    // Users
    Task<(TokenResponse Token, bool WasCreated)> RegisterUserAsync(RegisterRequest request, HttpResponse response);
    Task<TokenResponse?> LoginUserAsync(LoginRequest request, HttpResponse response);

    // Shared
    Task<TokenResponse?> RefreshAsync(string refreshToken, HttpResponse response);
    Task LogoutAsync(string refreshToken, HttpResponse response);
}
