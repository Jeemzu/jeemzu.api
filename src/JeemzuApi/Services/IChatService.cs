using JeemzuApi.DTOs;

namespace JeemzuApi.Services;

public interface IChatService
{
    /// <summary>
    /// Runs the full RAG pipeline: embeds the question, retrieves relevant knowledge
    /// chunks, injects them as context, appends conversation history, calls the LLM,
    /// and returns the generated answer.
    /// </summary>
    Task<string> ChatAsync(
        string question,
        IEnumerable<ConversationMessage> history,
        CancellationToken ct = default);
}
