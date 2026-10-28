using System.Text.Json;

namespace JeemzuApi.Putt;

/// <summary>
/// JSON messages over <c>/api/putt/ws</c>. Every message has a <c>type</c>. Client → server:
/// <c>create {name, icon}</c>, <c>join {code, name, icon}</c>, <c>resume {code, playerId, token}</c>,
/// <c>selectCourse {courseId}</c>, <c>start</c>, <c>shot {dirX, dirZ, power}</c>, <c>returnToLobby</c>, <c>leave</c>.
/// Server → client: <c>welcome</c>, <c>course</c>, <c>state</c>, <c>error</c>.
/// </summary>
public static class PuttProtocol
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public sealed record CourseSummary(string Id, string Name, string Author);

    public sealed record Welcome(
        string Code, string PlayerId, string Token, IReadOnlyList<CourseSummary> Courses,
        double BallRadius, double CupRadius, int MaxStrokeLimit, string Type = "welcome");

    public sealed record CourseMessage(CourseFile Course, string Type = "course");

    public sealed record Error(string Code, string Type = "error");

    public sealed record PlayerState(
        string Id, string Name, string Icon, int Color, bool Connected, bool Disqualified,
        double X, double Z, bool Moving, bool Sunk, int Strokes, bool HoleDone, IReadOnlyList<int> Scores, int Total);

    public sealed record State(
        string Code, string Phase, string? HostId, string? CourseId, int HoleIndex, double PhaseRemaining,
        double Time, IReadOnlyList<PlayerState> Players, IReadOnlyList<string> Winners, string Type = "state");

    public static State Snapshot(PuttSession s) => new(
        s.Code,
        s.Phase.ToString().ToLowerInvariant(),
        s.HostId,
        s.Course?.Id,
        s.HoleIndex,
        Math.Round(Math.Max(0, s.PhaseRemaining), 3),
        Math.Round(s.Time, 4),
        s.Players.Select(p => new PlayerState(
            p.Id, p.Name, p.Icon, p.Color, p.Connected, p.Disqualified,
            Math.Round(p.Ball.X, 3), Math.Round(p.Ball.Z, 3), p.Ball.Moving, p.Ball.Sunk,
            p.Strokes, p.HoleDone, p.Scores.ToArray(), p.Total)).ToList(),
        s.Winners);

    public static byte[] Encode<T>(T message) => JsonSerializer.SerializeToUtf8Bytes(message, Json);

    public static byte[] EncodeCourse(CourseFile course) =>
        JsonSerializer.SerializeToUtf8Bytes(new CourseMessage(course), CourseValidator.JsonOptions);
}
