using System.Security.Claims;
using JeemzuApi.Data;
using JeemzuApi.DTOs;
using JeemzuApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JeemzuApi.Controllers;

[ApiController]
[Route("api/scores")]
public class ScoresController : ControllerBase
{
    private readonly IScoreService _scoreService;
    private readonly AppDbContext _db;

    public ScoresController(IScoreService scoreService, AppDbContext db)
    {
        _scoreService = scoreService;
        _db = db;
    }

    // POST /api/scores — requires authentication
    [HttpPost]
    [Authorize]
    [ProducesResponseType(typeof(ScoreResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Submit([FromBody] SubmitScoreRequest request)
    {
        var username = User.FindFirstValue(ClaimTypes.Name)!;
        var result = await _scoreService.SaveScoreAsync(request, username);
        return CreatedAtAction(nameof(GetLeaderboard), new { gameId = result.GameId }, result);
    }

    // GET /api/scores/{gameId}?limit=10 — public
    [HttpGet("{gameId}")]
    [ProducesResponseType(typeof(IEnumerable<ScoreResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLeaderboard(
        [FromRoute] string gameId,
        [FromQuery] int limit = 10)
    {
        var scores = await _scoreService.GetLeaderboardAsync(gameId, limit);
        return Ok(scores);
    }

    // GET /api/scores/{gameId}/summary — public, but returns personalBest when authenticated
    [HttpGet("{gameId}/summary")]
    [ProducesResponseType(typeof(GameSummaryResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetGameSummary([FromRoute] string gameId)
    {
        Guid? userId = null;
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userIdClaim is not null && Guid.TryParse(userIdClaim, out var parsed))
            userId = parsed;

        // Fall back to username lookup if NameIdentifier claim isn't present
        if (userId is null)
        {
            var username = User.FindFirstValue(ClaimTypes.Name);
            if (username is not null)
            {
                userId = (await _db.Users.FirstOrDefaultAsync(u => u.Username == username))?.Id;
            }
        }

        var summary = await _scoreService.GetGameSummaryAsync(gameId, userId);
        return Ok(summary);
    }
}
