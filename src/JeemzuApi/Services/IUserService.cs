using JeemzuApi.DTOs;

namespace JeemzuApi.Services;

public interface IUserService
{
    /// <summary>Updates OptedIn preference for an existing authenticated user.</summary>
    Task<UserResponse> UpdatePreferencesAsync(string username, UpdateUserRequest request);

    /// <summary>Returns null when the username does not exist.</summary>
    Task<UserResponse?> GetUserAsync(string username);
}
