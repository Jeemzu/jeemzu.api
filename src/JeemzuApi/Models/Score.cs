namespace JeemzuApi.Models;

public class Score
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Matches the gameId field from the React frontend (e.g. "snake", "tetris").
    /// Not a FK to a Games table — kept simple intentionally.
    /// </summary>
    public string GameId { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    /// <summary>FK to the User who submitted this score. Null for legacy/guest scores.</summary>
    public Guid? UserId { get; set; }
    public User? User { get; set; }

    /// <summary>Named ScoreValue to avoid conflicting with the Score class name.</summary>
    public int ScoreValue { get; set; }

    /// <summary>Unix timestamp in milliseconds — matches the timestamp field sent by the React frontend.</summary>
    public long Timestamp { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
