using JeemzuApi.DTOs;
using JeemzuApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace JeemzuApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    private readonly IChatService _chat;

    public ChatController(IChatService chat)
    {
        _chat = chat;
    }

    /// <summary>
    /// Ask a question about James. No authentication required.
    /// Supply previous turns in History to maintain context across a multi-turn conversation.
    /// The server is stateless — the client is responsible for tracking history.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ChatResponse>> Chat(
        [FromBody] ChatRequest request,
        CancellationToken ct)
    {
        var answer = await _chat.ChatAsync(request.Question, request.History, ct);
        return Ok(new ChatResponse { Answer = answer });
    }
}
