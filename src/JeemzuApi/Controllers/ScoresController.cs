using JeemzuApi.DTOs;
using JeemzuApi.Services;
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

    // POST /api/scores
    // Body: { gameId, username, score, timestamp }
    [HttpPost]
    [ProducesResponseType(typeof(ScoreResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Submit([FromBody] SubmitScoreRequest request)
    {
        // [ApiController] handles model validation automatically and returns 400
        // if required fields are missing or constraints are violated.
        var result = await _scoreService.SaveScoreAsync(request);
        return CreatedAtAction(nameof(GetLeaderboard), new { gameId = result.GameId }, result);
    }

    // GET /api/scores/{gameId}?limit=10
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
