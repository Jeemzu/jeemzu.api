using System.Security.Cryptography;

namespace JeemzuApi.Putt;

public enum SessionPhase { Lobby, Playing, Transition, Finished }

/// <summary>Stable error ids sent to clients; the client maps them to player-facing text.</summary>
public static class PuttErrors
{
    public const string SessionNotFound = "sessionNotFound";
    public const string SessionFull = "sessionFull";
    public const string GameInProgress = "gameInProgress";
    public const string InvalidName = "invalidName";
    public const string InvalidIcon = "invalidIcon";
    public const string NotHost = "notHost";
    public const string NotPlaying = "notPlaying";
    public const string BallMoving = "ballMoving";
    public const string HoleDone = "holeDone";
    public const string InvalidShot = "invalidShot";
    public const string UnknownCourse = "unknownCourse";
    public const string ResumeFailed = "resumeFailed";
    public const string InvalidMessage = "invalidMessage";
    public const string ServerBusy = "serverBusy";
}

public sealed class PuttPlayer
{
    public required string Id { get; init; }
    public required string Token { get; init; }
    public required string Name { get; init; }
    public required string Icon { get; init; }
    public required int Color { get; init; }
    public bool Connected { get; set; } = true;
    public DateTimeOffset? DisconnectedAt { get; set; }
    public bool Disqualified { get; set; }
    public Ball Ball { get; } = new();
    public int Strokes { get; set; }
    public bool HoleDone { get; set; }
    public List<int> Scores { get; } = [];
    public int Total => Scores.Sum();
    /// <summary>Still competing: not disqualified. Disconnected players stay active for the reconnect window.</summary>
    public bool Active => !Disqualified;
}

/// <summary>
/// Authoritative state for one room. Not thread-safe: callers serialize access through <see cref="Gate"/>.
/// Time is passed in explicitly so rules are deterministic under test.
/// </summary>
public sealed class PuttSession
{
    private readonly CourseCatalog _catalog;
    private readonly List<PuttPlayer> _players = [];
    private HoleCollider? _collider;
    private double _accumulator;

    public object Gate { get; } = new();
    public string Code { get; }
    public SessionPhase Phase { get; private set; } = SessionPhase.Lobby;
    public string? HostId { get; private set; }
    public CourseFile? Course { get; private set; }
    public int HoleIndex { get; private set; }
    public double PhaseRemaining { get; private set; }
    public double Time { get; private set; }
    public IReadOnlyList<string> Winners { get; private set; } = [];
    public IReadOnlyList<PuttPlayer> Players => _players;
    /// <summary>Set whenever client-visible state changes; cleared by <see cref="TakeDirty"/>.</summary>
    public bool Dirty { get; private set; } = true;
    public DateTimeOffset LastActivity { get; private set; }

    public PuttSession(string code, CourseCatalog catalog, DateTimeOffset now)
    {
        Code = code;
        _catalog = catalog;
        Course = catalog.Courses[0];
        LastActivity = now;
    }

    public bool TakeDirty()
    {
        bool d = Dirty;
        Dirty = false;
        return d;
    }

    public PuttPlayer? Find(string id) => _players.Find(p => p.Id == id);

