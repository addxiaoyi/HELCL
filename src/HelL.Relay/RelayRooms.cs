using System.Net.WebSockets;
using System.Text.Json;
using HelL.Contracts;
using HelL.Remote;

namespace HelL.Relay;

public sealed class RelayRooms
{
    private const int FrameBytes = 64 * 1024;
    private readonly object gate = new();
    private readonly Dictionary<string, TicketState> tickets = new(StringComparer.Ordinal);
    private readonly RelayLimits limits;
    private int activeRooms;

    private sealed class TicketState(long expires, bool revoked = false)
    {
        public long Expires { get; } = expires;
        public bool Revoked { get; set; } = revoked;
        public Room? Active;
        public RoleBudget HostBudget { get; } = new();
        public RoleBudget ClientBudget { get; } = new();
    }

    private sealed class RoleBudget
    {
        public DateTimeOffset Started;
        public int Connections;
    }

    internal sealed class Room
    {
        public WebSocket? Host;
        public WebSocket? Client;
        public bool HostReserved;
        public bool ClientReserved;
        public TaskCompletionSource<bool> Paired { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationTokenSource Ended { get; } = new();
        public int Occupants;
        public DateTimeOffset WindowStarted = DateTimeOffset.UtcNow;
        public long Bytes;
        public int Fragments;
    }

    public sealed class Reservation : IDisposable
    {
        private readonly RelayRooms owner;
        private int disposed;
        internal Room Room { get; }
        internal RelayTicket Ticket { get; }
        internal CancellationToken Ended { get; }

        internal Reservation(RelayRooms owner, RelayTicket ticket, Room room)
        {
            this.owner = owner;
            Ticket = ticket;
            Room = room;
            Ended = room.Ended.Token;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0) owner.Release(this);
        }
    }

    private sealed record SavedRoom(string Room, long Expires, bool Revoked);

    public RelayRooms() : this(new RelayLimits()) { }

    public RelayRooms(RelayLimits limits)
    {
        this.limits = limits ?? throw new ArgumentNullException(nameof(limits));
        limits.Validate();
        LoadState();
        SaveState();
    }

    public void Register(RelayRoom issued)
    {
        ArgumentNullException.ThrowIfNull(issued);
        lock (gate)
        {
            PurgeExpired();
            if (tickets.Count >= limits.MaxTrackedRooms)
                throw new RelayAdmissionException(503, "Relay room registry capacity reached.");
            tickets.Add(issued.Room, new(issued.Expires));
            try { SaveState(); }
            catch { tickets.Remove(issued.Room); throw; }
        }
    }

    public bool IsAllowed(RelayTicket ticket)
    {
        lock (gate)
        {
            return ticket.Expires > DateTimeOffset.UtcNow.ToUnixTimeSeconds() &&
                tickets.TryGetValue(ticket.Room, out TicketState? state) &&
                !state.Revoked && state.Expires == ticket.Expires;
        }
    }

    public bool Revoke(string room)
    {
        lock (gate)
        {
            PurgeExpired();
            if (!tickets.TryGetValue(room, out TicketState? state)) return false;
            if (!state.Revoked)
            {
                state.Revoked = true;
                try { SaveState(); }
                catch { state.Revoked = false; throw; }
            }
            EndRoom(state.Active);
            return true;
        }
    }

    public RelayRoom Renew(string room, Func<RelayRoom> issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
        lock (gate)
        {
            PurgeExpired();
            if (!tickets.TryGetValue(room, out TicketState? previous) || previous.Revoked)
                throw new RelayAdmissionException(404, "This relay room cannot be renewed.");
            if (tickets.Count >= limits.MaxTrackedRooms)
                throw new RelayAdmissionException(503, "Relay room registry capacity reached.");
            RelayRoom replacement = issue();
            tickets.Add(replacement.Room, new(replacement.Expires));
            previous.Revoked = true;
            try { SaveState(); }
            catch { previous.Revoked = false; tickets.Remove(replacement.Room); throw; }
            EndRoom(previous.Active);
            return replacement;
        }
    }

    public Reservation Reserve(RelayTicket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        if (ticket.Role is not ("host" or "client")) throw new IOException("This relay role is invalid.");
        if (ticket.Expires <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()) throw new IOException("This relay ticket has expired.");
        lock (gate)
        {
            PurgeExpired();
            if (!tickets.TryGetValue(ticket.Room, out TicketState? state) || state.Revoked || state.Expires != ticket.Expires)
                throw new RelayAdmissionException(401, "This relay room ticket is no longer active.");
            RoleBudget budget = ticket.Role == "host" ? state.HostBudget : state.ClientBudget;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (now - budget.Started >= limits.ConnectionWindow) { budget.Started = now; budget.Connections = 0; }
            if (budget.Connections >= limits.ConnectionsPerWindow)
                throw new RelayAdmissionException(429, "This relay credential has exceeded its connection budget.");
            budget.Connections++;
            Room? room = state.Active;
            if (room is not null && (ticket.Role == "host" ? room.HostReserved : room.ClientReserved))
                throw new RelayAdmissionException(429, "This relay role is already connected.");
            if (room is null)
            {
                if (activeRooms >= limits.MaxRooms) throw new RelayAdmissionException(503, "Relay room capacity reached.");
                state.Active = room = new Room();
                activeRooms++;
            }
            if (ticket.Role == "host") room.HostReserved = true; else room.ClientReserved = true;
            room.Occupants++;
            return new(this, ticket, room);
        }
    }

