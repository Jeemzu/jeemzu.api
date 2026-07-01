using System.Text.Json;
using JeemzuApi.Data;
using JeemzuApi.DTOs;
using JeemzuApi.Models;
using Microsoft.EntityFrameworkCore;

namespace JeemzuApi.Services;

public class CampaignService : ICampaignService
{
    private readonly AppDbContext _db;
    private readonly IRpgProxyService _rpgProxy;

    public CampaignService(AppDbContext db, IRpgProxyService rpgProxy)
    {
        _db = db;
        _rpgProxy = rpgProxy;
    }

    public async Task<Campaign> SaveAsync(Guid hostUserId, Guid? existingCampaignId, string name, string rpgSessionId)
    {
        // Export current state from the Python RPG service
        var stateJson = await _rpgProxy.ExportStateAsync(rpgSessionId);

        Campaign campaign;
        if (existingCampaignId.HasValue)
        {
            campaign = await _db.Campaigns.FirstOrDefaultAsync(c => c.Id == existingCampaignId.Value && c.HostUserId == hostUserId)
                ?? throw new InvalidOperationException("CAMPAIGN_NOT_FOUND");
            campaign.GameStateJson = stateJson.GetRawText();
            campaign.UpdatedAt = DateTimeOffset.UtcNow;
            campaign.LastPlayedAt = DateTimeOffset.UtcNow;
            // Keep existing name unless a new one is provided
            if (!string.IsNullOrWhiteSpace(name))
                campaign.Name = name;
        }
        else
        {
            campaign = new Campaign
            {
                HostUserId = hostUserId,
                Name = string.IsNullOrWhiteSpace(name) ? $"Campaign {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm}" : name,
                GameStateJson = stateJson.GetRawText(),
            };
            _db.Campaigns.Add(campaign);
        }

        // Extract display-friendly metadata from the state
        ExtractCampaignMetadata(campaign, stateJson);

        await _db.SaveChangesAsync();
        return campaign;
    }

    public async Task<RpgNewGameResult> LoadAsync(Guid campaignId, Guid hostUserId)
    {
        var campaign = await _db.Campaigns.FirstOrDefaultAsync(c => c.Id == campaignId && c.HostUserId == hostUserId)
            ?? throw new InvalidOperationException("CAMPAIGN_NOT_FOUND");

        var stateElement = JsonSerializer.Deserialize<JsonElement>(campaign.GameStateJson);
        var result = await _rpgProxy.ImportStateAsync(stateElement);

        campaign.LastPlayedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();

        return result;
    }

    public async Task<List<CampaignSummaryResponse>> ListAsync(Guid userId)
    {
        return await _db.Campaigns
            .Where(c => c.HostUserId == userId && c.Status == "Active")
            .OrderByDescending(c => c.LastPlayedAt)
            .Select(c => new CampaignSummaryResponse
            {
                Id = c.Id,
                Name = c.Name,
                CurrentLocation = c.CurrentLocation,
                CharacterSummaryJson = c.CharacterSummaryJson,
                Status = c.Status,
                LastPlayedAt = c.LastPlayedAt,
                CreatedAt = c.CreatedAt,
            })
            .ToListAsync();
    }

    public async Task<bool> DeleteAsync(Guid campaignId, Guid userId)
    {
        var campaign = await _db.Campaigns.FirstOrDefaultAsync(c => c.Id == campaignId && c.HostUserId == userId);
        if (campaign is null) return false;

        _db.Campaigns.Remove(campaign);
        await _db.SaveChangesAsync();
        return true;
    }

    private static void ExtractCampaignMetadata(Campaign campaign, JsonElement state)
    {
        if (state.TryGetProperty("current_location", out var loc))
            campaign.CurrentLocation = loc.GetString() ?? "";

        if (state.TryGetProperty("players", out var players) && players.ValueKind == JsonValueKind.Object)
        {
            var summaries = new List<object>();
            foreach (var prop in players.EnumerateObject())
            {
                var player = prop.Value;
                summaries.Add(new
                {
                    playerId = prop.Name,
                    name = player.TryGetProperty("name", out var n) ? n.GetString() : "Unknown",
                    characterClass = player.TryGetProperty("character_class", out var c) ? c.GetString() : "warrior",
                    level = player.TryGetProperty("level", out var l) ? l.GetInt32() : 1,
                });
            }
            campaign.CharacterSummaryJson = JsonSerializer.Serialize(summaries);
        }
    }
}
