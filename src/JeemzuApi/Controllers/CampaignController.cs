using System.Security.Claims;
using JeemzuApi.Data;
using JeemzuApi.DTOs;
using JeemzuApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JeemzuApi.Controllers;

/// <summary>
/// REST endpoints for campaign management — listing and deleting saved campaigns.
/// Save/Load during gameplay go through the SignalR GameHub instead.
/// </summary>
[ApiController]
[Route("api/campaigns")]
[Authorize]
public class CampaignController : ControllerBase
{
    private readonly ICampaignService _campaignService;
    private readonly AppDbContext _db;

    public CampaignController(ICampaignService campaignService, AppDbContext db)
    {
        _campaignService = campaignService;
        _db = db;
    }

    /// <summary>Lists all saved campaigns for the authenticated user.</summary>
    [HttpGet]
    public async Task<ActionResult<List<CampaignSummaryResponse>>> ListCampaigns()
    {
        var userId = await ResolveUserIdAsync();
        if (userId is null) return Unauthorized();

        var campaigns = await _campaignService.ListAsync(userId.Value);
        return Ok(campaigns);
    }

    /// <summary>Deletes a campaign. Only the owning host can delete.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> DeleteCampaign(Guid id)
    {
        var userId = await ResolveUserIdAsync();
        if (userId is null) return Unauthorized();

        var deleted = await _campaignService.DeleteAsync(id, userId.Value);
        return deleted ? NoContent() : NotFound();
    }

    private async Task<Guid?> ResolveUserIdAsync()
    {
        var username = User.FindFirstValue(ClaimTypes.Name);
        if (username is null) return null;

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username);
        return user?.Id;
    }
}
