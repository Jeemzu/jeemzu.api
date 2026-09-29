using System.Security.Claims;
using System.Text.Json;
using JeemzuApi.Data;
using JeemzuApi.DTOs;
using JeemzuApi.Models;
using JeemzuApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JeemzuApi.Controllers;

/// <summary>
/// Load and save the authenticated user's personal budget. Each user has their
/// own budget — the owner is always taken from the JWT, never from the request.
/// </summary>
[ApiController]
[Route("api/budget")]
[Authorize]
public class BudgetController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _db;
    private readonly IBudgetAgentProxyService _assistant;
    private readonly ILogger<BudgetController> _logger;

    public BudgetController(
        AppDbContext db,
        IBudgetAgentProxyService assistant,
        ILogger<BudgetController> logger)
    {
        _db = db;
        _assistant = assistant;
        _logger = logger;
    }

    /// <summary>Returns the saved budget, or 404 when the user has not saved one yet.</summary>
    [HttpGet]
    public async Task<ActionResult<BudgetSnapshotResponse>> GetBudget(CancellationToken ct)
    {
        var userId = await ResolveUserIdAsync(ct);
        if (userId is null) return Unauthorized();

        var snapshot = await _db.BudgetSnapshots
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.UserId == userId.Value, ct);

        if (snapshot is null) return NotFound();

        var data = JsonSerializer.Deserialize<BudgetDataDto>(snapshot.DataJson, JsonOptions) ?? new BudgetDataDto();
        return Ok(new BudgetSnapshotResponse
        {
            Data = data,
            Revision = snapshot.Revision,
            UpdatedAt = snapshot.UpdatedAt,
        });
    }

    /// <summary>
    /// Saves the budget. Send the revision from the last load or save; a null
    /// revision creates the first budget. A mismatch means another device saved
    /// in the meantime, so the save is rejected instead of overwriting it.
    /// </summary>
    [HttpPut]
    [RequestSizeLimit(1_000_000)]
    public async Task<ActionResult<BudgetSnapshotResponse>> SaveBudget(
        [FromBody] SaveBudgetRequest request,
        CancellationToken ct)
    {
        var userId = await ResolveUserIdAsync(ct);
        if (userId is null) return Unauthorized();

        // Re-serialize the validated DTO so only known fields are ever stored.
        var json = JsonSerializer.Serialize(request.Data, JsonOptions);
        var revision = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        if (request.Revision is null)
        {
            _db.BudgetSnapshots.Add(new BudgetSnapshot
            {
                UserId = userId.Value,
                DataJson = json,
                Revision = revision,
                CreatedAt = now,
                UpdatedAt = now,
            });

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                return Conflict(new { message = "A budget already exists. Reload it before saving." });
            }
        }
        else
        {
            var rows = await _db.BudgetSnapshots
                .Where(b => b.UserId == userId.Value && b.Revision == request.Revision.Value)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(b => b.DataJson, json)
                    .SetProperty(b => b.Revision, revision)
                    .SetProperty(b => b.UpdatedAt, now), ct);

            if (rows == 0)
            {
                return Conflict(new { message = "This budget was changed elsewhere. Reload before saving." });
            }
        }

        return Ok(new BudgetSnapshotResponse
        {
            Data = request.Data,
            Revision = revision,
            UpdatedAt = now,
        });
    }

    /// <summary>Permanently deletes the saved budget. Idempotent: 204 even when none exists.</summary>
    [HttpDelete]
    public async Task<IActionResult> DeleteBudget(CancellationToken ct)
    {
        var userId = await ResolveUserIdAsync(ct);
        if (userId is null) return Unauthorized();

        await _db.BudgetSnapshots
            .Where(b => b.UserId == userId.Value)
            .ExecuteDeleteAsync(ct);

        return NoContent();
    }

    /// <summary>
    /// Asks the assistant about the budget. The budget travels in the request rather
    /// than being read from the database, so unsaved edits are visible; the assistant
    /// only ever proposes changes, it never writes anything here.
    /// </summary>
    [HttpPost("chat")]
    [RequestSizeLimit(2_000_000)]
    public async Task<ActionResult<BudgetChatResult>> Chat(
        [FromBody] BudgetChatRequest request,
        CancellationToken ct)
    {
        var userId = await ResolveUserIdAsync(ct);
        if (userId is null) return Unauthorized();

        try
        {
            var result = await _assistant.ChatAsync(new BudgetChatPayload
            {
                Question = request.Question,
                History = request.History,
                Budget = request.Budget,
                Projection = request.Projection,
                Today = request.Today,
                Strategy = request.Strategy,
            }, ct);

            return Ok(result);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Budget assistant call failed");
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "The budget assistant is unavailable right now." });
        }
    }

    /// <summary>
    /// Records something the assistant could not do, so unmet demand becomes a
    /// backlog instead of vanishing into a chat transcript.
    /// </summary>
    [HttpPost("gaps")]
    public async Task<IActionResult> LogGap([FromBody] LogBudgetGapRequest request, CancellationToken ct)
    {
        var userId = await ResolveUserIdAsync(ct);
        if (userId is null) return Unauthorized();

        _db.BudgetCapabilityGaps.Add(new BudgetCapabilityGap
        {
            UserId = userId.Value,
            RequestText = request.Request,
            Reason = request.Reason,
            SuggestedFeature = request.SuggestedFeature.Trim().ToLowerInvariant(),
        });
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    private async Task<Guid?> ResolveUserIdAsync(CancellationToken ct)
    {
        var username = User.FindFirstValue(ClaimTypes.Name);
        if (username is null) return null;

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username, ct);
        return user?.Id;
    }
}
