using System.Text.Json;
using System.Threading.Channels;
using System.Reflection;
using HelL.Contracts;
using HelL.Mobile.Remote;
using HelL.Remote;
using Xunit;

namespace HelL.Mobile.Tests;

public sealed class MobileRemoteTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "hell-mobile-pair-" + Guid.NewGuid().ToString("N"));
    private readonly RemotePairingStore phone;
    private readonly RemotePairingStore host;
    public MobileRemoteTests()
    {
        phone = new RemotePairingStore(Path.Combine(root, "phone"), "手机", new Keys());
        host = new RemotePairingStore(Path.Combine(root, "host"), "家里的电脑", new Keys());
    }
    [Fact]
    public void BothPublicOffersAndExplicitFingerprintConfirmationAreRequired()
    {
        using var remote = new MobileRemoteController(phone);
        remote.StageOffer(JsonSerializer.Serialize(host.Offer));
        Assert.Equal(host.Offer.Fingerprint, remote.PendingHost?.Fingerprint);
        Assert.Throws<InvalidOperationException>(() => remote.ConfirmPairing(false, true));
        Assert.Empty(remote.Hosts);
        remote.ConfirmPairing(true, false);
        Assert.Equal([HostPermission.Read], Assert.Single(remote.Hosts).Permissions);
        Assert.DoesNotContain("Identity", remote.OfferJson);
        Assert.DoesNotContain("Ticket", remote.OfferJson);
    }
    [Fact]
    public void InvitationsCannotTrustAHostOrLeakAnOperatorCredential()
    {
        using var remote = new MobileRemoteController(phone);
        Assert.Throws<UnauthorizedAccessException>(() => remote.ImportInvitation(JsonSerializer.Serialize(Invite())));
        Pair(remote);
        string dangerous = JsonSerializer.Serialize(Invite()).TrimEnd('}') + ",\"OperatorToken\":\"secret\"}";
        Assert.Throws<InvalidDataException>(() => remote.ImportInvitation(dangerous));
        Assert.Null(remote.Invitation);
    }
    [Fact]
    public async Task ReadOnlyPermissionBlocksCancellationAndRevocationClearsTheSnapshot()
    {
        int calls = 0;
        using var remote = new MobileRemoteController(phone, (invite, request, token) =>
        {
            calls++;
            return Task.FromResult(new HostReply { RequestId = request.RequestId, Device = "电脑", Ok = true,
                Tasks = [new HostTask { Id = "task-1", Title = "安装资源", State = "running" }] });
        });
        Pair(remote);
        remote.ImportInvitation(JsonSerializer.Serialize(Invite()));
        await remote.RefreshAsync();
        Assert.Single(remote.Tasks);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => remote.CancelTaskAsync("task-1"));
        Assert.Equal(1, calls);
        remote.Revoke(host.Offer.DeviceId);
        Assert.Empty(remote.Tasks);
        Assert.Null(remote.Invitation);
        Assert.Empty(remote.Hosts);
    }
    [Fact]
    public async Task CancellingARefreshCannotPublishALateSnapshot()
    {
        var completion = new TaskCompletionSource<HostReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var remote = new MobileRemoteController(phone, (_, _, _) => completion.Task);
        Pair(remote, true);
        remote.ImportInvitation(JsonSerializer.Serialize(Invite()));
        Task refresh = remote.RefreshAsync();
        Assert.True(remote.Busy);
        remote.Cancel();
        completion.SetResult(new HostReply { Ok = true, Device = "电脑", Tasks = [new HostTask { Id = "stale" }] });
        await refresh;
        Assert.Empty(remote.Tasks);
        Assert.False(remote.Busy);
        Assert.Contains("取消", remote.Notice);
    }
    [Fact]
    public void ExpiredOrDifferentClientUpdatesPreserveTheLastValidInvitation()
    {
        using var remote = new MobileRemoteController(phone);
        Pair(remote, true);
        RemoteHostInvite valid = Invite();
        remote.UpdateInvitation(valid);
        Assert.True(remote.CanCancel);
        Assert.Throws<InvalidDataException>(() => remote.UpdateInvitation(valid with { Expires = 1 }));
        Assert.Throws<UnauthorizedAccessException>(() => remote.UpdateInvitation(valid with { ClientDeviceId = Guid.NewGuid().ToString("N") }));
        Assert.Same(valid, remote.Invitation);
    }
    private RemoteHostInvite Invite() => new(host.Offer, phone.Offer.DeviceId, "https://relay.example/",
        new string('a', 48), "client-ticket", DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds());
    [Fact]
    public async Task SecureUpdatesReplaceOnlyTheCurrentHostsInvitationAndPauseRejectsLateUpdates()
    {
        var feed = Channel.CreateUnbounded<InvitationFeedUpdate>();
        using var remote = new MobileRemoteController(phone, watch: (_, _) => feed.Reader.ReadAllAsync());
        Pair(remote);
        RemoteHostInvite initial = Invite() with { Feed = new InvitationFeedDescriptor(new string('b', 48), new string('c', 64), Convert.ToBase64String(new byte[32]), DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds()) };
        remote.ImportInvitation(JsonSerializer.Serialize(initial));
        remote.StartUpdates();
        Assert.True(remote.Watching);
        RemoteHostInvite renewed = initial with { Room = new string('d', 48), ClientTicket = "renewed" };
        await feed.Writer.WriteAsync(new InvitationFeedUpdate(1, renewed));
        Assert.True(SpinWait.SpinUntil(() => remote.Invitation?.Room == renewed.Room, TimeSpan.FromSeconds(2)));
        remote.StopUpdates();
        await feed.Writer.WriteAsync(new InvitationFeedUpdate(2, initial));
        feed.Writer.Complete();
        await Task.Delay(80);
        Assert.Equal(renewed.Room, remote.Invitation?.Room);
        Assert.False(remote.Watching);
    }
    [Fact]
    public async Task FailedUpdateSubscriptionIsVisibleAndRetryableWithoutExposingCredentials()
    {
        var feed = Channel.CreateUnbounded<InvitationFeedUpdate>();
        using var remote = new MobileRemoteController(phone, watch: (_, token) => feed.Reader.ReadAllAsync(token));
        Pair(remote);
        remote.UpdateInvitation(Invite() with { Feed = new InvitationFeedDescriptor(new string('b', 48), new string('c', 64), Convert.ToBase64String(new byte[32]), DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds()) });
        remote.StartUpdates();
        feed.Writer.Complete(new IOException("private-reader-token-must-not-appear"));
        Assert.True(SpinWait.SpinUntil(() => !remote.Watching, TimeSpan.FromSeconds(2)));
        await Task.Delay(50);
        Assert.Contains("重试", remote.UpdateNotice);
        Assert.DoesNotContain("private-reader", remote.UpdateNotice);
    }
    [Fact]
    public async Task ResumingUpdatesDoesNotAcceptAnOlderRevision()
    {
        var first = Channel.CreateUnbounded<InvitationFeedUpdate>();
        var second = Channel.CreateUnbounded<InvitationFeedUpdate>();
        int starts = 0;
        using var remote = new MobileRemoteController(phone, watch: (_, token) =>
            (Interlocked.Increment(ref starts) == 1 ? first : second).Reader.ReadAllAsync(token));
        Pair(remote);
        RemoteHostInvite initial = Invite() with { Feed = InvitationFeedCodec.Create(DateTimeOffset.UtcNow.AddDays(1)) };
        remote.ImportInvitation(JsonSerializer.Serialize(initial));
        RemoteHostInvite renewed = initial with { Room = new string('d', 48), ClientTicket = "renewed" };
        await first.Writer.WriteAsync(new InvitationFeedUpdate(2, renewed));
        Assert.True(SpinWait.SpinUntil(() => remote.Invitation?.Room == renewed.Room, TimeSpan.FromSeconds(2)));
        remote.StopUpdates();
        remote.StartUpdates();
        await second.Writer.WriteAsync(new InvitationFeedUpdate(1, initial));
        await Task.Delay(100);
        Assert.Equal(renewed.Room, remote.Invitation?.Room);
    }
    [Fact]
    public void RebindingAnInvitationNeverPublishesItWithThePreviousWatchStillActive()
    {
        var feed = Channel.CreateUnbounded<InvitationFeedUpdate>();
        using var remote = new MobileRemoteController(phone, watch: (_, token) => feed.Reader.ReadAllAsync(token));
        Pair(remote);
        RemoteHostInvite first = Invite() with { Feed = InvitationFeedCodec.Create(DateTimeOffset.UtcNow.AddDays(1)) };
        remote.ImportInvitation(JsonSerializer.Serialize(first));
        Assert.True(remote.Watching);
        RemoteHostInvite replacement = first with { Room = new string('d', 48), Feed = null };
        bool mixedBinding = false;
        remote.Changed += () => { if (remote.Invitation?.Room == replacement.Room && remote.Watching) mixedBinding = true; };
        remote.ImportInvitation(JsonSerializer.Serialize(replacement));
        Assert.False(mixedBinding);
        Assert.Equal(replacement.Room, remote.Invitation?.Room);
        Assert.False(remote.Watching);
    }
    [Fact]
    public async Task ARefreshWaitingForTheStateGateUsesTheReplacementInvitation()
    {
        string requestedRoom = "";
        using var remote = new MobileRemoteController(phone, (invite, request, token) =>
        {
            requestedRoom = invite.Room;
            return Task.FromResult(new HostReply { RequestId = request.RequestId, Ok = true, Device = "电脑" });
        });
        Pair(remote);
        RemoteHostInvite initial = Invite();
        RemoteHostInvite replacement = initial with { Room = new string('d', 48) };
        remote.UpdateInvitation(initial);
        object gate = typeof(MobileRemoteController).GetField("stateGate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(remote)!;
        Task? refresh = null;
        var worker = new Thread(() => refresh = remote.RefreshAsync()) { IsBackground = true };
        lock (gate)
        {
            worker.Start();
            Assert.True(SpinWait.SpinUntil(() => (worker.ThreadState & ThreadState.WaitSleepJoin) != 0, TimeSpan.FromSeconds(2)));
            remote.UpdateInvitation(replacement);
        }
        Assert.True(worker.Join(TimeSpan.FromSeconds(2)));
        Assert.NotNull(refresh);
        await refresh;
        Assert.Equal(replacement.Room, requestedRoom);
    }
    private void Pair(MobileRemoteController remote, bool cancel = false)
    { remote.StageOffer(JsonSerializer.Serialize(host.Offer)); remote.ConfirmPairing(true, cancel); }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    private sealed class Keys : IRemoteSecretStore
    {
        private readonly Dictionary<string, string> keys = [];
        public string Read(string key) => keys.GetValueOrDefault(key, "");
        public void Write(string key, string value) => keys[key] = value;
        public void Delete(string key) => keys.Remove(key);
    }
}
