using JeemzuApi.Data;
using JeemzuApi.Models;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace JeemzuApi.Services;

public class VectorStoreService : IVectorStoreService
{
    private readonly AppDbContext _db;

    public VectorStoreService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IEnumerable<KnowledgeChunk>> SearchAsync(
        ReadOnlyMemory<float> queryEmbedding,
        int topK = 5,
        CancellationToken ct = default)
    {
        var queryVector = new Vector(queryEmbedding.ToArray());

        // The <=> operator is cosine distance (0 = identical, 2 = opposite).
        // Ordering ascending gives us the most semantically similar chunks first.
        // EF.Functions.CosineDistance is provided by Npgsql's built-in pgvector support.
        return await _db.KnowledgeChunks
            .OrderBy(k => k.Embedding.CosineDistance(queryVector))
            .Take(topK)
            .ToListAsync(ct);
    }
}
