using JeemzuApi.DTOs;

namespace JeemzuApi.Services;

public interface IAuthService
{
    Task<TokenResponse?> LoginAsync(LoginRequest request, HttpResponse response);
    Task<TokenResponse?> RefreshAsync(string refreshToken, HttpResponse response);
    Task LogoutAsync(string refreshToken, HttpResponse response);
}
