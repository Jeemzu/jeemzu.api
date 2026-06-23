using System.ComponentModel.DataAnnotations;

namespace JeemzuApi.DTOs;

// ── Scores ──────────────────────────────────────────────────────────────────

/// <summary>
/// Request body for POST /api/scores.
/// Username is NOT accepted from the client — it is taken from the authenticated
/// user's JWT claim to prevent score spoofing.
/// </summary>
public class SubmitScoreRequest
{
    [Required]
    [MaxLength(100)]
    public string GameId { get; set; } = string.Empty;

    [Range(0, int.MaxValue)]
    public int Score { get; set; }

    /// <summary>Unix timestamp in milliseconds — supplied by the client.</summary>
    public long Timestamp { get; set; }
}

/// <summary>
/// Returned from GET /api/scores/{gameId}.
/// Must match the TypeScript GameHighScore type:
///   { gameId: string, username: string, score: number, timestamp: number }
/// </summary>
public class ScoreResponse
{
    public string GameId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public int Score { get; set; }
    public long Timestamp { get; set; }
}

/// <summary>
/// Returned from GET /api/scores/{gameId}/summary.
/// Provides all-time record and the requesting user's personal best in one call.
/// PersonalBest is null when the request is unauthenticated.
/// </summary>
public class GameSummaryResponse
{
    public ScoreResponse? AllTimeRecord { get; set; }
    public int? PersonalBest { get; set; }
}

// ── Users ────────────────────────────────────────────────────────────────────

/// <summary>
/// Request body for POST /api/users.
/// Username is taken from the JWT claim — only OptedIn is accepted from the client.
/// </summary>
public class UpdateUserRequest
{
    public bool OptedIn { get; set; }
}

/// <summary>Request body for POST /api/users/register.</summary>
public class RegisterRequest
{
    [Required]
    [MaxLength(50)]
    public string Username { get; set; } = string.Empty;

    [Required]
    [MinLength(8)]
    [MaxLength(100)]
    public string Password { get; set; } = string.Empty;

    public bool OptedIn { get; set; }
}

/// <summary>
/// Response shape for GET /api/users/{username}.
/// Must match the TypeScript UserGameData type:
///   { userId?, username, optedIn, highScores: Record&lt;string, number&gt; }
/// </summary>
public class UserResponse
{
    public string? UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public bool OptedIn { get; set; }

    /// <summary>
    /// Maps gameId → that user's highest score for the game.
    /// Populated by joining the Scores table on Username.
    /// </summary>
    public Dictionary<string, int> HighScores { get; set; } = [];
}

// ── Auth ──────────────────────────────────────────────────────────────────────

public class LoginRequest
{
    [Required]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}

/// <summary>
/// Returned from POST /api/auth/login and POST /api/auth/refresh.
/// The refresh token itself is set as an httpOnly cookie, not in this body.
/// </summary>
public class TokenResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string TokenType { get; set; } = "Bearer";

    /// <summary>Seconds until the access token expires.</summary>
    public int ExpiresIn { get; set; }
    public string Role { get; set; } = string.Empty;
}

// ── Chat ──────────────────────────────────────────────────────────────────────

/// <summary>
/// A single message in a conversation. Role must be "user" or "assistant",
/// mirroring the OpenAI chat message convention so the client payload is
/// immediately familiar to anyone who has used the OpenAI API.
/// </summary>
public class ConversationMessage
{
    [Required]
    [AllowedValues("user", "assistant")]
    public string Role { get; set; } = string.Empty;

    [Required]
    public string Content { get; set; } = string.Empty;
}

/// <summary>Request body for POST /api/chat.</summary>
public class ChatRequest
{
    [Required]
    [MinLength(1)]
    [MaxLength(2000)]
    public string Question { get; set; } = string.Empty;

    /// <summary>
    /// Prior conversation turns, oldest first.
    /// Omit or send an empty array to start a fresh conversation.
    /// The server is stateless — the client owns the history.
    /// </summary>
    public List<ConversationMessage> History { get; set; } = [];
}

/// <summary>Response from POST /api/chat.</summary>
public class ChatResponse
{
    public string Answer { get; set; } = string.Empty;
}

/// <summary>Response from POST /api/admin/knowledge/ingest.</summary>
public class IngestResponse
{
    public int ChunksUpserted { get; set; }
}
