using JeemzuApi.DTOs;
using JeemzuApi.Models;

namespace JeemzuApi.Services;

public interface ICampaignService
{
    /// <summary>
    /// Exports the current Python session state and saves (or updates) a campaign in the DB.
    /// Only the host can save. If existingCampaignId is set, updates that campaign; otherwise creates a new one.
    /// </summary>
    Task<Campaign> SaveAsync(Guid hostUserId, Guid? existingCampaignId, string name, string rpgSessionId);

    /// <summary>
    /// Loads a saved campaign from the DB and imports the state into the Python RPG service,
    /// returning the result for broadcasting to clients (same shape as creating a new game).
    /// </summary>
    Task<RpgNewGameResult> LoadAsync(Guid campaignId, Guid hostUserId);

    /// <summary>Lists all campaigns owned by the given user, newest first.</summary>
    Task<List<CampaignSummaryResponse>> ListAsync(Guid userId);

    /// <summary>Deletes a campaign. Returns true if deleted, false if not found or not owned.</summary>
    Task<bool> DeleteAsync(Guid campaignId, Guid userId);
}
