using System.Security.Claims;
using JeemzuApi.DTOs;
using JeemzuApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace JeemzuApi.Controllers;

[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly IAuthService _authService;
    private readonly IAccountService _accountService;

    public UsersController(
        IUserService userService,
        IAuthService authService,
        IAccountService accountService)
    {
        _userService = userService;
        _authService = authService;
        _accountService = accountService;
    }

    // POST /api/users/register
    [HttpPost("register")]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken ct)
    {
        try
        {
            var (token, _) = await _authService.RegisterUserAsync(request, Response);
            await _accountService.SendVerificationEmailAsync(request.Username, ct);
            return StatusCode(StatusCodes.Status201Created, token);
        }
        catch (InvalidOperationException ex) when (ex.Message == "USERNAME_TAKEN")
        {
            return Conflict(new { message = $"Username '{request.Username}' is already taken." });
        }
    }

    // POST /api/users/login
    [HttpPost("login")]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var result = await _authService.LoginUserAsync(request, Response);

        if (result is null)
            return Unauthorized(new { message = "Invalid username or password." });

        return Ok(result);
    }

    // POST /api/users — update preferences (requires auth)
    [HttpPost]
    [Authorize]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpdatePreferences([FromBody] UpdateUserRequest request)
    {
        var username = User.FindFirstValue(ClaimTypes.Name)!;
        var user = await _userService.UpdatePreferencesAsync(username, request);
        return Ok(user);
    }

    // GET /api/users/me — the signed-in user's own account settings
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(ProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetProfile()
    {
        var username = User.FindFirstValue(ClaimTypes.Name)!;
        var profile = await _accountService.GetProfileAsync(username);

        if (profile is null)
            return NotFound(new { message = "Account not found." });

        return Ok(profile);
    }

    // POST /api/users/me/preferences — leaderboard opt-in and mailing list
    [HttpPost("me/preferences")]
    [Authorize]
    [ProducesResponseType(typeof(ProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpdateProfilePreferences([FromBody] UpdateUserRequest request)
    {
        var username = User.FindFirstValue(ClaimTypes.Name)!;
        await _userService.UpdatePreferencesAsync(username, request);
        return Ok(await _accountService.GetProfileAsync(username));
    }

    // POST /api/users/me/username — rename the account; returns fresh tokens
    [HttpPost("me/username")]
    [Authorize]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ChangeUsername([FromBody] ChangeUsernameRequest request)
    {
        var username = User.FindFirstValue(ClaimTypes.Name)!;

        try
        {
            return Ok(await _accountService.ChangeUsernameAsync(username, request, Response));
        }
        catch (InvalidOperationException ex) when (ex.Message == "USERNAME_TAKEN")
        {
            return Conflict(new { message = $"Username '{request.Username}' is already taken." });
        }
    }

    // POST /api/users/me/password — change password while signed in
    [HttpPost("me/password")]
    [Authorize]
    [EnableRateLimiting("account-user")]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var username = User.FindFirstValue(ClaimTypes.Name)!;
        var token = await _accountService.ChangePasswordAsync(username, request, Response);

        if (token is null)
            return Unauthorized(new { message = "Current password is incorrect." });

        return Ok(token);
    }

    // POST /api/users/me/resend-verification
    [HttpPost("me/resend-verification")]
    [Authorize]
    [EnableRateLimiting("account-user")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ResendVerification(CancellationToken ct)
    {
        var username = User.FindFirstValue(ClaimTypes.Name)!;
        await _accountService.SendVerificationEmailAsync(username, ct);
        return Accepted();
    }

    // POST /api/users/verify-email — completes registration's email confirmation
    [HttpPost("verify-email")]
    [EnableRateLimiting("account-public")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailRequest request)
    {
        return await _accountService.VerifyEmailAsync(request.Token) switch
        {
            VerifyEmailResult.Success => NoContent(),
            VerifyEmailResult.AddressTaken => Conflict(new
            {
                message = "That address is already verified on another account."
            }),
            _ => BadRequest(new { message = "This verification link is invalid or has expired." }),
        };
    }

    // POST /api/users/forgot-password
    // Always 202 — the response must not reveal whether an account exists.
    [HttpPost("forgot-password")]
    [EnableRateLimiting("account-public")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken ct)
    {
        await _accountService.RequestPasswordResetAsync(request.Email, ct);
        return Accepted();
    }

    // POST /api/users/reset-password
    [HttpPost("reset-password")]
    [EnableRateLimiting("account-public")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        if (!await _accountService.ResetPasswordAsync(request))
            return BadRequest(new { message = "This reset link is invalid or has expired." });

        return NoContent();
    }

    // GET /api/users/{username}
    [HttpGet("{username}")]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUser([FromRoute] string username)
    {
        var user = await _userService.GetUserAsync(username);

        if (user is null)
            return NotFound(new { message = $"User '{username}' not found." });

        return Ok(user);
    }
}
