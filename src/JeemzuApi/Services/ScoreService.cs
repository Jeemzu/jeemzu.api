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
        var gameId = request.GameId.ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username);

        // Authenticated users get one score per game — upsert, only update if new score is higher
        if (user is not null)
        {
            var existing = await _db.Scores
                .FirstOrDefaultAsync(s => s.UserId == user.Id && s.GameId == gameId);

            if (existing is not null)
            {
                if (request.Score <= existing.ScoreValue)
                    return MapToResponse(existing); // no improvement, return current best

                existing.ScoreValue = request.Score;
                existing.Timestamp = request.Timestamp;
                await _db.SaveChangesAsync();
                return MapToResponse(existing);
            }
        }

        var score = new Score
        {
            GameId = gameId,
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

    public async Task<GameSummaryResponse> GetGameSummaryAsync(string gameId, Guid? userId)
    {
        var normalizedId = gameId.ToLowerInvariant();

        var allTimeRecord = await _db.Scores
            .Where(s => s.GameId == normalizedId)
            .OrderByDescending(s => s.ScoreValue)
            .FirstOrDefaultAsync();

        int? personalBest = null;
        if (userId.HasValue)
        {
            personalBest = await _db.Scores
                .Where(s => s.GameId == normalizedId && s.UserId == userId)
                .Select(s => (int?)s.ScoreValue)
                .FirstOrDefaultAsync();
        }

        return new GameSummaryResponse
        {
            AllTimeRecord = allTimeRecord is null ? null : MapToResponse(allTimeRecord),
            PersonalBest = personalBest,
        };
    }

    private static ScoreResponse MapToResponse(Score s) => new()
    {
        GameId = s.GameId,
        Username = s.Username,
        Score = s.ScoreValue,
        Timestamp = s.Timestamp,
    };
}
