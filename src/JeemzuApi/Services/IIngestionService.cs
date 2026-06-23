namespace JeemzuApi.Services;

public interface IIngestionService
{
    /// <summary>
    /// Reads Data/about-me.json, converts each entry to a human-readable text chunk,
    /// generates an embedding for each, and upserts them into the knowledge_chunks table.
    /// Returns the number of chunks upserted.
    /// </summary>
    Task<int> IngestAsync(CancellationToken ct = default);
}
