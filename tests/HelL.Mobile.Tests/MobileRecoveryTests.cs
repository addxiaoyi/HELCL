using System.Text.Json;
using System.Threading.Channels;
using HelL.Contracts;
using HelL.Mobile.Remote;
using HelL.Remote;
using Xunit;

namespace HelL.Mobile.Tests;

public sealed class MobileRecoveryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "hell-mobile-recovery-" + Guid.NewGuid().ToString("N"));
    private readonly Keys keys = new();
    private readonly RemotePairingStore phone;
    private readonly RemotePairingStore first;
    private readonly RemotePairingStore second;

    public MobileRecoveryTests()
    {
        phone = new RemotePairingStore(Path.Combine(root, "phone"), "phone", keys);
        first = new RemotePairingStore(Path.Combine(root, "first"), "first", new Keys());
        second = new RemotePairingStore(Path.Combine(root, "second"), "second", new Keys());
        phone.Trust(first.Offer, [HostPermission.Read, HostPermission.ControlGame]);
        phone.Trust(second.Offer, [HostPermission.Read]);
    }

    [Fact]
    public void MultipleInvitationsAndSelectionSurviveRestartWithoutPlaintextCapabilities()
    {
        using (var remote = new MobileRemoteController(phone))
        {
            remote.ImportInvitation(JsonSerializer.Serialize(Invite(first)));
            remote.ImportInvitation(JsonSerializer.Serialize(Invite(second)));
            remote.SelectHost(first.Offer.DeviceId);
        }
        using var reopened = new MobileRemoteController(Reopen());
        Assert.Equal(first.Offer.DeviceId, reopened.SelectedHostId);
        Assert.Equal(first.Offer.DeviceId, reopened.Invitation?.Host.DeviceId);
        reopened.SelectHost(second.Offer.DeviceId);
        Assert.Equal(second.Offer.DeviceId, reopened.Invitation?.Host.DeviceId);
        string bytes = File.ReadAllText(Path.Combine(root, "phone", "remote-pairing.json"));
        Assert.DoesNotContain("private-client-ticket", bytes);
        Assert.DoesNotContain(first.Offer.Fingerprint, bytes);
    }

    [Fact]
    public async Task ExpiredRoomTicketCannotReachTransportAndOnlyVerifiedRenewalUnlocksIt()
    {
        RemoteHostInvite expired = Invite(first) with { Expires = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds(), Feed = InvitationFeedCodec.Create(DateTimeOffset.UtcNow.AddDays(1)) };
        phone.SaveInvitation(expired);
        var channel = Channel.CreateUnbounded<InvitationFeedUpdate>();
        int calls = 0;
        using var remote = new MobileRemoteController(Reopen(), (_, request, _) =>
        {
            calls++;
            return Task.FromResult(new HostReply { RequestId = request.RequestId, Ok = true });
        }, (_, token) => channel.Reader.ReadAllAsync(token));
        Assert.True(remote.Watching);
        await Assert.ThrowsAsync<InvalidDataException>(() => remote.RefreshAsync());
        await remote.RestoreConnectionAsync();
        Assert.Equal(0, calls);
        RemoteHostInvite renewed = expired with { Expires = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(), Room = new string('d', 48) };
        await channel.Writer.WriteAsync(new InvitationFeedUpdate(2, renewed));
        Assert.True(SpinWait.SpinUntil(() => remote.Invitation?.Room == renewed.Room, TimeSpan.FromSeconds(2)));
        Assert.True(SpinWait.SpinUntil(() => calls == 1 && !remote.Busy, TimeSpan.FromSeconds(2)));
        Assert.Equal(1, calls);
        Assert.NotNull(Reopen().LastConnected(first.Offer.DeviceId));
    }

    [Fact]
    public async Task RestoredValidInvitationAutomaticallyReadsTasksWhenTheRemotePageOpens()
    {
        phone.SaveInvitation(Invite(first));
        int calls = 0;
        using var remote = new MobileRemoteController(Reopen(), (_, request, _) =>
        {
            calls++;
            return Task.FromResult(new HostReply { RequestId = request.RequestId, Ok = true, Tasks = [new HostTask { Id = "restored" }] });
        });
        await remote.RestoreConnectionAsync();
        Assert.Equal(1, calls);
        Assert.Equal("restored", Assert.Single(remote.Tasks).Id);
    }

    [Fact]
    public void FailedInvitationWritePreservesLastSavedAndInMemoryInvitation()
    {
        using var remote = new MobileRemoteController(phone);
        RemoteHostInvite original = Invite(first);
        remote.ImportInvitation(JsonSerializer.Serialize(original));
        byte[] previous = File.ReadAllBytes(Path.Combine(root, "phone", "remote-pairing.json"));
        keys.FailWrites = true;
        Assert.Throws<IOException>(() => remote.ImportInvitation(JsonSerializer.Serialize(Invite(second))));
        Assert.Equal(first.Offer.DeviceId, remote.SelectedHostId);
        Assert.Equal(original.Room, remote.Invitation?.Room);
        Assert.Equal(previous, File.ReadAllBytes(Path.Combine(root, "phone", "remote-pairing.json")));
        keys.FailWrites = false;
        Assert.Equal(first.Offer.DeviceId, Assert.Single(Reopen().Invitations()).Host.DeviceId);
    }

    [Fact]
    public void RevocationClearsOnlyTheAffectedHostsInvitationAndActivity()
    {
        phone.SaveInvitation(Invite(first));
        phone.RecordConnection(first.Offer.DeviceId);
        phone.SaveInvitation(Invite(second));
        using var remote = new MobileRemoteController(phone);
        remote.Revoke(first.Offer.DeviceId);
        RemotePairingStore reopened = Reopen();
        Assert.Equal(second.Offer.DeviceId, Assert.Single(reopened.Invitations()).Host.DeviceId);
        Assert.Null(reopened.LastConnected(first.Offer.DeviceId));
        Assert.Equal(second.Offer.DeviceId, remote.Invitation?.Host.DeviceId);
    }

    [Fact]
    public async Task RestoredFeedRevisionRejectsRollbackAfterProcessRestart()
    {
        RemoteHostInvite initial = Invite(first) with { Feed = InvitationFeedCodec.Create(DateTimeOffset.UtcNow.AddDays(1)) };
        phone.SaveInvitation(initial, 4);
        var channel = Channel.CreateUnbounded<InvitationFeedUpdate>();
        using var remote = new MobileRemoteController(Reopen(), watch: (_, token) => channel.Reader.ReadAllAsync(token));
        await channel.Writer.WriteAsync(new InvitationFeedUpdate(3, initial with { Room = new string('d', 48) }));
        await Task.Delay(100);
        Assert.Equal(initial.Room, remote.Invitation?.Room);
        Assert.Equal(4, Reopen().InvitationRevision(first.Offer.DeviceId));
        RemoteHostInvite latest = initial with { Room = new string('e', 48) };
        await channel.Writer.WriteAsync(new InvitationFeedUpdate(5, latest));
        Assert.True(SpinWait.SpinUntil(() => remote.Invitation?.Room == latest.Room, TimeSpan.FromSeconds(2)));
        Assert.Equal(5, Reopen().InvitationRevision(first.Offer.DeviceId));
    }

    [Fact]
    public async Task SwitchingHostsClearsDetailsAndLateRepliesCannotRestoreThem()
    {
        using var remote = new MobileRemoteController(phone, (_, request, _) => Task.FromResult(new HostReply
        {
            RequestId = request.RequestId, Ok = true, Tasks = [new HostTask { Id = "task-1" }],
            Detail = new HostTaskDetails { TaskId = request.TaskId, RecentLogs = ["first-host-log"] },
            Game = new HostGameStatus { Running = true, SessionId = new string('a', 32), State = "running" }
        }));
        remote.ImportInvitation(JsonSerializer.Serialize(Invite(first)));
        await remote.RefreshAsync();
        await remote.ReadTaskDetailsAsync("task-1");
        await remote.ReadGameAsync();
        Assert.NotNull(remote.Detail);
        Assert.NotNull(remote.Game);
        remote.ImportInvitation(JsonSerializer.Serialize(Invite(second)));
        Assert.Null(remote.Detail);
        Assert.Null(remote.Game);
        Assert.Empty(remote.Tasks);
        remote.SelectHost(first.Offer.DeviceId);
        Assert.Null(remote.Detail);
        Assert.Null(remote.Game);
    }

    [Fact]
    public async Task AReplyFromThePreviousHostCannotPopulateTheSelectedHostsGameState()
    {
        var pending = new TaskCompletionSource<HostReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        HostRequest? request = null;
        using var remote = new MobileRemoteController(phone, (_, command, _) => { request = command; return pending.Task; });
        remote.ImportInvitation(JsonSerializer.Serialize(Invite(first)));
        Task reading = remote.ReadGameAsync();
        remote.ImportInvitation(JsonSerializer.Serialize(Invite(second)));
        pending.SetResult(new HostReply { RequestId = request!.RequestId, Ok = true,
            Game = new HostGameStatus { Running = true, SessionId = new string('a', 32), State = "running" } });
        await reading;
        Assert.Equal(second.Offer.DeviceId, remote.SelectedHostId);
        Assert.Null(remote.Game);
    }

    [Fact]
    public async Task OversizedOrMismatchedTaskDetailsDoNotReplaceTheLastValidDetails()
    {
        bool malformed = false;
        using var remote = new MobileRemoteController(phone, (_, request, _) => Task.FromResult(new HostReply
        {
            RequestId = request.RequestId, Ok = true, Tasks = [new HostTask { Id = "task-1" }],
            Detail = new HostTaskDetails { TaskId = request.TaskId, RecentLogs = malformed ? [new string('x', 513)] : ["bounded-line"] }
        }));
        remote.ImportInvitation(JsonSerializer.Serialize(Invite(first)));
        await remote.RefreshAsync();
        await remote.ReadTaskDetailsAsync("task-1");
        malformed = true;
        await remote.ReadTaskDetailsAsync("task-1");
        Assert.Equal("bounded-line", Assert.Single(remote.Detail!.RecentLogs));
        Assert.NotEmpty(remote.Error);
    }

    [Fact]
    public async Task GracefulStopNeedsSeparatePermissionCurrentSessionAndExplicitConfirmation()
    {
        var sent = new List<HostRequest>();
        using var remote = new MobileRemoteController(phone, (_, request, _) =>
        {
            sent.Add(request);
            return Task.FromResult(new HostReply { RequestId = request.RequestId, Ok = true,
                Game = new HostGameStatus { Running = true, SessionId = new string('a', 32), State = "running" } });
        });
        remote.ImportInvitation(JsonSerializer.Serialize(Invite(first)));
        await remote.ReadGameAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => remote.StopGameAsync(new string('a', 32), false));
        await Assert.ThrowsAsync<InvalidOperationException>(() => remote.StopGameAsync("old-session", true));
        await remote.StopGameAsync(new string('a', 32), true);
        Assert.Equal(HostCommand.StopGame, sent.Last().Command);
        remote.ImportInvitation(JsonSerializer.Serialize(Invite(second)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => remote.StopGameAsync(new string('a', 32), true));
    }

    [Fact]
    public async Task AGameWithoutACloseableWindowShowsTheHostsSafeRefusalInsteadOfRawErrors()
    {
        using var remote = new MobileRemoteController(phone, (_, request, _) => Task.FromResult(
            request.Command == HostCommand.GameStatus
                ? new HostReply { RequestId = request.RequestId, Ok = true,
                    Game = new HostGameStatus { Running = true, SessionId = new string('a', 32), State = "running" } }
                : new HostReply { RequestId = request.RequestId, ErrorCode = "graceful_stop_unavailable", Error = "private-path-and-token-must-not-appear" }));
        remote.ImportInvitation(JsonSerializer.Serialize(Invite(first)));
        await remote.ReadGameAsync();
        await remote.StopGameAsync(new string('a', 32), true);
        Assert.Contains("窗口", remote.Error);
        Assert.Contains("不会强制结束", remote.Error);
        Assert.DoesNotContain("private-path", remote.Error);
        Assert.True(remote.Game?.Running);
        Assert.False(remote.Busy);
    }

    private RemotePairingStore Reopen() => new(Path.Combine(root, "phone"), "phone", keys);

    [Fact]
    public void RevokingASelectedHostWithoutAnInvitationClearsItsSelection()
    {
        using var remote = new MobileRemoteController(phone);
        remote.SelectHost(first.Offer.DeviceId);
        Assert.Equal(first.Offer.DeviceId, remote.SelectedHostId);
        remote.Revoke(first.Offer.DeviceId);
        Assert.Null(remote.SelectedHostId);
        Assert.Null(remote.Invitation);
        Assert.Equal("", Reopen().SelectedHostId);
    }
    private RemoteHostInvite Invite(RemotePairingStore host) => new(host.Offer, phone.Offer.DeviceId, "https://relay.example/", new string('a', 48), "private-client-ticket", DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds());
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    private sealed class Keys : IRemoteSecretStore
    {
        private readonly Dictionary<string, string> values = [];
        public bool FailWrites { get; set; }
        public string Read(string key) => values.GetValueOrDefault(key, "");
        public void Write(string key, string value) { if (FailWrites) throw new IOException("key store is locked"); values[key] = value; }
        public void Delete(string key) => values.Remove(key);
    }
}
