using JeemzuApi.Data;
using JeemzuApi.DTOs;
using JeemzuApi.Models;
using Microsoft.EntityFrameworkCore;

namespace JeemzuApi.Services;

public class UserService : IUserService
{
    private readonly AppDbContext _db;

    public UserService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<UserResponse> UpdatePreferencesAsync(string username, UpdateUserRequest request)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username)
            ?? throw new InvalidOperationException($"User '{username}' not found.");

        user.OptedIn = request.OptedIn;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();

        return await BuildUserResponseAsync(user);
    }

    public async Task<UserResponse?> GetUserAsync(string username)
    {
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Username == username);

        if (user is null) return null;

        return await BuildUserResponseAsync(user);
    }

    /// <summary>
    /// Assembles the UserResponse including the highScores dictionary.
    /// For each game the user has played, picks their single best score.
    /// </summary>
    private async Task<UserResponse> BuildUserResponseAsync(User user)
    {
        var highScores = await _db.Scores
            .Where(s => s.Username == user.Username)
            .GroupBy(s => s.GameId)
            .Select(g => new { GameId = g.Key, Best = g.Max(s => s.ScoreValue) })
            .ToDictionaryAsync(x => x.GameId, x => x.Best);

        return new UserResponse
        {
            UserId = user.Id.ToString(),
            Username = user.Username,
            OptedIn = user.OptedIn,
            HighScores = highScores,
        };
    }
}
