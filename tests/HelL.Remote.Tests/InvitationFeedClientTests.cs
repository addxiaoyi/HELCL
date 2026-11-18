using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
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

public sealed class InvitationFeedClientTests
{
    [Fact]
    public void CiphertextIsBoundToPairingChannelAndRevision()
    {
        RemoteHostInvite seed = Invite("https://relay.example/");
        InvitationFeedDescriptor feed = InvitationFeedCodec.Create(DateTimeOffset.UtcNow.AddDays(7));
        seed = seed with { Feed = feed };
        InvitationFeedEnvelope envelope = InvitationFeedCodec.Protect(seed, feed, 1);
        string serialized = JsonSerializer.Serialize(envelope);
        Assert.DoesNotContain(seed.ClientTicket, serialized);
        Assert.DoesNotContain(feed.ReaderToken, serialized);
        Assert.DoesNotContain(feed.EncryptionKey, serialized);
        Assert.Equal(seed, InvitationFeedCodec.Unprotect(envelope, seed, 0));
        Assert.Throws<InvalidDataException>(() => InvitationFeedCodec.Unprotect(envelope, seed, 1));
        Assert.Throws<InvalidDataException>(() => InvitationFeedCodec.Unprotect(envelope with { Revision = 2 }, seed, 0));
        var different = feed with { Channel = new string('b', 48) };
        Assert.Throws<InvalidDataException>(() => InvitationFeedCodec.Unprotect(envelope, seed with { Feed = different }, 0));
        RemoteHostInvite otherClient = seed with { ClientDeviceId = Guid.NewGuid().ToString("N") };
        Assert.Throws<InvalidDataException>(() => InvitationFeedCodec.Unprotect(envelope, otherClient, 0));
        Assert.DoesNotContain(seed.ClientTicket, seed.ToString());
        Assert.DoesNotContain(feed.EncryptionKey, feed.ToString());
    }

    [Fact]
    public async Task EncryptedUpdatesSurvivePublisherRestartAndRevocationStopsReading()
    {
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await using WebApplication app = App(token);
        await app.StartAsync();
        Uri relay = new(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single() + "/");
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        var secrets = new Secrets();
        RemoteHostInvite first = Invite(relay.AbsoluteUri);
        var publisher = new InvitationFeedPublisher(http, relay, token, secrets, first.Host.DeviceId, first.ClientDeviceId);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        first = await publisher.PublishAsync(first, timeout.Token);
        var reader = new InvitationFeedClient(http);
        InvitationFeedUpdate? initial = await reader.ReadAsync(first, 0, timeout.Token);
        Assert.NotNull(initial);
        Assert.Equal(first, initial.Invitation);
        Assert.Null(await reader.ReadAsync(first, initial.Revision, timeout.Token));
        var reopened = new InvitationFeedPublisher(http, relay, token, secrets, first.Host.DeviceId, first.ClientDeviceId);
        Assert.Equal(publisher.Descriptor, reopened.Descriptor);
        RemoteHostInvite next = first with { Room = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant(), ClientTicket = "fictional-next-client-ticket" };
        await reopened.PublishAsync(next, timeout.Token);
        InvitationFeedUpdate? update = await reader.ReadAsync(first, initial.Revision, timeout.Token);
        Assert.NotNull(update);
        Assert.True(update.Revision > initial.Revision);
        Assert.Equal(next, update.Invitation);
        await reopened.RevokeAsync(timeout.Token);
        HttpRequestException denied = await Assert.ThrowsAsync<HttpRequestException>(() => reader.ReadAsync(next, update.Revision, timeout.Token));
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.Empty(secrets.Values);
        await app.StopAsync();
    }

    [Fact]
    public async Task AFailedSecretReservationCannotPublishOrLoseThePreviousSubscription()
    {
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await using WebApplication app = App(token);
        await app.StartAsync();
        Uri relay = new(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single() + "/");
        using var http = new HttpClient();
        var secrets = new Secrets();
        RemoteHostInvite invite = Invite(relay.AbsoluteUri);
        var publisher = new InvitationFeedPublisher(http, relay, token, secrets, invite.Host.DeviceId, invite.ClientDeviceId);
        invite = await publisher.PublishAsync(invite, CancellationToken.None);
        string previous = secrets.Values.Values.Single();
        secrets.FailWrites = true;
        await Assert.ThrowsAsync<IOException>(() => publisher.PublishAsync(invite with { ClientTicket = "fictional-uncommitted" }, CancellationToken.None));
        Assert.Equal(previous, secrets.Values.Values.Single());
        InvitationFeedUpdate? update = await new InvitationFeedClient(http).ReadAsync(invite, 0, CancellationToken.None);
        Assert.Equal(invite, update!.Invitation);
        await app.StopAsync();
    }

