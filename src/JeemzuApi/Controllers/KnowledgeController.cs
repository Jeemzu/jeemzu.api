using JeemzuApi.DTOs;
using JeemzuApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace JeemzuApi.Controllers;

[ApiController]
[Route("api/knowledge")]
public class KnowledgeController : ControllerBase
{
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorStoreService _vectorStoreService;
    private readonly IConfiguration _configuration;

    public KnowledgeController(
        IEmbeddingService embeddingService,
        IVectorStoreService vectorStoreService,
        IConfiguration configuration)
    {
        _embeddingService = embeddingService;
        _vectorStoreService = vectorStoreService;
        _configuration = configuration;
    }

    /// <summary>
    /// Semantic search over the knowledge base. Embeds the query and returns
    /// the top-K most similar chunks by cosine distance. No LLM call — raw retrieval only.
    /// Requires X-Internal-Key header matching the configured InternalApiKey.
    /// </summary>
    [HttpGet("search")]
    [ProducesResponseType(typeof(KnowledgeSearchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Search(
        [FromQuery] string query,
        [FromQuery] int topK = 5,
        CancellationToken ct = default)
    {
        var expectedKey = _configuration["InternalApiKey"];
        if (!string.IsNullOrEmpty(expectedKey))
        {
            var providedKey = Request.Headers["X-Internal-Key"].FirstOrDefault();
            if (providedKey != expectedKey)
                return Unauthorized(new { message = "Invalid or missing internal API key." });
        }

        if (string.IsNullOrWhiteSpace(query))
            return BadRequest(new { message = "Query parameter is required." });

        topK = Math.Clamp(topK, 1, 20);

        var embedding = await _embeddingService.GenerateEmbeddingAsync(query, ct);
        var chunks = await _vectorStoreService.SearchAsync(embedding, topK, ct);

        var results = chunks.Select(c => new KnowledgeSearchResult
        {
            SourceKey = c.SourceKey,
            Content = c.Content
        }).ToList();

        return Ok(new KnowledgeSearchResponse
        {
            Results = results,
            TotalResults = results.Count
        });
    }
}
