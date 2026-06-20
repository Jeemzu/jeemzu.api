using JeemzuApi.DTOs;
using JeemzuApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace JeemzuApi.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private const string RefreshTokenCookie = "refreshToken";
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    // POST /api/auth/refresh
    // Reads the httpOnly refreshToken cookie — no body needed
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh()
    {
        var refreshToken = Request.Cookies[RefreshTokenCookie];

        if (string.IsNullOrEmpty(refreshToken))
            return Unauthorized(new { message = "No refresh token present." });

        var result = await _authService.RefreshAsync(refreshToken, Response);

        if (result is null)
            return Unauthorized(new { message = "Refresh token is invalid or expired." });

        return Ok(result);
    }

    // POST /api/auth/logout
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout()
    {
        var refreshToken = Request.Cookies[RefreshTokenCookie];

        if (!string.IsNullOrEmpty(refreshToken))
            await _authService.LogoutAsync(refreshToken, Response);

        return NoContent();
    }
}
