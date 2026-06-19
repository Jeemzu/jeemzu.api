using JeemzuApi.DTOs;

namespace JeemzuApi.Services;

public interface IUserService
{
    /// <summary>
    /// Creates or updates a user record.
    /// Returns (user, wasCreated) so the controller can return 201 vs 200.
    /// </summary>
    Task<(UserResponse User, bool WasCreated)> UpsertUserAsync(UpdateUserRequest request);

    /// <summary>Returns null when the username does not exist.</summary>
    Task<UserResponse?> GetUserAsync(string username);
}
