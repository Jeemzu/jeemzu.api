using JeemzuApi.Data;
using JeemzuApi.DTOs;
using JeemzuApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JeemzuApi.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")]
public class AdminController : ControllerBase
{
    private readonly IIngestionService _ingestion;
    private readonly AppDbContext _db;
    private readonly IConfiguration _configuration;

    public AdminController(IIngestionService ingestion, AppDbContext db, IConfiguration configuration)
    {
        _ingestion = ingestion;
        _db = db;
        _configuration = configuration;
    }

    /// <summary>
    /// Re-reads Data/about-me.json, generates fresh embeddings for every entry,
    /// and upserts them into the knowledge_chunks table.
    /// Run this whenever you update about-me.json.
    /// Requires an Admin-role JWT in the Authorization header.
    /// </summary>
    [HttpPost("knowledge/ingest")]
    public async Task<ActionResult<IngestResponse>> Ingest(CancellationToken ct)
    {
        var count = await _ingestion.IngestAsync(ct);
        return Ok(new IngestResponse { ChunksUpserted = count });
    }

    /// <summary>List all knowledge chunks in the vector store.</summary>
    [HttpGet("knowledge/chunks")]
    public async Task<ActionResult<AdminKnowledgeListResponse>> GetChunks(CancellationToken ct)
    {
        var chunks = await _db.KnowledgeChunks
            .OrderBy(c => c.SourceKey)
            .Select(c => new AdminKnowledgeChunkResponse
            {
                Id = c.Id,
                SourceKey = c.SourceKey,
                Content = c.Content,
                UpdatedAt = c.UpdatedAt,
            })
            .ToListAsync(ct);

        return Ok(new AdminKnowledgeListResponse
        {
            Chunks = chunks,
            TotalChunks = chunks.Count,
        });
    }

    /// <summary>List all registered users.</summary>
    [HttpGet("users")]
    public async Task<ActionResult<List<AdminUserResponse>>> GetUsers(CancellationToken ct)
    {
        var users = await _db.Users
            .OrderByDescending(u => u.CreatedAt)
            .Select(u => new AdminUserResponse
            {
                Id = u.Id,
                Username = u.Username,
                Role = u.Role,
                OptedIn = u.OptedIn,
                CreatedAt = u.CreatedAt,
            })
            .ToListAsync(ct);

        return Ok(users);
    }

    /// <summary>Change a user's role (promote/demote).</summary>
    [HttpPatch("users/{username}/role")]
    public async Task<IActionResult> UpdateRole(string username, [FromBody] UpdateRoleRequest request, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username, ct);
        if (user is null) return NotFound(new { message = $"User '{username}' not found." });

        user.Role = request.Role;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Ok(new AdminUserResponse
        {
            Id = user.Id,
            Username = user.Username,
            Role = user.Role,
            OptedIn = user.OptedIn,
            CreatedAt = user.CreatedAt,
        });
    }

    /// <summary>Delete a user account.</summary>
    [HttpDelete("users/{username}")]
    public async Task<IActionResult> DeleteUser(string username, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username, ct);
        if (user is null) return NotFound(new { message = $"User '{username}' not found." });

        _db.Users.Remove(user);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Budgetize asks the assistant couldn't fulfil, grouped by feature so the most
    /// requested gaps rise to the top.
    /// </summary>
    [HttpGet("budget/gaps")]
    public async Task<ActionResult<List<BudgetGapSummaryResponse>>> BudgetGaps(CancellationToken ct)
    {
        var groups = await _db.BudgetCapabilityGaps
            .AsNoTracking()
            .GroupBy(g => g.SuggestedFeature)
            .Select(g => new
            {
                SuggestedFeature = g.Key,
                RequestCount = g.Count(),
                UserCount = g.Select(x => x.UserId).Distinct().Count(),
                LastRequestedAt = g.Max(x => x.CreatedAt),
                Examples = g.OrderByDescending(x => x.CreatedAt).Take(3).Select(x => x.RequestText).ToList(),
            })
            .OrderByDescending(g => g.RequestCount)
            .ThenByDescending(g => g.LastRequestedAt)
            .ToListAsync(ct);

        return Ok(groups.Select(g => new BudgetGapSummaryResponse
        {
            SuggestedFeature = g.SuggestedFeature,
            RequestCount = g.RequestCount,
            UserCount = g.UserCount,
            LastRequestedAt = g.LastRequestedAt,
            Examples = g.Examples,
        }).ToList());
    }

    /// <summary>Check health of external services (agent, database).</summary>
    [HttpGet("health")]
    public async Task<ActionResult<List<ServiceHealthStatus>>> HealthCheck(CancellationToken ct)
    {
        var results = new List<ServiceHealthStatus>();

        // Database
        var dbStatus = new ServiceHealthStatus { Service = "PostgreSQL" };
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await _db.Database.CanConnectAsync(ct);
            sw.Stop();
            dbStatus.Healthy = true;
            dbStatus.ResponseTimeMs = (int)sw.ElapsedMilliseconds;
        }
        catch (Exception ex)
        {
            dbStatus.Healthy = false;
            dbStatus.Error = ex.Message;
        }
        results.Add(dbStatus);

        // Python agent service, reachable only over the private network
        var agentUrl = _configuration["Agents:BaseUrl"];
        if (!string.IsNullOrEmpty(agentUrl))
        {
            var agentStatus = new ServiceHealthStatus { Service = "Agents" };
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var resp = await http.GetAsync($"{agentUrl}/health", ct);
                sw.Stop();
                agentStatus.Healthy = resp.IsSuccessStatusCode;
                agentStatus.ResponseTimeMs = (int)sw.ElapsedMilliseconds;
            }
            catch (Exception ex)
            {
                agentStatus.Healthy = false;
                agentStatus.Error = ex.Message;
            }
            results.Add(agentStatus);
        }

        return Ok(results);
    }
}
