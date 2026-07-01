using System.ComponentModel.DataAnnotations;
using System.Text.Json;

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

// ── Knowledge Search ─────────────────────────────────────────────────────────

/// <summary>
/// A single knowledge chunk returned from GET /api/knowledge/search.
/// Contains raw content retrieved via vector similarity — no LLM processing.
/// </summary>
public class KnowledgeSearchResult
{
    public string SourceKey { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

/// <summary>Response from GET /api/knowledge/search.</summary>
public class KnowledgeSearchResponse
{
    public List<KnowledgeSearchResult> Results { get; set; } = [];
    public int TotalResults { get; set; }
}

// ── Admin ─────────────────────────────────────────────────────────────────────

/// <summary>Returned from GET /api/admin/users — one entry per user.</summary>
public class AdminUserResponse
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool OptedIn { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Request body for PATCH /api/admin/users/{username}/role.</summary>
public class UpdateRoleRequest
{
    [Required]
    [AllowedValues("User", "Admin")]
    public string Role { get; set; } = string.Empty;
}

/// <summary>A knowledge chunk summary for the admin viewer.</summary>
public class AdminKnowledgeChunkResponse
{
    public Guid Id { get; set; }
    public string SourceKey { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Response from GET /api/admin/knowledge/chunks.</summary>
public class AdminKnowledgeListResponse
{
    public List<AdminKnowledgeChunkResponse> Chunks { get; set; } = [];
    public int TotalChunks { get; set; }
}

/// <summary>Health status of an external service.</summary>
public class ServiceHealthStatus
{
    public string Service { get; set; } = string.Empty;
    public bool Healthy { get; set; }
    public int? ResponseTimeMs { get; set; }
    public string? Error { get; set; }
}

// ── RPG / Party (multiplayer AI Game Master) ──────────────────────────────────

/// <summary>
/// A single member of a party, as broadcast to clients over SignalR.
/// </summary>
public class PartyMemberResponse
{
    public string Username { get; set; } = string.Empty;
    public string CharacterName { get; set; } = string.Empty;
    public string CharacterClass { get; set; } = string.Empty;
    public bool IsHost { get; set; }
    public bool IsConnected { get; set; }
    public string? ControlledBy { get; set; }
}

/// <summary>
/// Party state broadcast to clients — returned by CreateParty/JoinParty and sent via the
/// PartyUpdated SignalR event.
/// </summary>
public class PartyResponse
{
    public Guid PartyId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid? CampaignId { get; set; }
    public List<PartyMemberResponse> Members { get; set; } = [];
}

/// <summary>
/// Internal contract sent to the Python RPG service — one entry per party member.
/// Serialized with a snake_case naming policy, so PlayerId → "player_id", etc.
/// </summary>
public class RpgPlayerCreate
{
    public string PlayerId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string CharacterClass { get; set; } = string.Empty;
}

/// <summary>Deserialized response from POST /rpg/new on the Python RPG service.</summary>
public class RpgNewGameResult
{
    public string SessionId { get; set; } = string.Empty;
    public string Narrative { get; set; } = string.Empty;
    public List<JsonElement> VisualCommands { get; set; } = [];
    public JsonElement UiState { get; set; }
}

/// <summary>Deserialized response from POST /rpg/{sessionId}/action on the Python RPG service.</summary>
public class RpgActionResult
{
    public string Narrative { get; set; } = string.Empty;
    public List<JsonElement> VisualCommands { get; set; } = [];
    public JsonElement UiState { get; set; }
    public string ActionType { get; set; } = string.Empty;
}

// ── Campaigns (RPG save files) ────────────────────────────────────────────────

public class CampaignSummaryResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CurrentLocation { get; set; } = string.Empty;
    public string CharacterSummaryJson { get; set; } = "[]";
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset LastPlayedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class SaveCampaignResponse
{
    public Guid CampaignId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset SavedAt { get; set; }
}