    public (PuttPlayer? Player, string? Error) AddPlayer(string? name, string? icon, DateTimeOffset now)
    {
        var clean = CleanName(name);
        if (clean is null) return (null, PuttErrors.InvalidName);
        if (icon is null || !PuttRules.Icons.Contains(icon)) return (null, PuttErrors.InvalidIcon);
        if (Phase != SessionPhase.Lobby) return (null, PuttErrors.GameInProgress);
        if (_players.Count >= PuttRules.MaxPlayers) return (null, PuttErrors.SessionFull);

        int color = Enumerable.Range(0, PuttRules.MaxPlayers).First(c => _players.All(p => p.Color != c));
        var player = new PuttPlayer
        {
            Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant(),
            Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)),
            Name = clean,
            Icon = icon,
            Color = color,
        };
        _players.Add(player);
        HostId ??= player.Id;
        Touch(now);
        return (player, null);
    }

    public static string? CleanName(string? name)
    {
        if (name is null) return null;
        var trimmed = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return trimmed.Length is >= 1 and <= 16 ? trimmed : null;
    }

    public PuttPlayer? Resume(string? playerId, string? token, DateTimeOffset now)
    {
        var player = playerId is null ? null : Find(playerId);
        if (player is null || player.Disqualified || token is null
            || !CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(player.Token), TryDecode(token)))
            return null;
        player.Connected = true;
        player.DisconnectedAt = null;
        if (HostId is null || Find(HostId) is not { Connected: true }) HostId = player.Id;
        Touch(now);
        return player;
    }

    private static byte[] TryDecode(string token)
    {
        try { return Convert.FromBase64String(token); }
        catch (FormatException) { return []; }
    }

    public void Disconnect(string playerId, DateTimeOffset now)
    {
        if (Find(playerId) is not { Connected: true } player) return;
        player.Connected = false;
        player.DisconnectedAt = now;
        ReassignHost();
        Touch(now);
    }

    public void Leave(string playerId, DateTimeOffset now)
    {
        if (Find(playerId) is not { } player) return;
        if (Phase == SessionPhase.Lobby) _players.Remove(player);
        else
        {
            player.Connected = false;
            player.Disqualified = true;
        }
        ReassignHost();
        Touch(now);
    }

    private void ReassignHost()
    {
        if (HostId is not null && Find(HostId) is { Connected: true, Disqualified: false }) return;
        HostId = _players.FirstOrDefault(p => p is { Connected: true, Disqualified: false })?.Id;
    }

    public string? SelectCourse(string playerId, string? courseId, DateTimeOffset now)
    {
        if (playerId != HostId) return PuttErrors.NotHost;
        if (Phase != SessionPhase.Lobby) return PuttErrors.GameInProgress;
        Course = _catalog.Find(courseId);
        if (Course is null)
        {
            Course = _catalog.Courses[0];
            return PuttErrors.UnknownCourse;
        }
        Touch(now);
        return null;
    }

    public string? Start(string playerId, DateTimeOffset now)
    {
        if (playerId != HostId) return PuttErrors.NotHost;
        if (Phase != SessionPhase.Lobby) return PuttErrors.GameInProgress;
        foreach (var p in _players) p.Scores.Clear();
        Winners = [];
        BeginHole(0);
        Touch(now);
        return null;
    }

    public string? ReturnToLobby(string playerId, DateTimeOffset now)
    {
        if (playerId != HostId) return PuttErrors.NotHost;
        if (Phase != SessionPhase.Finished) return PuttErrors.GameInProgress;
        _players.RemoveAll(p => p.Disqualified);
        foreach (var p in _players)
        {
            p.Scores.Clear();
            p.Strokes = 0;
            p.HoleDone = false;
        }
        Phase = SessionPhase.Lobby;
        Winners = [];
        _collider = null;
        Touch(now);
        return null;
    }

    public string? Shoot(string playerId, double dirX, double dirZ, double power, DateTimeOffset now)
    {
        if (Phase != SessionPhase.Playing || _collider is null) return PuttErrors.NotPlaying;
        if (Find(playerId) is not { Active: true } player) return PuttErrors.NotPlaying;
        if (player.HoleDone) return PuttErrors.HoleDone;
        if (player.Ball.Moving) return PuttErrors.BallMoving;
        double len = Math.Sqrt(dirX * dirX + dirZ * dirZ);
        if (!double.IsFinite(len) || len < 1e-6 || !double.IsFinite(power) || power < PuttRules.MinShotPower)
            return PuttErrors.InvalidShot;
        power = Math.Min(power, 1);
        double speed = power * PuttRules.MaxShotSpeed;
        player.Ball.VX = dirX / len * speed;
        player.Ball.VZ = dirZ / len * speed;
        player.Strokes++;
        Touch(now);
        return null;
    }

    private HoleDefinition CurrentHole => Course!.Holes![HoleIndex];

    private void BeginHole(int index)
    {
        HoleIndex = index;
        _collider = new HoleCollider(CurrentHole);
        _accumulator = 0;
        foreach (var p in _players.Where(p => p.Active))
        {
            p.Strokes = 0;
            p.HoleDone = false;
            p.Ball.PlaceAt(_collider.Tee);
        }
        Phase = SessionPhase.Playing;
        PhaseRemaining = 0;
        Dirty = true;
    }

    /// <summary>Advances simulation, timers and timeouts by <paramref name="dt"/> real seconds.</summary>
    public void Advance(double dt, DateTimeOffset now)
    {
        dt = Math.Clamp(dt, 0, 0.25);
        Time += dt;
        ExpireDisconnected(now);

        switch (Phase)
        {
            case SessionPhase.Playing:
                _accumulator += dt;
                while (_accumulator >= PuttRules.FixedStep)
                {
                    _accumulator -= PuttRules.FixedStep;
                    foreach (var p in _players)
                    {
                        if (!p.Active || !p.Ball.Moving) continue;
                        _collider!.Step(p.Ball, PuttRules.FixedStep);
                        Dirty = true;
                    }
                }
                UpdateHoleProgress();
                break;
            case SessionPhase.Transition:
                PhaseRemaining -= dt;
                if (PhaseRemaining <= 0)
                {
                    if (HoleIndex + 1 < PuttRules.HoleCount) BeginHole(HoleIndex + 1);
                    else Finish();
                }
                break;
        }
    }

    private void UpdateHoleProgress()
    {
        int limit = CurrentHole.StrokeLimit;
        foreach (var p in _players.Where(p => p.Active && !p.HoleDone))
        {
            if (p.Ball.Sunk) CompleteHole(p, p.Strokes);
            else if (!p.Ball.Moving && p.Strokes >= limit) CompleteHole(p, PuttRules.MaxStrokeLimit);
        }

        // Disconnected players don't hold up the hole, but someone connected must be present to finish it.
        var waiting = _players.Where(p => p is { Active: true, Connected: true }).ToList();
        if (waiting.Count == 0 || waiting.Any(p => !p.HoleDone)) return;
        foreach (var p in _players.Where(p => p.Active && !p.HoleDone)) CompleteHole(p, PuttRules.MaxStrokeLimit);

        Phase = SessionPhase.Transition;
        PhaseRemaining = HoleIndex + 1 < PuttRules.HoleCount ? PuttRules.TransitionSeconds : PuttRules.FinalHoleSeconds;
        Dirty = true;
    }

    private void CompleteHole(PuttPlayer p, int score)
    {
        p.HoleDone = true;
        p.Scores.Add(score);
        Dirty = true;
    }

    private void Finish()
    {
        Phase = SessionPhase.Finished;
        PhaseRemaining = 0;
        _collider = null;
        // Only players still connected at the end are counted; every tied lowest total wins.
        var eligible = _players.Where(p => p is { Active: true, Connected: true }).ToList();
        int best = eligible.Count > 0 ? eligible.Min(p => p.Total) : 0;
        Winners = eligible.Where(p => p.Total == best).Select(p => p.Id).ToList();
        Dirty = true;
    }

    private void ExpireDisconnected(DateTimeOffset now)
    {
        foreach (var p in _players.ToList())
        {
            if (p.Connected || p.Disqualified || p.DisconnectedAt is not { } at || now - at < PuttRules.ReconnectWindow) continue;
            if (Phase == SessionPhase.Lobby) _players.Remove(p);
            else p.Disqualified = true;
            ReassignHost();
            Dirty = true;
        }
    }

    /// <summary>True once nobody is connected and the reconnect window has passed for everyone.</summary>
    public bool IsAbandoned(DateTimeOffset now) =>
        _players.All(p => !p.Connected) && now - LastActivity >= PuttRules.ReconnectWindow;

    private void Touch(DateTimeOffset now)
    {
        LastActivity = now;
        Dirty = true;
    }
}
