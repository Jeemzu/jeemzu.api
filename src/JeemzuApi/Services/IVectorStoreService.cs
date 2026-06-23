using JeemzuApi.Models;

namespace JeemzuApi.Services;

public interface IVectorStoreService
{
    /// <summary>
    /// Returns the <paramref name="topK"/> knowledge chunks whose embeddings are
    /// most similar to <paramref name="queryEmbedding"/> by cosine distance.
    /// </summary>
    Task<IEnumerable<KnowledgeChunk>> SearchAsync(
        ReadOnlyMemory<float> queryEmbedding,
        int topK = 5,
        CancellationToken ct = default);
}
