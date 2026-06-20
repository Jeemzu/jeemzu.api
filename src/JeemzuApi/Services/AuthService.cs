using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using JeemzuApi.Data;
using JeemzuApi.DTOs;
using JeemzuApi.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace JeemzuApi.Services;

public class AuthService : IAuthService
{
    private const string RefreshTokenCookie = "refreshToken";
    private const int AccessTokenMinutes = 60;
    private const int RefreshTokenDays = 30;

    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public AuthService(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public async Task<TokenResponse?> LoginAsync(LoginRequest request, HttpResponse response)
    {
        var adminUsername = _config["Admin:Username"];
        var adminPasswordHash = _config["Admin:PasswordHash"];

        if (adminUsername is null || adminPasswordHash is null)
            throw new InvalidOperationException("Admin credentials are not configured.");

        if (!string.Equals(request.Username, adminUsername, StringComparison.OrdinalIgnoreCase))
            return null;

        if (!BCrypt.Net.BCrypt.Verify(request.Password, adminPasswordHash))
            return null;

        return await IssueTokensAsync(adminUsername, "Admin", response);
    }

    public async Task<(TokenResponse Token, bool WasCreated)> RegisterUserAsync(
        RegisterRequest request, HttpResponse response)
    {
        var existing = await _db.Users
            .FirstOrDefaultAsync(u => u.Username == request.Username);

        if (existing is not null)
        {
            // Username taken — return 409 signal to caller
            throw new InvalidOperationException("USERNAME_TAKEN");
        }

        var user = new JeemzuApi.Models.User
        {
            Username = request.Username,
            OptedIn = request.OptedIn,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, workFactor: 12),
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var token = await IssueTokensAsync(user.Username, "User", response);
        return (token, true);
    }

    public async Task<TokenResponse?> LoginUserAsync(LoginRequest request, HttpResponse response)
    {
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Username == request.Username);

        if (user is null || user.PasswordHash is null)
            return null;

        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return null;

        return await IssueTokensAsync(user.Username, "User", response);
    }

    public async Task<TokenResponse?> RefreshAsync(string refreshToken, HttpResponse response)
    {
        var stored = await _db.RefreshTokens
            .FirstOrDefaultAsync(r => r.Token == refreshToken && !r.IsRevoked);

        if (stored is null || stored.ExpiresAt <= DateTimeOffset.UtcNow)
            return null;

        // Rotate: revoke the old token and issue a new pair
        stored.IsRevoked = true;
        await _db.SaveChangesAsync();

        return await IssueTokensAsync(stored.Username, "Admin", response);
    }

    public async Task LogoutAsync(string refreshToken, HttpResponse response)
    {
        var stored = await _db.RefreshTokens
            .FirstOrDefaultAsync(r => r.Token == refreshToken);

        if (stored is not null)
        {
            stored.IsRevoked = true;
            await _db.SaveChangesAsync();
        }

        response.Cookies.Delete(RefreshTokenCookie, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.None,
            Path = "/api/auth"
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<TokenResponse> IssueTokensAsync(
        string username, string role, HttpResponse response)
    {
        var accessToken = BuildAccessToken(username, role);
        var refreshToken = await StoreRefreshTokenAsync(username);

        response.Cookies.Append(RefreshTokenCookie, refreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.None,
            Expires = DateTimeOffset.UtcNow.AddDays(RefreshTokenDays),
            Path = "/api/auth"
        });

        return new TokenResponse
        {
            AccessToken = accessToken,
            ExpiresIn = AccessTokenMinutes * 60,
            Role = role
        };
    }

    private string BuildAccessToken(string username, string role)
    {
        var secret = _config["Jwt:Secret"]
            ?? throw new InvalidOperationException("Jwt:Secret is not configured.");
        var issuer = _config["Jwt:Issuer"] ?? "jeemzu-api";
        var audience = _config["Jwt:Audience"] ?? "jeemzu-frontend";

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Role, role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(AccessTokenMinutes),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private async Task<string> StoreRefreshTokenAsync(string username)
    {
        var tokenValue = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

        _db.RefreshTokens.Add(new RefreshToken
        {
            Token = tokenValue,
            Username = username,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(RefreshTokenDays),
        });

        await _db.SaveChangesAsync();
        return tokenValue;
    }
}
