using JeemzuApi.DTOs;
using JeemzuApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JeemzuApi.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")]
public class AdminController : ControllerBase
{
    private readonly IIngestionService _ingestion;

    public AdminController(IIngestionService ingestion)
    {
        _ingestion = ingestion;
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
}
