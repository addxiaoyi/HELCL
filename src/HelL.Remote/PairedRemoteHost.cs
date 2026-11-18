using System.Net.WebSockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using HelL.Contracts;

namespace HelL.Remote;

public sealed record RemoteHostInvite(RemotePairingOffer Host, string ClientDeviceId, string Relay,
    string Room, string ClientTicket, long Expires)
{
    public InvitationFeedDescriptor? Feed { get; init; }
    public override string ToString() => "RemoteHostInvite { Credentials = [redacted] }";
}

public sealed class PairedRemoteHost : IAsyncDisposable
{
    private readonly RemotePairingStore pairings;
    private readonly HttpClient http;
    private readonly Uri relay;
    private readonly string operatorToken;
    private readonly Func<HostRequest, CancellationToken, Task<HostReply>> execute;
    private readonly TimeSpan renewalInterval;
    private readonly SemaphoreSlim changes = new(1, 1);
    private readonly CancellationTokenSource stopping = new();
    private readonly Dictionary<string, Session> sessions = new(StringComparer.Ordinal);
    private readonly object gate = new();
    private int disposed;

    private sealed class Session(PairedDevice device, RelayRoom room, CancellationToken cancellation,
        Func<RemoteHostInvite, CancellationToken, Task> publish)
    {
        public PairedDevice Device { get; } = device;
        public RelayRoom Room { get; set; } = room;
        public CancellationTokenSource Lifetime { get; } = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        public CancellationToken OwnerCancellation { get; } = cancellation;
        public int StopRequested;
        public CancellationTokenSource? Connection { get; set; }
        public Func<RemoteHostInvite, CancellationToken, Task> Publish { get; } = publish;
        public DateTimeOffset RenewedAt { get; set; } = DateTimeOffset.UtcNow;
        public Task Running { get; set; } = Task.CompletedTask;
    }

