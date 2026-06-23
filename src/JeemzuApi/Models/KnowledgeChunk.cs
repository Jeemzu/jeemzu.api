using Pgvector;

namespace JeemzuApi.Models;

public class KnowledgeChunk
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Stable identifier for this chunk, e.g. "projects.jeemzu_api" or "experience.acme_corp".
    /// The ingestion service uses this to upsert rather than create duplicate rows.
    /// </summary>
    public string SourceKey { get; set; } = string.Empty;

    /// <summary>Human-readable text that was embedded — what the LLM will see as context.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>1536-dimensional embedding vector (text-embedding-3-small output dimensions).</summary>
    public Vector Embedding { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
