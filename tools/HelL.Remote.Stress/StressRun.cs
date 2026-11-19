using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text.Json;
using HelL.Contracts;
using HelL.Remote;

namespace HelL.Remote.Stress;

public sealed class StressRun(StressOptions options)
{
    private readonly StressReport report = new() { Options = options };
    private readonly List<StressSecrets> secretStores = [];
    private readonly TaskOwner tasks = new();
    private string stage = "initialization";
    private int cycle;
    private int snapshots;
    private int cancellations;
    private int readOnlyDenials;
    private int revocationDenials;
    private int feedRecoveries;

    private sealed class Peer(RemotePairingStore store, StressSecrets secrets, string root, string task)
    {
        internal RemotePairingStore Store = store;
        internal readonly StressSecrets Secrets = secrets;
        internal readonly string Root = root;
        internal readonly string TaskId = task;
        internal string HostId = "";
        internal InvitationFeedPublisher Publisher = null!;
        internal RemoteHostInvite First = null!;
        internal RemoteHostInvite Latest = null!;
        internal long Revision;
    }

    public async Task<StressReport> RunAsync(CancellationToken cancellation)
    {
        var elapsed = Stopwatch.StartNew();
        var meter = new ResourceMeter(report);
        OwnedWorkspace? workspace = null;
        RelayFixture? relay = null;
        using var sampling = new CancellationTokenSource();
        Task samples = Task.CompletedTask;
        report.HostSessionsStopped = true;
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false }) { Timeout = TimeSpan.FromSeconds(15) };
        try
        {
            workspace = new OwnedWorkspace();
            relay = new(workspace.Root);
            StressSecrets hostSecrets = Secrets(meter);
            string hostRoot = Path.Combine(workspace.Root, "pairings", "host");
            var hostStore = new RemotePairingStore(hostRoot, "stress-host", hostSecrets);
            RemotePairingOffer identity = hostStore.Offer;
            var peers = Enumerable.Range(0, options.Pairs).Select(index =>
            {
                StressSecrets keys = Secrets(meter);
                string root = Path.Combine(workspace.Root, "pairings", "peer-" + index);
                return new Peer(new RemotePairingStore(root, "stress-peer", keys), keys, root, "fixture-task-" + index);
            }).ToArray();
            samples = SampleAsync(meter, sampling.Token);
            await Step("relay-start", relay.StartAsync, cancellation);
            var random = new Random(options.Seed);
            for (cycle = 1; cycle <= options.Cycles; cycle++)
            {
                cancellation.ThrowIfCancellationRequested();
                var lap = Stopwatch.StartNew();
                Peer[] order = peers.OrderBy(_ => random.Next()).ToArray();
                hostStore.Refresh();
                Check(hostStore.Offer == identity, "The host identity changed between pairing generations.");
                if (!await CycleAsync(hostStore, hostSecrets, order, relay, http, random, cancellation)) break;
                Check(meter.SecretEntries == options.Pairs + 1, "Pairing or feed retirement left unexpected secret entries between cycles.");
                report.CompletedCycles++;
                (int rooms, int feeds) = relay.Counts();
                report.PeakTrackedRooms = Math.Max(report.PeakTrackedRooms, rooms);
                report.PeakTrackedFeeds = Math.Max(report.PeakTrackedFeeds, feeds);
                meter.Sample();
                Console.WriteLine($"Cycle {cycle}/{options.Cycles} passed; snapshots={Volatile.Read(ref snapshots)}; trackedRooms={rooms}; trackedFeeds={feeds}; peakRssBytes={report.PeakWorkingSetBytes}.");
                if (options.Period > lap.Elapsed)
                {
                    stage = "cycle-pacing";
                    await Task.Delay(options.Period - lap.Elapsed, cancellation);
                }
                if (samples.IsFaulted) await samples;
            }
        }
        catch (Exception failure) { Record(failure); }
        finally
        {
            stage = "relay-cleanup";
            if (relay is not null)
            {
                try { await relay.DisposeAsync(); report.RelayStopped = relay.Stopped; }
                catch (Exception failure) { Record(failure); }
            }
            else report.RelayStopped = true;
            sampling.Cancel();
            try { await samples; } catch (OperationCanceledException) when (sampling.IsCancellationRequested) { }
            catch (Exception failure) { Record(failure, "resource-sampling"); }
            stage = "owned-fixture-cleanup";
            foreach (StressSecrets keys in secretStores) keys.Clear();
            tasks.Clear();
            report.FinalSecretEntries = meter.SecretEntries;
            report.FinalFixtureTasks = tasks.Count;
            if (workspace is not null && report.RelayStopped && report.HostSessionsStopped)
            {
                try { workspace.Remove(); report.OwnedWorkspaceRemoved = !Directory.Exists(workspace.Root); }
                catch (Exception failure) { Record(failure); }
            }
        }
        report.Snapshots = Volatile.Read(ref snapshots);
        report.Cancellations = Volatile.Read(ref cancellations);
        report.ReadOnlyDenials = Volatile.Read(ref readOnlyDenials);
        report.RevocationDenials = Volatile.Read(ref revocationDenials);
        report.FeedRecoveries = Volatile.Read(ref feedRecoveries);
        report.ElapsedSeconds = elapsed.Elapsed.TotalSeconds;
        bool cleaned = report.HostSessionsStopped && report.RelayStopped && report.OwnedWorkspaceRemoved
            && report.FinalSecretEntries == 0 && report.FinalFixtureTasks == 0;
        report.Status = cancellation.IsCancellationRequested ? "cancelled"
            : report.Errors.Count == 0 && report.CompletedCycles == options.Cycles && cleaned ? "passed" : "failed";
        Directory.CreateDirectory(Path.GetDirectoryName(options.Report)!);
        await File.WriteAllTextAsync(options.Report, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        return report;
    }

    private async Task<bool> CycleAsync(RemotePairingStore hostStore, StressSecrets hostSecrets, Peer[] peers,
        RelayFixture relay, HttpClient http, Random random, CancellationToken cancellation)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        PairedRemoteHost? hosting = null;
        bool passed = true;
        try
        {
            stage = "pairing-restore-and-trust";
            foreach (Peer peer in peers)
            {
                RemotePairingOffer previous = peer.Store.Offer;
                peer.Store = new RemotePairingStore(peer.Root, "stress-peer", peer.Secrets);
                Check(peer.Store.Offer == previous, "The client identity changed after reopening its pairing store.");
                hostStore.Trust(peer.Store.Offer, [HostPermission.Read, HostPermission.CancelTasks]);
                peer.HostId = hostStore.Offer.DeviceId;
                peer.Store.Trust(hostStore.Offer, [HostPermission.Read]);
                peer.Publisher = Publisher(http, relay, hostSecrets, hostStore, peer);
                tasks.Start(peer.TaskId);
            }
            hosting = new(hostStore, http, relay.Address, relay.OperatorToken,
                (request, token) => Task.FromResult(tasks.Execute(request, hostStore.Offer.DeviceId, token)));
            report.HostSessionsStopped = false;
            foreach (Peer peer in peers)
            {
                await Step("paired-connect-and-publish", async _ =>
                {
                    peer.First = await hosting.StartAsync(peer.Store.Offer.DeviceId, async (invite, token) =>
                    {
                        RemoteHostInvite published = await peer.Publisher.PublishAsync(invite, token);
                        Volatile.Write(ref peer.Latest, published);
                    }, lifetime.Token);
                    peer.First = Volatile.Read(ref peer.Latest);
                    InvitationFeedUpdate first = await ReadFeed(http, peer.First, 0, cancellation);
                    peer.Revision = first.Revision;
                }, cancellation);
            }
            for (int wave = 0, waves = random.Next(1, 3); wave < waves; wave++)
                await Step("snapshot", token => Task.WhenAll(peers.Select(peer => Snapshot(peer, token))), cancellation);
            await Step("cancel-task", token => Task.WhenAll(peers.Select(async peer =>
            {
                var request = new HostRequest { RequestId = Guid.NewGuid().ToString("N"), Command = HostCommand.CancelTask, TaskId = peer.TaskId };
                HostReply reply = await new PairedRemoteClient(peer.Store).ExecuteAsync(peer.Latest, request, token);
                Check(reply.Ok && reply.Device == peer.HostId && tasks.IsCancelled(peer.TaskId), "A cancellation did not reach the expected fixture task owner.");
                Interlocked.Increment(ref cancellations);
            })), cancellation);
            foreach (Peer peer in peers)
            {
                RemoteHostInvite old = peer.Latest;
                await Step("renew", async token =>
                {
                    await hosting.RenewAsync(peer.Store.Offer.DeviceId, token);
                    Check(peer.Latest.Room != old.Room, "Renewal reused an old room.");
                    await RejectedTicket(http, relay, old.ClientTicket, token);
                    await Snapshot(peer, token);
                }, cancellation);
            }
            await Step("relay-restart", async token =>
            {
                await relay.StopAsync(token);
                await relay.StartAsync(token);
                report.RelayRestarts++;
            }, cancellation);
            foreach (Peer peer in peers)
            {
                await Step("feed-and-host-recovery", async token =>
                {
                    peer.Publisher = Publisher(http, relay, hostSecrets, hostStore, peer);
                    InvitationFeedUpdate recovered = await ReadFeed(http, peer.First, peer.Revision, token);
                    Check(recovered.Invitation.Room == peer.Latest.Room, "The persisted feed lost its pre-restart room.");
                    peer.Revision = recovered.Revision;
                    await RejectedTicket(http, relay, peer.First.ClientTicket, token);
                    await hosting.RenewAsync(peer.Store.Offer.DeviceId, token);
                    InvitationFeedUpdate renewed = await ReadFeed(http, peer.First, peer.Revision, token);
                    Check(renewed.Revision > peer.Revision && renewed.Invitation.Room == peer.Latest.Room, "The restarted publisher did not advance its feed revision.");
                    peer.Revision = renewed.Revision;
                    await Snapshot(peer, token);
                    Interlocked.Increment(ref feedRecoveries);
                }, cancellation);
            }
            foreach (Peer peer in peers)
            {
                await Step("read-only-permission", async token =>
                {
                    await hosting.StopAsync(peer.Store.Offer.DeviceId, token);
                    hostStore.Trust(peer.Store.Offer, [HostPermission.Read]);
                    tasks.Start(peer.TaskId);
                    await hosting.StartAsync(peer.Store.Offer.DeviceId, async (invite, publishToken) =>
                        Volatile.Write(ref peer.Latest, await peer.Publisher.PublishAsync(invite, publishToken)), lifetime.Token);
                    int before = tasks.Cancelled;
                    HostReply denied = await new PairedRemoteClient(peer.Store).ExecuteAsync(peer.Latest,
                        new HostRequest { RequestId = Guid.NewGuid().ToString("N"), Command = HostCommand.CancelTask, TaskId = peer.TaskId }, token);
                    Check(!denied.Ok && denied.ErrorCode == "permission_denied" && tasks.Cancelled == before && !tasks.IsCancelled(peer.TaskId),
                        "A read-only peer executed a task cancellation.");
                    Interlocked.Increment(ref readOnlyDenials);
                }, cancellation);
                await Step("revoke-and-release", async token =>
                {
                    RemoteHostInvite last = peer.Latest;
                    await hosting.RevokeAsync(peer.Store.Offer.DeviceId, token);
                    int executed = tasks.Executed;
                    bool rejected = false;
                    try { await new PairedRemoteClient(peer.Store).SnapshotAsync(last, token); }
                    catch (Exception failure) when (failure is WebSocketException or IOException) { rejected = true; }
                    Check(rejected && tasks.Executed == executed, "A revoked pairing continued to execute remote requests.");
                    await peer.Publisher.RevokeAsync(token);
                    try
                    {
                        await new InvitationFeedClient(http).ReadAsync(last, 0, token);
                        throw new StressCheckFailure("A revoked reader credential continued receiving invitations.");
                    }
                    catch (HttpRequestException failure) when (failure.StatusCode == HttpStatusCode.Unauthorized) { }
                    peer.Store.Revoke(hostStore.Offer.DeviceId);
                    Interlocked.Increment(ref revocationDenials);
                }, cancellation);
            }
        }
        catch (Exception failure) { Record(failure); passed = false; }
        finally
        {
            lifetime.Cancel();
            if (hosting is not null)
            {
                try { await hosting.DisposeAsync(); report.HostSessionsStopped = true; }
                catch (Exception failure) { Record(failure, "host-session-cleanup"); passed = false; }
            }
            tasks.Clear();
        }
        return passed;
    }

    private static InvitationFeedPublisher Publisher(HttpClient http, RelayFixture relay, StressSecrets secrets, RemotePairingStore host, Peer peer)
        => new(http, relay.Address, relay.OperatorToken, secrets, host.Offer.DeviceId, peer.Store.Offer.DeviceId);

    private async Task Snapshot(Peer peer, CancellationToken cancellation)
    {
        HostReply reply = await new PairedRemoteClient(peer.Store).SnapshotAsync(peer.Latest, cancellation);
        Check(reply.Ok && reply.Device == peer.HostId && reply.Tasks.Any(task => task.Id == peer.TaskId), "The paired snapshot lost its expected device or fixture task.");
        Interlocked.Increment(ref snapshots);
    }

    private static async Task<InvitationFeedUpdate> ReadFeed(HttpClient http, RemoteHostInvite first, long after, CancellationToken token)
        => await new InvitationFeedClient(http).ReadAsync(first, after, token)
            ?? throw new StressCheckFailure("The encrypted invitation feed did not advance.");

    private static async Task RejectedTicket(HttpClient http, RelayFixture relay, string ticket, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(relay.Address, "v1/tunnel"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ticket);
        using HttpResponseMessage response = await http.SendAsync(request, token);
        Check(response.StatusCode == HttpStatusCode.Unauthorized, "A retired room ticket remained valid.");
    }

    private async Task Step(string name, Func<CancellationToken, Task> operation, CancellationToken cancellation)
    {
        stage = name;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(options.Timeout);
        await operation(timeout.Token).WaitAsync(timeout.Token);
    }

    private static async Task SampleAsync(ResourceMeter meter, CancellationToken cancellation)
    {
        while (true) { cancellation.ThrowIfCancellationRequested(); meter.Sample(); await Task.Delay(TimeSpan.FromSeconds(1), cancellation); }
    }

    private StressSecrets Secrets(ResourceMeter meter) { var keys = new StressSecrets(meter); secretStores.Add(keys); return keys; }
    private void Record(Exception error, string? at = null) => report.Errors.Add(StressError.From(error, cycle, at ?? stage));
    private static void Check(bool passed, string message) { if (!passed) throw new StressCheckFailure(message); }

    private sealed class TaskOwner
    {
        private readonly ConcurrentDictionary<string, bool> cancelled = new();
        private int actions;
        private int executed;
        internal int Count => cancelled.Count;
        internal int Cancelled => Volatile.Read(ref actions);
        internal int Executed => Volatile.Read(ref executed);
        internal void Start(string task) => cancelled[task] = false;
        internal bool IsCancelled(string task) => cancelled.TryGetValue(task, out bool stopped) && stopped;
        internal void Clear() => cancelled.Clear();
        internal HostReply Execute(HostRequest request, string device, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            request.Validate();
            Interlocked.Increment(ref executed);
            if (request.Command == HostCommand.CancelTask)
            {
                if (!cancelled.ContainsKey(request.TaskId)) return new() { RequestId = request.RequestId, Device = device, ErrorCode = "task_not_running" };
                cancelled[request.TaskId] = true;
                Interlocked.Increment(ref actions);
            }
            return new() { RequestId = request.RequestId, Device = device, Ok = true,
                Tasks = cancelled.Select(row => new HostTask { Id = row.Key, Title = "Isolated fixture task", State = row.Value ? "cancelled" : "running" }).ToArray() };
        }
    }
}