    public PairedRemoteHost(RemotePairingStore pairings, HttpClient http, Uri relay, string operatorToken,
        Func<HostRequest, CancellationToken, Task<HostReply>> execute, TimeSpan? renewalInterval = null)
    {
        this.pairings = pairings ?? throw new ArgumentNullException(nameof(pairings));
        this.http = http ?? throw new ArgumentNullException(nameof(http));
        this.relay = relay ?? throw new ArgumentNullException(nameof(relay));
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorToken);
        this.operatorToken = operatorToken;
        this.execute = execute ?? throw new ArgumentNullException(nameof(execute));
        this.renewalInterval = renewalInterval ?? TimeSpan.FromHours(20);
        if (this.renewalInterval <= TimeSpan.Zero || this.renewalInterval > TimeSpan.FromHours(23))
            throw new ArgumentOutOfRangeException(nameof(renewalInterval));
        pairings.Changed += PairingChanged;
    }

    public async Task<RemoteHostInvite> StartAsync(string deviceId,
        Func<RemoteHostInvite, CancellationToken, Task> publish, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(publish);
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(cancellation, stopping.Token);
        startup.CancelAfter(TimeSpan.FromSeconds(30));
        await changes.WaitAsync(startup.Token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            pairings.Refresh();
            PairedDevice device = pairings.Find(deviceId) ?? throw new UnauthorizedAccessException("Pair this device before enabling its remote session.");
            lock (gate) if (sessions.ContainsKey(deviceId)) throw new InvalidOperationException("Stop the previous remote session before starting another.");
            RelayRoom room = await RemoteHostChannel.CreateRoomAsync(http, relay, operatorToken, startup.Token).ConfigureAwait(false);
            var session = new Session(device, room, cancellation, publish);
            RemoteHostInvite invite = Invite(session);
            try
            {
                await publish(invite, startup.Token).WaitAsync(startup.Token).ConfigureAwait(false);
                lock (gate)
                {
                    ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
                    pairings.Refresh();
                    PairedDevice? confirmed = pairings.Find(deviceId);
                    if (confirmed is null || confirmed.Fingerprint != device.Fingerprint || !confirmed.Permissions.SequenceEqual(device.Permissions))
                        throw new UnauthorizedAccessException("This device pairing changed while its remote session was starting.");
                    session.Lifetime.Token.ThrowIfCancellationRequested();
                    sessions.Add(deviceId, session);
                    session.Running = RunAsync(session);
                }
                return invite;
            }
            catch
            {
                session.Lifetime.Dispose();
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await RemoteHostChannel.RevokeRoomAsync(http, relay, operatorToken, room.Room, cleanup.Token).ConfigureAwait(false);
                throw;
            }
        }
        finally { changes.Release(); }
    }

    public Task Completion(string deviceId)
    {
        lock (gate) return sessions.TryGetValue(deviceId, out Session? session) ? session.Running : Task.CompletedTask;
    }

    public async Task<RemoteHostInvite> RenewAsync(string deviceId, CancellationToken cancellation)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellation, stopping.Token);
        budget.CancelAfter(TimeSpan.FromSeconds(30));
        await changes.WaitAsync(budget.Token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            Session session;
            lock (gate) session = sessions.GetValueOrDefault(deviceId) ?? throw new InvalidOperationException("Start this device session before renewing it.");
            using var renewal = CancellationTokenSource.CreateLinkedTokenSource(budget.Token, session.Lifetime.Token);
            RelayRoom next = await RemoteHostChannel.RenewRoomAsync(http, relay, operatorToken, session.Room.Room, renewal.Token).ConfigureAwait(false);
            bool installed = false;
            try
            {
                lock (gate)
                {
                    renewal.Token.ThrowIfCancellationRequested();
                    session.Room = next;
                    installed = true;
                    session.RenewedAt = DateTimeOffset.UtcNow;
                    session.Connection?.Cancel();
                }
                RemoteHostInvite invite = Invite(session);
                try { await session.Publish(invite, renewal.Token).WaitAsync(renewal.Token).ConfigureAwait(false); }
                catch { session.Lifetime.Cancel(); throw; }
                return invite;
            }
            catch
            {
                if (!installed)
                {
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await RemoteHostChannel.RevokeRoomAsync(http, relay, operatorToken, next.Room, cleanup.Token).ConfigureAwait(false);
                }
                throw;
            }
        }
        finally { changes.Release(); }
    }

    public async Task RevokeAsync(string deviceId, CancellationToken cancellation)
    {
        pairings.Revoke(deviceId);
        await StopAsync(deviceId, cancellation).ConfigureAwait(false);
    }

    public async Task StopAsync(string deviceId, CancellationToken cancellation)
    {
        await changes.WaitAsync(cancellation).ConfigureAwait(false);
        Session? session = null;
        try
        {
            lock (gate)
            {
                if (!sessions.Remove(deviceId, out session)) return;
                RequestStop(session);
            }
            try { await session.Running.ConfigureAwait(false); }
            finally { await RemoteHostChannel.RevokeRoomAsync(http, relay, operatorToken, session.Room.Room, cancellation).ConfigureAwait(false); }
        }
        finally { session?.Lifetime.Dispose(); changes.Release(); }
    }

    private async Task RunAsync(Session session)
    {
        using X509Certificate2 identity = pairings.OpenIdentity();
        Task serving = ServeAsync(session, identity);
        Task monitoring = MonitorAsync(session);
        await Task.WhenAny(serving, monitoring).ConfigureAwait(false);
        bool expectedStop = stopping.IsCancellationRequested || session.OwnerCancellation.IsCancellationRequested
            || Volatile.Read(ref session.StopRequested) != 0;
        session.Lifetime.Cancel();
        try { await Task.WhenAll(serving, monitoring).ConfigureAwait(false); }
        catch (OperationCanceledException) when (expectedStop) { }
        catch (OperationCanceledException error)
        { throw new IOException("Remote hosting stopped because an internal operation timed out or was cancelled.", error); }
    }

    private async Task ServeAsync(Session session, X509Certificate2 identity)
    {
        while (!session.Lifetime.IsCancellationRequested)
        {
            using var connection = CancellationTokenSource.CreateLinkedTokenSource(session.Lifetime.Token);
            string ticket;
            lock (gate) { session.Connection = connection; ticket = session.Room.HostTicket; }
            try
            {
                await RemoteHostChannel.ServeAsync(relay, ticket, identity, session.Device.Fingerprint,
                    (request, cancellation) => DispatchAsync(session, request, cancellation), connection.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (connection.IsCancellationRequested) { }
            catch (Exception ex) when (ex is IOException or WebSocketException or AuthenticationException or TimeoutException or OperationCanceledException)
            {
                // Transport failures are retried; ticket and device contents must not be logged.
                System.Diagnostics.Trace.TraceWarning("Paired remote transport interrupted: " + ex.GetType().Name);
            }
            finally { lock (gate) if (ReferenceEquals(session.Connection, connection)) session.Connection = null; }
            await Task.Delay(TimeSpan.FromSeconds(1), session.Lifetime.Token).ConfigureAwait(false);
        }
    }

    private async Task MonitorAsync(Session session)
    {
        while (!session.Lifetime.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), session.Lifetime.Token).ConfigureAwait(false);
            pairings.Refresh();
            if (DateTimeOffset.UtcNow - session.RenewedAt >= renewalInterval
                || session.Room.Expires <= DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeSeconds())
                await RenewAsync(session.Device.DeviceId, session.Lifetime.Token).ConfigureAwait(false);
        }
    }

    private Task<HostReply> DispatchAsync(Session session, HostRequest request, CancellationToken cancellation)
    {
        request.Validate();
        cancellation.ThrowIfCancellationRequested();
        pairings.Refresh();
        PairedDevice? current = pairings.Find(session.Device.DeviceId);
        if (current is null || current.Fingerprint != session.Device.Fingerprint || !current.Permissions.Contains(HostPermission.Read)
            || (request.Command == HostCommand.CancelTask && !current.Permissions.Contains(HostPermission.CancelTasks)))
            return Task.FromResult(new HostReply { RequestId = request.RequestId, Device = pairings.Offer.Name,
                ErrorCode = "permission_denied", Error = "This paired device does not have permission for that host command." });
        return execute(request, cancellation);
    }

    private RemoteHostInvite Invite(Session session) => new(pairings.Offer, session.Device.DeviceId,
        relay.AbsoluteUri, session.Room.Room, session.Room.ClientTicket, session.Room.Expires);

    private void PairingChanged(string deviceId)
    {
        lock (gate) if (sessions.TryGetValue(deviceId, out Session? session)) RequestStop(session);
    }

    private static void RequestStop(Session session)
    {
        Volatile.Write(ref session.StopRequested, 1);
        session.Lifetime.Cancel();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        stopping.Cancel();
        pairings.Changed -= PairingChanged;
        string[] devices;
        lock (gate) foreach (Session session in sessions.Values) RequestStop(session);
        await changes.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (gate)
            {
                devices = sessions.Keys.ToArray();
                foreach (Session session in sessions.Values) RequestStop(session);
            }
        }
        finally { changes.Release(); }
        var failures = new List<Exception>();
        foreach (string device in devices)
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try { await StopAsync(device, cleanup.Token).ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { failures.Add(ex); }
        }
        stopping.Dispose();
        if (failures.Count != 0) throw new AggregateException("Some remote sessions could not complete relay cleanup.", failures);
    }
}