    // Direct callers are trusted ticket validators; HTTP callers reserve before upgrading.
    public async Task ForwardAsync(RelayTicket ticket, WebSocket socket, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        lock (gate)
        {
            if (!tickets.ContainsKey(ticket.Room) && ticket.Role is "host" or "client" && ticket.Expires > DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            {
                PurgeExpired();
                if (tickets.Count >= limits.MaxTrackedRooms) throw new IOException("Relay room registry capacity reached.");
                tickets.Add(ticket.Room, new(ticket.Expires));
            }
        }
        Reservation reservation;
        try { reservation = Reserve(ticket); }
        catch (RelayAdmissionException ex) { throw new IOException(ex.Message, ex); }
        using (reservation) await ForwardReservedAsync(reservation, socket, cancellation).ConfigureAwait(false);
    }

    internal async Task ForwardReservedAsync(Reservation reservation, WebSocket socket, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        ArgumentNullException.ThrowIfNull(socket);
        Room room = reservation.Room;
        RelayTicket ticket = reservation.Ticket;
        using var connection = CancellationTokenSource.CreateLinkedTokenSource(cancellation, reservation.Ended);
        try
        {
            lock (gate)
            {
                connection.Token.ThrowIfCancellationRequested();
                if (ticket.Role == "host") room.Host = socket; else room.Client = socket;
                if (room.Host is not null && room.Client is not null) room.Paired.TrySetResult(true);
            }
            using (var pairing = CancellationTokenSource.CreateLinkedTokenSource(connection.Token))
            {
                pairing.CancelAfter(limits.PairingTimeout);
                await room.Paired.Task.WaitAsync(pairing.Token).ConfigureAwait(false);
            }
            WebSocket peer = (ticket.Role == "host" ? room.Client : room.Host)!;
            byte[] buffer = new byte[FrameBytes];
            int messageBytes = 0;
            while (!connection.IsCancellationRequested)
            {
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(connection.Token);
                idle.CancelAfter(limits.IdleTimeout);
                WebSocketReceiveResult fragment = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), idle.Token).ConfigureAwait(false);
                if (fragment.MessageType == WebSocketMessageType.Close) return;
                if (fragment.MessageType != WebSocketMessageType.Binary) throw new IOException("Relay accepts encrypted binary traffic only.");
                messageBytes += fragment.Count;
                if (messageBytes > HostProtocol.MaxMessageBytes) throw new IOException("Relay message size limit exceeded.");
                CountTraffic(room, fragment.Count);
                await peer.SendAsync(new ArraySegment<byte>(buffer, 0, fragment.Count), WebSocketMessageType.Binary,
                    fragment.EndOfMessage, idle.Token).ConfigureAwait(false);
                if (fragment.EndOfMessage) messageBytes = 0;
            }
            connection.Token.ThrowIfCancellationRequested();
        }
        finally { reservation.Dispose(); socket.Abort(); }
    }

    private void CountTraffic(Room room, int bytes)
    {
        lock (gate)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (now - room.WindowStarted >= limits.TrafficWindow)
            { room.WindowStarted = now; room.Bytes = 0; room.Fragments = 0; }
            room.Bytes += bytes;
            if (++room.Fragments > limits.FragmentsPerWindow || room.Bytes > limits.BytesPerWindow)
                throw new IOException("Relay room traffic budget exceeded.");
        }
    }

    private void Release(Reservation reservation)
    {
        lock (gate)
        {
            Room room = reservation.Room;
            EndRoom(room);
            if (tickets.TryGetValue(reservation.Ticket.Room, out TicketState? state) && ReferenceEquals(state.Active, room))
            { state.Active = null; activeRooms--; }
            if (--room.Occupants == 0) room.Ended.Dispose();
        }
    }

    private static void EndRoom(Room? room)
    {
        if (room is null) return;
        room.Ended.Cancel();
        room.Host?.Abort();
        room.Client?.Abort();
    }

    private void PurgeExpired()
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (string room in tickets.Where(row => row.Value.Expires <= now).Select(row => row.Key).ToArray())
        {
            TicketState state = tickets[room];
            if (state.Active is not null) { EndRoom(state.Active); activeRooms--; }
            tickets.Remove(room);
        }
    }

    private void LoadState()
    {
        if (string.IsNullOrWhiteSpace(limits.StatePath) || !File.Exists(limits.StatePath)) return;
        SavedRoom[] saved = JsonSerializer.Deserialize<SavedRoom[]>(File.ReadAllText(limits.StatePath))
            ?? throw new InvalidDataException("Relay room registry is empty or invalid.");
        if (saved.Length > limits.MaxTrackedRooms) throw new InvalidDataException("Relay room registry exceeds its configured capacity.");
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (SavedRoom row in saved)
        {
            if (row.Room is null || row.Room.Length != 48 || row.Room.Any(ch => !Uri.IsHexDigit(ch)) || row.Expires > DateTimeOffset.UtcNow.AddHours(24).ToUnixTimeSeconds())
                throw new InvalidDataException("Relay room registry contains an invalid room.");
            if (row.Expires > now && !tickets.TryAdd(row.Room, new(row.Expires, row.Revoked)))
                throw new InvalidDataException("Relay room registry contains duplicate rooms.");
        }
    }

    private void SaveState()
    {
        if (string.IsNullOrWhiteSpace(limits.StatePath)) return;
        string path = Path.GetFullPath(limits.StatePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        SavedRoom[] snapshot = tickets.Select(row => new SavedRoom(row.Key, row.Value.Expires, row.Value.Revoked)).ToArray();
        try
        {
            using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(file, snapshot);
                file.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
