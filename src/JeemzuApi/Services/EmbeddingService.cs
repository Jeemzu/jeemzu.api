using Microsoft.Extensions.AI;

namespace JeemzuApi.Services;

public class EmbeddingService : IEmbeddingService
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _inner;

    public EmbeddingService(IEmbeddingGenerator<string, Embedding<float>> inner)
    {
        _inner = inner;
    }

    public async Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
    {
        var result = await _inner.GenerateAsync([text], cancellationToken: ct);
        return result[0].Vector;
    }
}
