using System.Collections.Concurrent;
using System.Net;
using HelL.Contracts;
using HelL.Remote;
using Xunit;

namespace HelL.Remote.Tests;

public sealed class RemoteRecoveryTests
{
    [Fact]
    public void RefreshReloadsTheCommittedSnapshotWhenAnotherInstanceRetiresItsEncryptionKey()
    {
        string directory = Path.Combine(Path.GetTempPath(), "hell-pairing-rotation-" + Guid.NewGuid().ToString("N"));
        try
        {
            var secrets = new Secrets();
            var writer = new RemotePairingStore(directory, "desktop", secrets);
            using var identity = DeviceTunnel.CreateIdentity("phone");
            var offer = new RemotePairingOffer(Guid.NewGuid().ToString("N"), "phone", DeviceTunnel.Fingerprint(identity));
            writer.Trust(offer, [HostPermission.Read]);
            var reader = new RemotePairingStore(directory, "desktop", secrets);
            string oldKey = secrets.Values.Keys.Single();
            bool rotated = false;
            secrets.BeforeRead = key =>
            {
                if (key != oldKey || rotated) return;
                rotated = true;
                writer.Trust(offer, [HostPermission.Read, HostPermission.CancelTasks]);
            };

            reader.Refresh();

            Assert.True(rotated);
            Assert.Equal([HostPermission.Read, HostPermission.CancelTasks], reader.Find(offer.DeviceId)!.Permissions);
            Assert.Equal(writer.Offer, reader.Offer);
            Assert.Single(secrets.Values);
            Assert.DoesNotContain(oldKey, secrets.Values.Keys);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task FailedReaderRevocationRemainsRecoverableAfterPublisherRestart()
    {
        await using var relay = await RunningRelay.StartAsync();
        using var faults = new DeleteFaults { InnerHandler = new HttpClientHandler() };
        using var http = new HttpClient(faults);
        var secrets = new Secrets();
        RemoteHostInvite invite = new(new(Guid.NewGuid().ToString("N"), "desktop", new string('A', 64)),
            Guid.NewGuid().ToString("N"), relay.Address.AbsoluteUri, new string('a', 48), "fictional-client-ticket",
            DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds());
        var publisher = new InvitationFeedPublisher(http, relay.Address, relay.OperatorToken, secrets,
            invite.Host.DeviceId, invite.ClientDeviceId);
        RemoteHostInvite previous = await publisher.PublishAsync(invite, CancellationToken.None);
        faults.FailDeletes = true;
        await Assert.ThrowsAsync<IOException>(() => publisher.RevokeAsync(CancellationToken.None));
        Assert.NotNull(await new InvitationFeedClient(relay.Http).ReadAsync(previous, 0, CancellationToken.None));
        string pending = Assert.Single(secrets.Values).Value;

        var reopened = new InvitationFeedPublisher(http, relay.Address, relay.OperatorToken, secrets,
            invite.Host.DeviceId, invite.ClientDeviceId);
        Assert.NotEqual(previous.Feed!.Channel, reopened.Descriptor.Channel);
        int published = faults.Publications;
        await Assert.ThrowsAsync<IOException>(() => reopened.PublishAsync(invite, CancellationToken.None));
        Assert.Equal(published, faults.Publications);
        Assert.Equal(pending, Assert.Single(secrets.Values).Value);

        faults.FailDeletes = false;
        secrets.FailWrites = true;
        await Assert.ThrowsAsync<IOException>(() => reopened.PublishAsync(invite, CancellationToken.None));
        Assert.Equal(published, faults.Publications);
        Assert.Equal(pending, Assert.Single(secrets.Values).Value);
        secrets.FailWrites = false;
        RemoteHostInvite fresh = await reopened.PublishAsync(invite, CancellationToken.None);
        Assert.NotNull(await new InvitationFeedClient(relay.Http).ReadAsync(fresh, 0, CancellationToken.None));
        HttpRequestException denied = await Assert.ThrowsAsync<HttpRequestException>(() =>
            new InvitationFeedClient(relay.Http).ReadAsync(previous, 0, CancellationToken.None));
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.Single(secrets.Values);
        string current = Assert.Single(secrets.Values).Value;
        await Assert.ThrowsAsync<IOException>(() => publisher.RevokeAsync(CancellationToken.None));
        Assert.Equal(current, Assert.Single(secrets.Values).Value);
        Assert.NotNull(await new InvitationFeedClient(relay.Http).ReadAsync(fresh, 0, CancellationToken.None));
    }

    [Fact]
    public async Task AStalePublisherCannotRewindThePersistedRevisionOrSendAnotherUpdate()
    {
        await using var relay = await RunningRelay.StartAsync();
        using var transport = new DeleteFaults { InnerHandler = new HttpClientHandler() };
        using var http = new HttpClient(transport);
        var secrets = new Secrets();
        RemoteHostInvite invite = new(new(Guid.NewGuid().ToString("N"), "desktop", new string('A', 64)),
            Guid.NewGuid().ToString("N"), relay.Address.AbsoluteUri, new string('a', 48), "fictional-client-ticket",
            DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds());
        var first = new InvitationFeedPublisher(http, relay.Address, relay.OperatorToken, secrets,
            invite.Host.DeviceId, invite.ClientDeviceId);
        invite = await first.PublishAsync(invite, CancellationToken.None);
        var stale = new InvitationFeedPublisher(http, relay.Address, relay.OperatorToken, secrets,
            invite.Host.DeviceId, invite.ClientDeviceId);
        await first.PublishAsync(invite, CancellationToken.None);
        string current = Assert.Single(secrets.Values).Value;
        int sent = transport.Publications;

        await Assert.ThrowsAsync<IOException>(() => stale.PublishAsync(invite, CancellationToken.None));

        Assert.Equal(sent, transport.Publications);
        Assert.Equal(current, Assert.Single(secrets.Values).Value);
        InvitationFeedUpdate? update = await new InvitationFeedClient(relay.Http).ReadAsync(invite, 0, CancellationToken.None);
        Assert.Equal(2, update!.Revision);
    }

    private sealed class DeleteFaults : DelegatingHandler
    {
        internal bool FailDeletes;
        internal int Publications;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Delete && FailDeletes) throw new IOException("The test relay is unavailable during revocation.");
            if (request.Method == HttpMethod.Put) Publications++;
            return base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class Secrets : IRemoteSecretStore
    {
        internal ConcurrentDictionary<string, string> Values { get; } = new();
        internal Action<string>? BeforeRead;
        internal bool FailWrites;
        public string Read(string key) { BeforeRead?.Invoke(key); return Values.GetValueOrDefault(key, ""); }
        public void Write(string key, string value)
        {
            if (FailWrites) throw new IOException("The test key store is locked.");
            Values[key] = value;
        }
        public void Delete(string key) => Values.TryRemove(key, out _);
    }
}
