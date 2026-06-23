using JeemzuApi.DTOs;
using Microsoft.SemanticKernel.ChatCompletion;

namespace JeemzuApi.Services;

public class ChatService : IChatService
{
    private readonly IEmbeddingService _embedding;
    private readonly IVectorStoreService _vectorStore;
    private readonly IChatCompletionService _chat;

    public ChatService(
        IEmbeddingService embedding,
        IVectorStoreService vectorStore,
        IChatCompletionService chat)
    {
        _embedding = embedding;
        _vectorStore = vectorStore;
        _chat = chat;
    }

    public async Task<string> ChatAsync(
        string question,
        IEnumerable<ConversationMessage> history,
        CancellationToken ct = default)
    {
        // Step 1 — Embed the question so we can search for semantically similar chunks
        var queryEmbedding = await _embedding.GenerateEmbeddingAsync(question, ct);

        // Step 2 — Retrieve the top-5 most relevant chunks from the knowledge base
        var chunks = await _vectorStore.SearchAsync(queryEmbedding, topK: 5, ct: ct);
        var context = string.Join("\n\n", chunks.Select(c => c.Content));

        // Step 3 — Build the conversation with a grounding system prompt.
        // The retrieved chunks are injected here so the LLM treats them as facts,
        // not as user-supplied claims it could dispute.
        var chatHistory = new ChatHistory();
        chatHistory.AddSystemMessage($"""
            You are a helpful personal assistant that answers questions about James.
            Use ONLY the information provided in the context below to answer questions.
            If the answer to a question is not present in the context, respond with:
            "I don't have that information about James."
            Do not infer, guess, or use any knowledge outside of what is explicitly stated.
            Be conversational and concise.

            Context:
            {context}
            """);

        // Step 4 — Replay the conversation history so the model remembers prior turns.
        // The client is responsible for sending history; the server is stateless.
        foreach (var msg in history)
        {
            if (msg.Role == "user")
                chatHistory.AddUserMessage(msg.Content);
            else if (msg.Role == "assistant")
                chatHistory.AddAssistantMessage(msg.Content);
        }

        // Step 5 — Add the current question
        chatHistory.AddUserMessage(question);

        // Step 6 — Generate the response
        var response = await _chat.GetChatMessageContentAsync(chatHistory, cancellationToken: ct);
        return response.Content ?? "I was unable to generate a response. Please try again.";
    }
}
