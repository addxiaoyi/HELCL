using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
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

public sealed class RelaySecurityTests
{
    [Fact]
    public async Task AnonymousRoomRequestsCannotSpendTheOperatorsBudget()
    {
        await using var relay = await RunningRelay.StartAsync();
        for (int attempt = 0; attempt < 11; attempt++)
        {
            using HttpResponseMessage denied = await relay.Http.PostAsync("v1/rooms", null);
            Assert.Contains(denied.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests });
        }
        relay.Authorize();
        using HttpResponseMessage created = await relay.Http.PostAsync("v1/rooms", null);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
    }

    [Fact]
    public async Task RevokingARoomRejectsItsStillValidTickets()
    {
        await using var relay = await RunningRelay.StartAsync();
        RelayRoom room = await relay.CreateRoomAsync();
        using HttpResponseMessage revoked = await relay.Http.DeleteAsync("v1/rooms/" + room.Room);
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        using var replay = new HttpRequestMessage(HttpMethod.Get, "v1/tunnel");
        replay.Headers.Authorization = new AuthenticationHeaderValue("Bearer", room.ClientTicket);
        using HttpResponseMessage denied = await relay.Http.SendAsync(replay);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OnlyExplicitlyTrustedProxiesCanSupplyTheRateLimitAddress(bool trusted)
    {
        var settings = new Dictionary<string, string?> { ["Relay:AnonymousRequestsPerMinute"] = "1" };
        if (trusted) settings["Relay:KnownProxies:0"] = "127.0.0.1";
        await using var relay = await RunningRelay.StartAsync(settings);
        using var first = new HttpRequestMessage(HttpMethod.Post, "v1/rooms");
        first.Headers.Add("X-Forwarded-For", "192.0.2.1");
        using HttpResponseMessage denied = await relay.Http.SendAsync(first);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using var second = new HttpRequestMessage(HttpMethod.Post, "v1/rooms");
        second.Headers.Add("X-Forwarded-For", "192.0.2.2");
        using HttpResponseMessage next = await relay.Http.SendAsync(second);
        Assert.Equal(trusted ? HttpStatusCode.Unauthorized : HttpStatusCode.TooManyRequests, next.StatusCode);
    }

    [Fact]
    public async Task RenewingARoomRevokesTheOldRoomAndReturnsFreshTickets()
    {
        await using var relay = await RunningRelay.StartAsync();
        RelayRoom first = await relay.CreateRoomAsync();
        RelayRoom next = await RemoteHostChannel.RenewRoomAsync(relay.Http, relay.Address, relay.OperatorToken, first.Room, CancellationToken.None);
        Assert.NotEqual(first.Room, next.Room);
        Assert.NotEqual(first.HostTicket, next.HostTicket);
        using var replay = new HttpRequestMessage(HttpMethod.Get, "v1/tunnel");
        replay.Headers.Authorization = new("Bearer", first.ClientTicket);
        using HttpResponseMessage denied = await relay.Http.SendAsync(replay);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using var replacement = new HttpRequestMessage(HttpMethod.Get, "v1/tunnel");
        replacement.Headers.Authorization = new("Bearer", next.ClientTicket);
        using HttpResponseMessage accepted = await relay.Http.SendAsync(replacement);
        Assert.Equal(HttpStatusCode.BadRequest, accepted.StatusCode);
        await RemoteHostChannel.RevokeRoomAsync(relay.Http, relay.Address, relay.OperatorToken, next.Room, CancellationToken.None);
    }

    [Fact]
    public async Task AnonymousRequestsCannotRevokeOrRenewARoom()
    {
        await using var relay = await RunningRelay.StartAsync();
        RelayRoom room = await relay.CreateRoomAsync();
        relay.Http.DefaultRequestHeaders.Authorization = null;
        using HttpResponseMessage revoke = await relay.Http.DeleteAsync("v1/rooms/" + room.Room);
        using HttpResponseMessage renew = await relay.Http.PostAsync("v1/rooms/" + room.Room + "/renew", null);
        Assert.Equal(HttpStatusCode.Unauthorized, revoke.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, renew.StatusCode);
    }

    [Fact]
    public async Task SignedTunnelRequestsHaveABudgetEvenWithoutAWebSocketUpgrade()
    {
        await using var relay = await RunningRelay.StartAsync(new() { ["Relay:ConnectionsPerWindow"] = "2" });
        RelayRoom room = await relay.CreateRoomAsync();
        for (int attempt = 0; attempt < 3; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "v1/tunnel");
            request.Headers.Authorization = new("Bearer", room.HostTicket);
            using HttpResponseMessage reply = await relay.Http.SendAsync(request);
            Assert.Equal(attempt < 2 ? HttpStatusCode.BadRequest : HttpStatusCode.TooManyRequests, reply.StatusCode);
        }
    }

    [Fact]
    public async Task RevocationClosesBothActiveSockets()
    {
        await using var relay = await RunningRelay.StartAsync();
        RelayRoom room = await relay.CreateRoomAsync();
        using var host = new ClientWebSocket();
        using var client = new ClientWebSocket();
        host.Options.SetRequestHeader("Authorization", "Bearer " + room.HostTicket);
        client.Options.SetRequestHeader("Authorization", "Bearer " + room.ClientTicket);
        Uri tunnel = new UriBuilder(new Uri(relay.Address, "v1/tunnel")) { Scheme = "ws" }.Uri;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await host.ConnectAsync(tunnel, timeout.Token);
        await client.ConnectAsync(tunnel, timeout.Token);
        Task hostEnded = ReadUntilClosed(host, timeout.Token);
        Task clientEnded = ReadUntilClosed(client, timeout.Token);
        await RemoteHostChannel.RevokeRoomAsync(relay.Http, relay.Address, relay.OperatorToken, room.Room, timeout.Token);
        await Task.WhenAll(hostEnded, clientEnded);
    }

    [Fact]
    public async Task AClientTicketWithoutATlsHandshakeCannotHoldTheHostIndefinitely()
    {
        await using var relay = await RunningRelay.StartAsync();
        RelayRoom room = await relay.CreateRoomAsync();
        using var identity = DeviceTunnel.CreateIdentity("host");
        using var paired = DeviceTunnel.CreateIdentity("paired");
        var options = new RemoteConnectionOptions { HandshakeTimeout = TimeSpan.FromMilliseconds(300) };
        Task serving = RemoteHostChannel.ServeAsync(relay.Address, room.HostTicket, identity, DeviceTunnel.Fingerprint(paired),
            (_, _) => throw new InvalidOperationException("Unauthenticated requests must not execute."), options, CancellationToken.None);
        using var silent = new ClientWebSocket();
        silent.Options.SetRequestHeader("Authorization", "Bearer " + room.ClientTicket);
        await silent.ConnectAsync(new UriBuilder(new Uri(relay.Address, "v1/tunnel")) { Scheme = "ws" }.Uri, CancellationToken.None);
        Exception? ended = await Record.ExceptionAsync(() => serving.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(ended is OperationCanceledException or IOException, ended?.ToString());
    }

    [Fact]
    public async Task AWebSocketUpgradeThatNeverAnswersHasItsOwnDeadline()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var identity = DeviceTunnel.CreateIdentity("host");
        using var paired = DeviceTunnel.CreateIdentity("paired");
        var options = new RemoteConnectionOptions { ConnectTimeout = TimeSpan.FromMilliseconds(150) };
        Uri address = new("http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/");
        Task serving = RemoteHostChannel.ServeAsync(address, "ticket", identity, DeviceTunnel.Fingerprint(paired),
            (_, _) => throw new InvalidOperationException("The connection never upgraded."), options, CancellationToken.None);
        using TcpClient accepted = await listener.AcceptTcpClientAsync();
        Exception? ended = await Record.ExceptionAsync(() => serving.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(ended is OperationCanceledException or WebSocketException, ended?.ToString());
    }

    [Fact]
    public async Task TheHandshakeDeadlineDoesNotCancelAnAuthenticatedCommand()
    {
        await using var relay = await RunningRelay.StartAsync();
        RelayRoom room = await relay.CreateRoomAsync();
        using var host = DeviceTunnel.CreateIdentity("host");
        using var client = DeviceTunnel.CreateIdentity("client");
        var options = new RemoteConnectionOptions
        {
            HandshakeTimeout = TimeSpan.FromMilliseconds(300), RequestTimeout = TimeSpan.FromSeconds(3)
        };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Task serving = RemoteHostChannel.ServeAsync(relay.Address, room.HostTicket, host, DeviceTunnel.Fingerprint(client),
            async (request, cancellation) =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(600), cancellation);
                return new HostReply { RequestId = request.RequestId, Ok = true, Device = "paired-host" };
            }, options, timeout.Token);
        var request = new HostRequest { RequestId = Guid.NewGuid().ToString("N"), Command = HostCommand.Snapshot };
        HostReply reply = await RemoteHostChannel.ExecuteAsync(relay.Address, room.ClientTicket, client,
            DeviceTunnel.Fingerprint(host), request, options, timeout.Token);
        Assert.True(reply.Ok);
        Exception? ended = await Record.ExceptionAsync(() => serving);
        Assert.True(ended is null or IOException or WebSocketException, ended?.ToString());
    }

    private static async Task ReadUntilClosed(ClientWebSocket socket, CancellationToken cancellation)
    {
        try
        {
            WebSocketReceiveResult frame = await socket.ReceiveAsync(new ArraySegment<byte>(new byte[1]), cancellation);
            Assert.Equal(WebSocketMessageType.Close, frame.MessageType);
        }
        catch (WebSocketException) { }
    }
}

