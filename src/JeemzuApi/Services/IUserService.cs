using JeemzuApi.DTOs;

namespace JeemzuApi.Services;

public interface IUserService
{
    /// <summary>Updates the preferences supplied on the request; omitted fields are left unchanged.</summary>
    Task<UserResponse> UpdatePreferencesAsync(string username, UpdateUserRequest request);

    /// <summary>Returns null when the username does not exist.</summary>
    Task<UserResponse?> GetUserAsync(string username);
}
