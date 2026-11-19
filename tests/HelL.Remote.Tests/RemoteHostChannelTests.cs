using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
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

public sealed class RemoteHostChannelTests
{
    [Theory]
    [InlineData("missing-protocol")]
    [InlineData("duplicate-command")]
    [InlineData("duplicate-id")]
    public async Task AmbiguousWireRequestsCannotReachTheCommandOwner(string fault)
    {
        await using var relay = await RunningRelay.StartAsync();
        RelayRoom room = await relay.CreateRoomAsync();
        using var host = DeviceTunnel.CreateIdentity("host");
        using var client = DeviceTunnel.CreateIdentity("client");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        int executed = 0;
        Task serving = RemoteHostChannel.ServeAsync(relay.Address, room.HostTicket, host, DeviceTunnel.Fingerprint(client),
            (request, _) =>
            {
                executed++;
                return Task.FromResult(new HostReply { RequestId = request.RequestId, Ok = true, Device = "owner" });
            }, deadline.Token);
        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Authorization", "Bearer " + room.ClientTicket);
        await socket.ConnectAsync(new UriBuilder(new Uri(relay.Address, "v1/tunnel")) { Scheme = "ws" }.Uri, deadline.Token);
        await using var tls = await DeviceTunnel.ConnectAsync(new WebSocketStream(socket, true), client, DeviceTunnel.Fingerprint(host), deadline.Token);
        string id = Guid.NewGuid().ToString("N");
        string json = fault switch
        {
            "missing-protocol" => "{\"RequestId\":\"" + id + "\",\"Command\":1}",
            "duplicate-command" => "{\"Protocol\":1,\"RequestId\":\"" + id + "\",\"Command\":1,\"Command\":2,\"TaskId\":\"task\"}",
            _ => "{\"Protocol\":1,\"RequestId\":\"" + Guid.NewGuid().ToString("N") + "\",\"RequestId\":\"" + id + "\",\"Command\":1}"
        };
        await HostFrames.WriteAsync(tls, Encoding.UTF8.GetBytes(json), deadline.Token);
        _ = await Record.ExceptionAsync(async () => { _ = await HostFrames.ReadAsync(tls, deadline.Token); });
        socket.Abort();
        _ = await Record.ExceptionAsync(() => serving.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(0, executed);
    }

    [Theory]
    [InlineData("snapshot-count")]
    [InlineData("negative-progress")]
    [InlineData("oversized-task-message")]
    [InlineData("wrong-task-details")]
    [InlineData("oversized-log")]
    [InlineData("contradictory-game")]
    public async Task ReplyLimitsApplyBeforeRemoteStateCanBeConsumed(string fault)
    {
        await using var relay = await RunningRelay.StartAsync();
        RelayRoom room = await relay.CreateRoomAsync();
        using var host = DeviceTunnel.CreateIdentity("host");
        using var client = DeviceTunnel.CreateIdentity("client");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        HostCommand command = fault.Contains("task-details") || fault == "oversized-log" ? HostCommand.TaskDetails
            : fault == "contradictory-game" ? HostCommand.GameStatus : HostCommand.Snapshot;
        var request = new HostRequest { RequestId = Guid.NewGuid().ToString("N"), Command = command,
            TaskId = command == HostCommand.TaskDetails ? "task-fixture" : "" };
        Task serving = RemoteHostChannel.ServeAsync(relay.Address, room.HostTicket, host, DeviceTunnel.Fingerprint(client),
            (_, _) => Task.FromResult(new HostReply
            {
                RequestId = request.RequestId, Ok = true, Device = "owner",
                Tasks = fault switch
                {
                    "snapshot-count" => Enumerable.Range(0, 41).Select(index => new HostTask { Id = "task-" + index, State = "running" }).ToArray(),
                    "negative-progress" => [new HostTask { Id = "task", State = "running", ProgressPercent = -1 }],
                    "oversized-task-message" => [new HostTask { Id = "task", State = "failed", Message = new string('x', 513) }],
                    _ => []
                },
                Detail = fault == "wrong-task-details" ? new HostTaskDetails { TaskId = "different-task" }
                    : fault == "oversized-log" ? new HostTaskDetails { TaskId = request.TaskId, RecentLogs = [new string('x', 513)] } : null,
                Game = fault == "contradictory-game" ? new HostGameStatus { Running = true, State = "stopped" } : null
            }), deadline.Token);

        await Assert.ThrowsAnyAsync<IOException>(() => RemoteHostChannel.ExecuteAsync(relay.Address, room.ClientTicket, client,
            DeviceTunnel.Fingerprint(host), request, deadline.Token));
        _ = await Record.ExceptionAsync(() => serving.WaitAsync(TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public async Task CommandsRoundTripThroughTheRelayWithExplicitDevicePins()
    {
        string operatorToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        await using WebApplication relay = RelayApp.Build([], builder =>
        {
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Relay:OperatorToken"] = operatorToken,
                ["Relay:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                ["urls"] = "http://127.0.0.1:0"
            });
            builder.Logging.ClearProviders();
        });
        await relay.StartAsync();
        Uri address = new(relay.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single() + "/");
        using var http = new HttpClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        RelayRoom room = await RemoteHostChannel.CreateRoomAsync(http, address, operatorToken, timeout.Token);
        using var host = DeviceTunnel.CreateIdentity("host");
        using var client = DeviceTunnel.CreateIdentity("client");
        Task serving = RemoteHostChannel.ServeAsync(address, room.HostTicket, host, DeviceTunnel.Fingerprint(client),
            (request, _) => Task.FromResult(new HostReply { RequestId = request.RequestId, Ok = true, Device = "paired-desktop",
                Tasks = [new HostTask { Id = "install-1", State = "running", Title = "Install Minecraft" }] }), timeout.Token);
        var request = new HostRequest { RequestId = Guid.NewGuid().ToString("N"), Command = HostCommand.Snapshot };
        HostReply reply = await RemoteHostChannel.ExecuteAsync(address, room.ClientTicket, client,
            DeviceTunnel.Fingerprint(host), request, timeout.Token);
        Assert.True(reply.Ok);
        Assert.Equal("paired-desktop", reply.Device);
        Assert.Equal("install-1", Assert.Single(reply.Tasks).Id);
        Exception? ended = await Record.ExceptionAsync(() => serving);
        Assert.True(ended is null or IOException or System.Net.WebSockets.WebSocketException, ended?.ToString());
        await relay.StopAsync(timeout.Token);
    }

    [Fact]
    public async Task PublicRelayCannotUsePlainHttpOrCredentialsInTheUrl()
    {
        using var http = new HttpClient();
        await Assert.ThrowsAsync<ArgumentException>(() => RemoteHostChannel.CreateRoomAsync(http,
            new Uri("http://relay.example/"), "secret", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => RemoteHostChannel.CreateRoomAsync(http,
            new Uri("https://operator:secret@relay.example/"), "secret", CancellationToken.None));
    }
}