    [Fact]
    public async Task CancellingTheWatcherDoesNotYieldLaterUpdates()
    {
        RemoteHostInvite seed = Invite("https://relay.example/");
        InvitationFeedDescriptor descriptor = InvitationFeedCodec.Create(DateTimeOffset.UtcNow.AddDays(7));
        seed = seed with { Feed = descriptor };
        using var cancellation = new CancellationTokenSource();
        using var http = new HttpClient(new Handler(_ =>
        {
            cancellation.Cancel();
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(JsonSerializer.Serialize(InvitationFeedCodec.Protect(seed, descriptor, 1))) };
        }));
        int delivered = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (InvitationFeedUpdate update in new InvitationFeedClient(http).WatchAsync(seed, cancellation.Token)) delivered++;
        });
        Assert.Equal(0, delivered);
    }

    [Fact]
    public async Task FailedKeyDeletionAfterRevocationCannotResurrectARetiredSubscription()
    {
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await using WebApplication app = App(token);
        await app.StartAsync();
        Uri relay = new(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single() + "/");
        using var http = new HttpClient();
        var secrets = new Secrets();
        RemoteHostInvite invite = Invite(relay.AbsoluteUri);
        var publisher = new InvitationFeedPublisher(http, relay, token, secrets, invite.Host.DeviceId, invite.ClientDeviceId);
        invite = await publisher.PublishAsync(invite, CancellationToken.None);
        secrets.FailDeletes = true;
        await Assert.ThrowsAsync<IOException>(() => publisher.RevokeAsync(CancellationToken.None));
        var reopened = new InvitationFeedPublisher(http, relay, token, secrets, invite.Host.DeviceId, invite.ClientDeviceId);
        Assert.NotEqual(publisher.Descriptor.Channel, reopened.Descriptor.Channel);
        RemoteHostInvite fresh = await reopened.PublishAsync(invite with { Feed = null }, CancellationToken.None);
        Assert.NotNull(await new InvitationFeedClient(http).ReadAsync(fresh, 0, CancellationToken.None));
        await Assert.ThrowsAsync<HttpRequestException>(() => new InvitationFeedClient(http).ReadAsync(invite, 0, CancellationToken.None));
        await app.StopAsync();
    }

    [Fact]
    public async Task ALockedLocalKeyStoreCannotPreventRemoteRevocation()
    {
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await using WebApplication app = App(token);
        await app.StartAsync();
        Uri relay = new(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single() + "/");
        using var http = new HttpClient();
        var secrets = new Secrets();
        RemoteHostInvite invite = Invite(relay.AbsoluteUri);
        var publisher = new InvitationFeedPublisher(http, relay, token, secrets, invite.Host.DeviceId, invite.ClientDeviceId);
        invite = await publisher.PublishAsync(invite, CancellationToken.None);
        secrets.FailWrites = true;
        await Assert.ThrowsAsync<IOException>(() => publisher.RevokeAsync(CancellationToken.None));
        HttpRequestException denied = await Assert.ThrowsAsync<HttpRequestException>(() => new InvitationFeedClient(http).ReadAsync(invite, 0, CancellationToken.None));
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.PublishAsync(invite, CancellationToken.None));
        await app.StopAsync();
    }

    private static WebApplication App(string token) => RelayApp.Build([], builder =>
    {
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Relay:OperatorToken"] = token,
            ["Relay:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ["urls"] = "http://127.0.0.1:0"
        });
        builder.Logging.ClearProviders();
    });

    private static RemoteHostInvite Invite(string relay) => new(new(Guid.NewGuid().ToString("N"), "desktop", new string('A', 64)),
        Guid.NewGuid().ToString("N"), relay, new string('a', 48), "fictional-client-ticket", DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds());

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request));
    }

    private sealed class Secrets : IRemoteSecretStore
    {
        public Dictionary<string, string> Values { get; } = [];
        public bool FailWrites;
        public bool FailDeletes;
        public string Read(string key) => Values.GetValueOrDefault(key, "");
        public void Write(string key, string value)
        {
            if (FailWrites) throw new IOException("The test key store is locked.");
            Values[key] = value;
        }
        public void Delete(string key)
        {
            if (FailDeletes) throw new IOException("The test key store cannot delete this entry.");
            Values.Remove(key);
        }
    }
}
