using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Threading.Channels;

namespace JeemzuApi.Putt;

/// <summary>One WebSocket. Outbound messages queue through a bounded channel so a slow client never blocks the tick loop.</summary>
public sealed class PuttConnection
{
    private readonly WebSocket _socket;
    private readonly Channel<byte[]> _outbound = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(64)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.Wait,
    });
    private readonly CancellationTokenSource _closed = new();

    public PuttConnection(WebSocket socket) => _socket = socket;

    public CancellationToken Closed => _closed.Token;

    /// <summary>Queues a message; a client too far behind is dropped and recovers by resuming.</summary>
    public void Send(byte[] payload)
    {
        if (!_outbound.Writer.TryWrite(payload)) Abort();
    }

    public void Abort()
    {
        _outbound.Writer.TryComplete();
        if (!_closed.IsCancellationRequested) _closed.Cancel();
    }

    public async Task RunWriterAsync()
    {
        try
        {
            await foreach (var payload in _outbound.Reader.ReadAllAsync(_closed.Token))
                await _socket.SendAsync(payload, WebSocketMessageType.Text, true, _closed.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException) { }
        finally
        {
            Abort();
        }
    }
}

/// <summary>Owns live rooms and the connections attached to each player. Mutations happen under <see cref="PuttSession.Gate"/>.</summary>
public sealed class PuttSessionManager
{
    public const int MaxSessions = 200;
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private readonly ConcurrentDictionary<string, Room> _rooms = new(StringComparer.Ordinal);

    public PuttSessionManager(CourseCatalog catalog) => Catalog = catalog;

    public CourseCatalog Catalog { get; }

    public sealed class Room(PuttSession session)
    {
        public PuttSession Session { get; } = session;
        /// <summary>Guarded by <see cref="PuttSession.Gate"/>.</summary>
        public Dictionary<string, PuttConnection> Connections { get; } = new();
        public DateTimeOffset LastBroadcast { get; set; }
    }

    public IEnumerable<Room> Rooms => _rooms.Values;

    public Room? Create(DateTimeOffset now)
    {
        if (_rooms.Count >= MaxSessions) return null;
        for (int attempt = 0; attempt < 20; attempt++)
        {
            var code = new string(Enumerable.Range(0, 6).Select(_ => CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)]).ToArray());
            var room = new Room(new PuttSession(code, Catalog, now));
            if (_rooms.TryAdd(code, room)) return room;
        }
        return null;
    }

    public Room? Find(string? code) =>
        code is not null && _rooms.TryGetValue(code.Trim().ToUpperInvariant(), out var room) ? room : null;

    public void Remove(Room room) => _rooms.TryRemove(room.Session.Code, out _);
}
