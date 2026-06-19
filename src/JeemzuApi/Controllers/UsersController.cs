using JeemzuApi.DTOs;
using JeemzuApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace JeemzuApi.Controllers;

[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;

    public UsersController(IUserService userService)
    {
        _userService = userService;
    }

    // POST /api/users
    // Body: { username, optedIn }
    // Returns 201 on create, 200 on update (upsert behaviour)
    [HttpPost]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Upsert([FromBody] UpdateUserRequest request)
    {
        var (user, wasCreated) = await _userService.UpsertUserAsync(request);

        if (wasCreated)
            return CreatedAtAction(nameof(GetUser), new { username = user.Username }, user);

        return Ok(user);
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
