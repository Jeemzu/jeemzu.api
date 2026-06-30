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

    public KnowledgeController(IEmbeddingService embeddingService, IVectorStoreService vectorStoreService)
    {
        _embeddingService = embeddingService;
        _vectorStoreService = vectorStoreService;
    }

    /// <summary>
    /// Semantic search over the knowledge base. Embeds the query and returns
    /// the top-K most similar chunks by cosine distance. No LLM call — raw retrieval only.
    /// </summary>
    [HttpGet("search")]
    [ProducesResponseType(typeof(KnowledgeSearchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Search(
        [FromQuery] string query,
        [FromQuery] int topK = 5,
        CancellationToken ct = default)
    {
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
