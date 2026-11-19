using System.Security.Cryptography;
using System.Net.WebSockets;
using HelL.Contracts;
using HelL.Relay;
using HelL.Remote;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HelL.Remote.Tests;

public sealed class PairedRemoteHostTests
{
    [Fact]
    public async Task ExplicitPairingEnforcesPermissionsAndRevokesTheRoom()
    {
        await using var fixture = await Setup.OpenAsync();
        var unpaired = new RemotePairingStore(Path.Combine(fixture.Root, "unpaired"), "unpaired", new Secrets());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Host.StartAsync(unpaired.Offer.DeviceId,
            (_, _) => Task.CompletedTask, fixture.Timeout.Token));
        RemoteHostInvite invite = await fixture.Host.StartAsync(fixture.Phone.Offer.DeviceId, (_, _) => Task.CompletedTask, fixture.Timeout.Token);
        HostReply snapshot = await fixture.Client.SnapshotAsync(invite, fixture.Timeout.Token);
        Assert.True(snapshot.Ok);
        Assert.Equal("existing-launcher", snapshot.Device);
        HostReply denied = await fixture.Client.ExecuteAsync(invite, new HostRequest
            { RequestId = Guid.NewGuid().ToString("N"), Command = HostCommand.CancelTask, TaskId = "running-task" }, fixture.Timeout.Token);
        Assert.Equal("permission_denied", denied.ErrorCode);
        Assert.Equal(1, fixture.Executed);
        await fixture.Host.RevokeAsync(fixture.Phone.Offer.DeviceId, fixture.Timeout.Token);
        Assert.Null(new RemotePairingStore(Path.Combine(fixture.Root, "desktop"), "desktop", fixture.DesktopSecrets).Find(fixture.Phone.Offer.DeviceId));
        await Assert.ThrowsAsync<WebSocketException>(() => fixture.Client.SnapshotAsync(invite, fixture.Timeout.Token));
    }

    [Fact]
    public async Task RenewalPublishesANewInviteAndRejectsTheRetiredRoom()
    {
        await using var fixture = await Setup.OpenAsync();
        RemoteHostInvite first = await fixture.Host.StartAsync(fixture.Phone.Offer.DeviceId, (_, _) => Task.CompletedTask, fixture.Timeout.Token);
        Assert.True((await fixture.Client.SnapshotAsync(first, fixture.Timeout.Token)).Ok);
        RemoteHostInvite next = await fixture.Host.RenewAsync(fixture.Phone.Offer.DeviceId, fixture.Timeout.Token);
        Assert.NotEqual(first.Room, next.Room);
        Assert.True((await fixture.Client.SnapshotAsync(next, fixture.Timeout.Token)).Ok);
        await Assert.ThrowsAsync<WebSocketException>(() => fixture.Client.SnapshotAsync(first, fixture.Timeout.Token));
    }

    [Fact]
    public async Task ReadOnlyGameAndTaskQueriesCannotAuthorizeAGameStop()
    {
        await using var fixture = await Setup.OpenAsync();
        RemoteHostInvite invite = await fixture.Host.StartAsync(fixture.Phone.Offer.DeviceId, (_, _) => Task.CompletedTask, fixture.Timeout.Token);
        HostReply details = await fixture.Client.ExecuteAsync(invite, new HostRequest
        { RequestId = Guid.NewGuid().ToString("N"), Command = HostCommand.TaskDetails, TaskId = "task-fixture" }, fixture.Timeout.Token);
        HostReply status = await fixture.Client.ExecuteAsync(invite, new HostRequest
        { RequestId = Guid.NewGuid().ToString("N"), Command = HostCommand.GameStatus }, fixture.Timeout.Token);
        Assert.True(details.Ok);
        Assert.True(status.Ok);
        HostReply denied = await fixture.Client.ExecuteAsync(invite, new HostRequest
        {
            RequestId = Guid.NewGuid().ToString("N"), Command = HostCommand.StopGame,
            SessionId = Guid.NewGuid().ToString("N")
        }, fixture.Timeout.Token);
        Assert.Equal("permission_denied", denied.ErrorCode);
        Assert.Equal(2, fixture.Executed);
    }

    [Fact]
    public async Task AutomaticRenewalPublishesAnInviteAndExternalRevocationClosesTheHost()
    {
        await using var fixture = await Setup.OpenAsync(TimeSpan.FromMilliseconds(100));
        var renewed = new TaskCompletionSource<RemoteHostInvite>(TaskCreationOptions.RunContinuationsAsynchronously);
        int invitations = 0;
        RemoteHostInvite first = await fixture.Host.StartAsync(fixture.Phone.Offer.DeviceId, (invite, _) =>
        {
            if (Interlocked.Increment(ref invitations) == 2) renewed.TrySetResult(invite);
            return Task.CompletedTask;
        }, fixture.Timeout.Token);
        RemoteHostInvite next = await renewed.Task.WaitAsync(fixture.Timeout.Token);
        Assert.NotEqual(first.Room, next.Room);
        var settings = new RemotePairingStore(Path.Combine(fixture.Root, "desktop"), "desktop", fixture.DesktopSecrets);
        settings.Revoke(fixture.Phone.Offer.DeviceId);
        await fixture.Host.Completion(fixture.Phone.Offer.DeviceId).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(fixture.Desktop.Find(fixture.Phone.Offer.DeviceId));
    }

    [Fact]
    public async Task AForwardedInviteCannotSilentlyTrustAnUnknownHost()
    {
        await using var fixture = await Setup.OpenAsync();
        RemoteHostInvite invite = await fixture.Host.StartAsync(fixture.Phone.Offer.DeviceId, (_, _) => Task.CompletedTask, fixture.Timeout.Token);
        fixture.Phone.Revoke(fixture.Desktop.Offer.DeviceId);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Client.SnapshotAsync(invite, fixture.Timeout.Token));
        Assert.Equal(0, fixture.Executed);
    }

    [Fact]
    public async Task RevocationDuringInvitationPublicationCannotStartAStaleSession()
    {
        await using var fixture = await Setup.OpenAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Host.StartAsync(fixture.Phone.Offer.DeviceId, (_, _) =>
        {
            fixture.Desktop.Revoke(fixture.Phone.Offer.DeviceId);
            return Task.CompletedTask;
        }, fixture.Timeout.Token));
        Assert.True(fixture.Host.Completion(fixture.Phone.Offer.DeviceId).IsCompleted);
        Assert.Equal(0, fixture.Executed);
    }

    [Fact]
    public async Task DisposalCancelsAnInvitationBeingPublishedBeforeReturning()
    {
        await using var fixture = await Setup.OpenAsync();
        var publishing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task startup = fixture.Host.StartAsync(fixture.Phone.Offer.DeviceId, async (_, cancellation) =>
        {
            publishing.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellation);
        }, fixture.Timeout.Token);
        await publishing.Task.WaitAsync(fixture.Timeout.Token);
        await fixture.Host.DisposeAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => startup);
        Assert.True(fixture.Host.Completion(fixture.Phone.Offer.DeviceId).IsCompleted);
    }

    [Fact]
    public async Task DisposalCancelsABlockedRenewalPublisher()
    {
        await using var fixture = await Setup.OpenAsync();
        int invitations = 0;
        var publishing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await fixture.Host.StartAsync(fixture.Phone.Offer.DeviceId, async (_, cancellation) =>
        {
            if (Interlocked.Increment(ref invitations) == 1) return;
            publishing.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellation);
        }, fixture.Timeout.Token);
        Task renewal = fixture.Host.RenewAsync(fixture.Phone.Offer.DeviceId, CancellationToken.None);
        await publishing.Task.WaitAsync(fixture.Timeout.Token);
        await fixture.Host.DisposeAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => renewal);
        Assert.True(fixture.Host.Completion(fixture.Phone.Offer.DeviceId).IsCompleted);
    }

    [Fact]
    public async Task AnInternalRenewalCancellationCannotReportNormalHostingCompletion()
    {
        await using var fixture = await Setup.OpenAsync(TimeSpan.FromMilliseconds(100), new RenewalCancelledHandler());
        await fixture.Host.StartAsync(fixture.Phone.Offer.DeviceId, (_, _) => Task.CompletedTask, fixture.Timeout.Token);
        await Assert.ThrowsAsync<IOException>(() => fixture.Host.Completion(fixture.Phone.Offer.DeviceId).WaitAsync(fixture.Timeout.Token));
        await Assert.ThrowsAsync<IOException>(() => fixture.Host.StopAsync(fixture.Phone.Offer.DeviceId, fixture.Timeout.Token));
    }

    [Fact]
    public async Task AnInternalInvitationPublicationCancellationCannotReportNormalHostingCompletion()
    {
        await using var fixture = await Setup.OpenAsync(TimeSpan.FromMilliseconds(100));
        int publications = 0;
        await fixture.Host.StartAsync(fixture.Phone.Offer.DeviceId, (_, _) => ++publications == 1
            ? Task.CompletedTask : Task.FromCanceled(new CancellationToken(true)), fixture.Timeout.Token);
        await Assert.ThrowsAsync<IOException>(() => fixture.Host.Completion(fixture.Phone.Offer.DeviceId).WaitAsync(fixture.Timeout.Token));
        await Assert.ThrowsAsync<IOException>(() => fixture.Host.StopAsync(fixture.Phone.Offer.DeviceId, fixture.Timeout.Token));
    }

    private sealed class RenewalCancelledHandler : DelegatingHandler
    {
        internal RenewalCancelledHandler() : base(new HttpClientHandler()) { }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            request.RequestUri!.AbsolutePath.EndsWith("/renew", StringComparison.Ordinal)
                ? Task.FromCanceled<HttpResponseMessage>(new CancellationToken(true))
                : base.SendAsync(request, cancellationToken);
    }

    private sealed class Secrets : IRemoteSecretStore
    {
        private readonly Dictionary<string, string> keys = [];
        public string Read(string key) => keys.GetValueOrDefault(key, "");
        public void Write(string key, string value) => keys[key] = value;
        public void Delete(string key) => keys.Remove(key);
    }

    private sealed class Setup : IAsyncDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "hell-paired-host-" + Guid.NewGuid().ToString("N"));
        public Secrets DesktopSecrets { get; } = new();
        public RemotePairingStore Desktop { get; private set; } = null!;
        public RemotePairingStore Phone { get; private set; } = null!;
        public PairedRemoteHost Host { get; private set; } = null!;
        public PairedRemoteClient Client { get; private set; } = null!;
        public CancellationTokenSource Timeout { get; } = new(TimeSpan.FromSeconds(25));
        public int Executed;
        private readonly HttpClient http;
        private WebApplication app = null!;

        private Setup(HttpMessageHandler? handler) => http = handler is null ? new HttpClient() : new HttpClient(handler);

        public static async Task<Setup> OpenAsync(TimeSpan? renewal = null, HttpMessageHandler? handler = null)
        {
            var fixture = new Setup(handler);
            string token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            fixture.app = RelayApp.Build([], builder =>
            {
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Relay:OperatorToken"] = token,
                    ["Relay:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                    ["urls"] = "http://127.0.0.1:0"
                });
                builder.Logging.ClearProviders();
            });
            await fixture.app.StartAsync();
            Uri relay = new(fixture.app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single() + "/");
            fixture.Desktop = new RemotePairingStore(Path.Combine(fixture.Root, "desktop"), "desktop", fixture.DesktopSecrets);
            fixture.Phone = new RemotePairingStore(Path.Combine(fixture.Root, "phone"), "phone", new Secrets());
            fixture.Desktop.Trust(fixture.Phone.Offer, [HostPermission.Read]);
            fixture.Phone.Trust(fixture.Desktop.Offer, [HostPermission.Read]);
            fixture.Host = new PairedRemoteHost(fixture.Desktop, fixture.http, relay, token, (request, _) =>
            {
                Interlocked.Increment(ref fixture.Executed);
                return Task.FromResult(new HostReply { RequestId = request.RequestId, Device = "existing-launcher", Ok = true });
            }, renewal);
            fixture.Client = new PairedRemoteClient(fixture.Phone);
            return fixture;
        }

        public async ValueTask DisposeAsync()
        {
            await Host.DisposeAsync();
            await app.StopAsync();
            await app.DisposeAsync();
            http.Dispose();
            Timeout.Dispose();
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }
}
