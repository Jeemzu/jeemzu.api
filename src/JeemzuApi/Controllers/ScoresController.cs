using System.Security.Claims;
using JeemzuApi.DTOs;
using JeemzuApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JeemzuApi.Controllers;

[ApiController]
[Route("api/scores")]
public class ScoresController : ControllerBase
{
    private readonly IScoreService _scoreService;

    public ScoresController(IScoreService scoreService)
    {
        _scoreService = scoreService;
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
}
