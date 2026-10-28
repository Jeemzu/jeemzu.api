using System.Diagnostics;
using System.Net.WebSockets;
using System.Text.Json;

namespace JeemzuApi.Putt;

public static class PuttPartyRegistration
{
    public const string SocketPath = "/api/putt/ws";
    private const int MaxMessageBytes = 4096;
    private const int MessagesPerSecond = 30;

    public static IServiceCollection AddPuttParty(this IServiceCollection services)
    {
        services.AddSingleton(_ => CourseCatalog.LoadDirectory(CourseCatalog.DefaultDirectory));
        services.AddSingleton<PuttSessionManager>();
        return services.AddHostedService<PuttTickService>();
    }

    /// <summary>Requires <c>UseWebSockets</c> earlier in the pipeline (origins are enforced there).</summary>
    public static IEndpointConventionBuilder MapPuttParty(this IEndpointRouteBuilder app) =>
        app.Map(SocketPath, HandleAsync);

    private static async Task HandleAsync(HttpContext context, PuttSessionManager manager)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var connection = new PuttConnection(socket);
        var writer = connection.RunWriterAsync();
        var client = new ClientState(manager, connection);
        var buffer = new byte[MaxMessageBytes];
        double tokens = MessagesPerSecond;
        long lastRefill = Stopwatch.GetTimestamp();