internal sealed class RunningRelay(WebApplication app, HttpClient http, string operatorToken) : IAsyncDisposable
{
    internal HttpClient Http { get; } = http;
    internal Uri Address => Http.BaseAddress!;
    internal string OperatorToken { get; } = operatorToken;

    internal static async Task<RunningRelay> StartAsync(Dictionary<string, string?>? settings = null)
    {
        string token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var configuration = new Dictionary<string, string?>
        {
            ["Relay:OperatorToken"] = token,
            ["Relay:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ["urls"] = "http://127.0.0.1:0"
        };
        if (settings is not null)
            foreach ((string key, string? value) in settings) configuration[key] = value;
        WebApplication application = RelayApp.Build([], builder =>
        {
            builder.Configuration.AddInMemoryCollection(configuration);
            builder.Logging.ClearProviders();
        });
        await application.StartAsync();
        string address = application.Services.GetRequiredService<IServer>().Features
            .Get<IServerAddressesFeature>()!.Addresses.Single();
        return new(application, new HttpClient { BaseAddress = new Uri(address + "/") }, token);
    }

    internal void Authorize() => Http.DefaultRequestHeaders.Authorization = new("Bearer", OperatorToken);

    internal async Task<RelayRoom> CreateRoomAsync()
    {
        Authorize();
        using HttpResponseMessage created = await Http.PostAsync("v1/rooms", null);
        created.EnsureSuccessStatusCode();
        return (await created.Content.ReadFromJsonAsync<RelayRoom>())!;
    }

    public async ValueTask DisposeAsync()
    {
        Http.Dispose();
        await app.StopAsync();
        await app.DisposeAsync();
    }
}
