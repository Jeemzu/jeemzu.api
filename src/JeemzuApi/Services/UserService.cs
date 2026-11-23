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

        if (request.EmailListSubscribed is bool subscribed)
            user.EmailListSubscribed = subscribed;

        user.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();

        return BuildUserResponse(user);
    }

    public async Task<UserResponse?> GetUserAsync(string username)
    {
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Username == username);

        if (user is null) return null;

        return BuildUserResponse(user);
    }

    private static UserResponse BuildUserResponse(User user)
    {
        return new UserResponse
        {
            UserId = user.Id.ToString(),
            Username = user.Username,
        };
    }
}
