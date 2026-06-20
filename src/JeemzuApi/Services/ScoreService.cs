using JeemzuApi.Data;
using JeemzuApi.DTOs;
using JeemzuApi.Models;
using Microsoft.EntityFrameworkCore;

namespace JeemzuApi.Services;

public class ScoreService : IScoreService
{
    private readonly AppDbContext _db;

    public ScoreService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<ScoreResponse> SaveScoreAsync(SubmitScoreRequest request, string username)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username);

        var score = new Score
        {
            GameId = request.GameId.ToLowerInvariant(),
            Username = username,
            UserId = user?.Id,
            ScoreValue = request.Score,
            Timestamp = request.Timestamp,
        };

        _db.Scores.Add(score);
        await _db.SaveChangesAsync();

        return MapToResponse(score);
    }

    public async Task<IEnumerable<ScoreResponse>> GetLeaderboardAsync(string gameId, int limit)
    {
        // Clamp limit so callers can't request unbounded result sets
        limit = Math.Clamp(limit, 1, 100);

        var scores = await _db.Scores
            .Where(s => s.GameId == gameId.ToLowerInvariant())
            .OrderByDescending(s => s.ScoreValue)
            .Take(limit)
            .ToListAsync();

        return scores.Select(MapToResponse);
    }

    private static ScoreResponse MapToResponse(Score s) => new()
    {
        GameId = s.GameId,
        Username = s.Username,
        Score = s.ScoreValue,
        Timestamp = s.Timestamp,
    };
}
