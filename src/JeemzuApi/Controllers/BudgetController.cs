using System.Security.Claims;
using System.Text.Json;
using JeemzuApi.Data;
using JeemzuApi.DTOs;
using JeemzuApi.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JeemzuApi.Controllers;

/// <summary>
/// Load and save the authenticated admin's personal budget. Each admin has their
/// own budget — the owner is always taken from the JWT, never from the request.
/// </summary>
[ApiController]
[Route("api/admin/budget")]
[Authorize(Roles = "Admin")]
public class BudgetController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _db;

    public BudgetController(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>Returns the saved budget, or 404 when the admin has not saved one yet.</summary>
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

    private async Task<Guid?> ResolveUserIdAsync(CancellationToken ct)
    {
        var username = User.FindFirstValue(ClaimTypes.Name);
        if (username is null) return null;

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username, ct);
        return user?.Id;
    }
}
