using System.Net.Http.Json;
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
