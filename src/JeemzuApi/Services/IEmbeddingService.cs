namespace JeemzuApi.Services;

public interface IEmbeddingService
{
    /// <summary>
    /// Calls the Azure OpenAI embedding model and returns a 1536-dimensional vector
    /// representing the semantic meaning of <paramref name="text"/>.
    /// </summary>
    Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(string text, CancellationToken ct = default);
}