        try
        {
            while (!connection.Closed.IsCancellationRequested)
            {
                var message = await ReceiveAsync(socket, buffer, connection.Closed);
                if (message is null) break;

                long nowTs = Stopwatch.GetTimestamp();
                tokens = Math.Min(MessagesPerSecond, tokens + Stopwatch.GetElapsedTime(lastRefill, nowTs).TotalSeconds * MessagesPerSecond);
                lastRefill = nowTs;
                if (--tokens < 0) break;

                client.Handle(message.Value);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException) { }
        finally
        {
            client.Detach(leave: false);
            connection.Abort();
            await writer;
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try { await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, timeout.Token); }
                catch (Exception ex) when (ex is OperationCanceledException or WebSocketException) { }
            }
        }
    }

    private static async Task<ReadOnlyMemory<byte>?> ReceiveAsync(WebSocket socket, byte[] buffer, CancellationToken ct)
    {
        int count = 0;
        while (true)
        {
            if (count >= buffer.Length) return null;
            var result = await socket.ReceiveAsync(buffer.AsMemory(count), ct);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            count += result.Count;
            if (result.EndOfMessage) return buffer.AsMemory(0, count);
        }
    }

    /// <summary>The room/player a socket is attached to, plus message dispatch.</summary>
    private sealed class ClientState(PuttSessionManager manager, PuttConnection connection)
    {
        private PuttSessionManager.Room? _room;
        private string? _playerId;

        public void Handle(ReadOnlyMemory<byte> payload)
        {
            JsonElement root;
            try
            {
                using var doc = JsonDocument.Parse(payload);
                root = doc.RootElement.Clone();
            }
            catch (JsonException)
            {
                Fail(PuttErrors.InvalidMessage);
                return;
            }
            if (root.ValueKind != JsonValueKind.Object) { Fail(PuttErrors.InvalidMessage); return; }

            var now = DateTimeOffset.UtcNow;
            string? type = Str(root, "type");
            if (_room is null || _playerId is null)
            {
                switch (type)
                {
                    case "create": Create(root, now); break;
                    case "join": Join(root, now); break;
                    case "resume": Resume(root, now); break;
                    default: Fail(PuttErrors.InvalidMessage); break;
                }
                return;
            }

            var room = _room;
            var session = room.Session;
            string? error;
            lock (session.Gate)
            {
                switch (type)
                {
                    case "selectCourse":
                        error = session.SelectCourse(_playerId, Str(root, "courseId"), now);
                        if (session.Course is { } course)
                        {
                            var bytes = PuttProtocol.EncodeCourse(course);
                            foreach (var c in room.Connections.Values) c.Send(bytes);
                        }
                        break;
                    case "start": error = session.Start(_playerId, now); break;
                    case "shot":
                        error = session.Shoot(_playerId, Num(root, "dirX"), Num(root, "dirZ"), Num(root, "power"), now);
                        break;
                    case "returnToLobby": error = session.ReturnToLobby(_playerId, now); break;
                    case "leave": error = null; break;
                    default: error = PuttErrors.InvalidMessage; break;
                }
            }
            if (type == "leave") Detach(leave: true);
            if (error is not null) Fail(error);
        }

        private void Create(JsonElement root, DateTimeOffset now)
        {
            var room = manager.Create(now);
            if (room is null) { Fail(PuttErrors.ServerBusy); return; }
            lock (room.Session.Gate)
            {
                var (player, error) = room.Session.AddPlayer(Str(root, "name"), Str(root, "icon"), now);
                if (player is null)
                {
                    manager.Remove(room);
                    Fail(error!);
                    return;
                }
                Attach(room, player);
            }
        }

        private void Join(JsonElement root, DateTimeOffset now)
        {
            var room = manager.Find(Str(root, "code"));
            if (room is null) { Fail(PuttErrors.SessionNotFound); return; }
            lock (room.Session.Gate)
            {
                var (player, error) = room.Session.AddPlayer(Str(root, "name"), Str(root, "icon"), now);
                if (player is null) { Fail(error!); return; }
                Attach(room, player);
            }
        }

        private void Resume(JsonElement root, DateTimeOffset now)
        {
            var room = manager.Find(Str(root, "code"));
            if (room is null) { Fail(PuttErrors.ResumeFailed); return; }
            lock (room.Session.Gate)
            {
                var player = room.Session.Resume(Str(root, "playerId"), Str(root, "token"), now);
                if (player is null) { Fail(PuttErrors.ResumeFailed); return; }
                Attach(room, player);
            }
        }

        /// <summary>Caller holds the session gate.</summary>
        private void Attach(PuttSessionManager.Room room, PuttPlayer player)
        {
            if (room.Connections.Remove(player.Id, out var previous)) previous.Abort();
            room.Connections[player.Id] = connection;
            _room = room;
            _playerId = player.Id;
            var courses = manager.Catalog.Courses.Select(c => new PuttProtocol.CourseSummary(c.Id!, c.Name!, c.Author!)).ToList();
            connection.Send(PuttProtocol.Encode(new PuttProtocol.Welcome(
                room.Session.Code, player.Id, player.Token, courses,
                PuttRules.BallRadius, PuttRules.CupRadius, PuttRules.MaxStrokeLimit)));
            if (room.Session.Course is { } course) connection.Send(PuttProtocol.EncodeCourse(course));
            connection.Send(PuttProtocol.Encode(PuttProtocol.Snapshot(room.Session)));
        }

        public void Detach(bool leave)
        {
            if (_room is not { } room || _playerId is not { } id) return;
            var now = DateTimeOffset.UtcNow;
            lock (room.Session.Gate)
            {
                if (room.Connections.TryGetValue(id, out var current) && current == connection)
                {
                    room.Connections.Remove(id);
                    if (!leave) room.Session.Disconnect(id, now);
                }
                if (leave) room.Session.Leave(id, now);
            }
            _room = null;
            _playerId = null;
        }

        private void Fail(string code) => connection.Send(PuttProtocol.Encode(new PuttProtocol.Error(code)));

        private static string? Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        private static double Num(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : double.NaN;
    }
}

/// <summary>Steps every room at 20 Hz (physics runs at a fixed 120 Hz inside) and broadcasts changed state.</summary>
public sealed class PuttTickService(PuttSessionManager manager, ILogger<PuttTickService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        long last = Stopwatch.GetTimestamp();
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                long now = Stopwatch.GetTimestamp();
                double dt = Stopwatch.GetElapsedTime(last, now).TotalSeconds;
                last = now;
                var wall = DateTimeOffset.UtcNow;
                foreach (var room in manager.Rooms)
                {
                    try { Tick(room, dt, wall); }
                    catch (Exception ex) { logger.LogError(ex, "Putt Party tick failed for room {Code}", room.Session.Code); }
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private void Tick(PuttSessionManager.Room room, double dt, DateTimeOffset now)
    {
        var session = room.Session;
        lock (session.Gate)
        {
            session.Advance(dt, now);
            if (session.IsAbandoned(now))
            {
                manager.Remove(room);
                return;
            }
            if (!session.TakeDirty() && now - room.LastBroadcast < Heartbeat) return;
            room.LastBroadcast = now;
            var payload = PuttProtocol.Encode(PuttProtocol.Snapshot(session));
            foreach (var connection in room.Connections.Values) connection.Send(payload);
        }
    }
}
