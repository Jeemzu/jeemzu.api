using System.ComponentModel.DataAnnotations;

namespace JeemzuApi.DTOs;

// ── Scores ──────────────────────────────────────────────────────────────────

/// <summary>
/// Request body for POST /api/scores.
/// Shape matches what gameApi.ts sends:
///   { gameId, username, score, timestamp }
/// </summary>
public class SubmitScoreRequest
{
    [Required]
    [MaxLength(100)]
    public string GameId { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string Username { get; set; } = string.Empty;

    [Range(0, int.MaxValue)]
    public int Score { get; set; }

    /// <summary>Unix timestamp in milliseconds — supplied by the client.</summary>
    public long Timestamp { get; set; }
}

/// <summary>
/// Response shape for GET /api/scores/{gameId}.
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

// ── Users ────────────────────────────────────────────────────────────────────

/// <summary>
/// Request body for POST /api/users.
/// Shape matches what gameApi.ts sends:
///   { username, optedIn }
/// </summary>
public class UpdateUserRequest
{
    [Required]
    [MaxLength(50)]
    public string Username { get; set; } = string.Empty;

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
