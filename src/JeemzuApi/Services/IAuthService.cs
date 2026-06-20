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
}
