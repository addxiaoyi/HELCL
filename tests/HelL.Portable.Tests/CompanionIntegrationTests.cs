using System.Security.Cryptography;
using HelL.Companion;
using HelL.Contracts;
using HelL.Core;
using HelL.Relay;
using HelL.Remote;
using HelL.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HelL.Portable.Tests;

public sealed class CompanionIntegrationTests
{
    [Fact]
    public async Task PinnedRemoteCommandsReachTheExistingLauncherOwnerThroughItsProtectedPipe()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new BackendFixture();
        using var pipe = HostPipeServer.Start(fixture.Api, "existing-desktop-owner", [HostPermission.Read, HostPermission.CancelTasks]);
        string root = Path.Combine(Path.GetTempPath(), "hell-companion-integration-" + Guid.NewGuid().ToString("N"));
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
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var http = new HttpClient();
        string task = fixture.Api.StartTask("task owned by the launcher", context =>
        {
            if (!context.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(25))) throw new TimeoutException("The paired cancellation was not delivered.");
            context.Token.ThrowIfCancellationRequested();
            return "unexpected completion";
        });
        try
        {
            var desktop = new RemotePairingStore(Path.Combine(root, "desktop"), "desktop", new Secrets());
            var phone = new RemotePairingStore(Path.Combine(root, "phone"), "phone", new Secrets());
            desktop.Trust(phone.Offer, [HostPermission.Read]);
            phone.Trust(desktop.Offer, [HostPermission.Read]);
            await using PairedRemoteHost host = CompanionRelayHost.Connect(desktop, http, address,
                operatorToken, pipe.Name, pipe.SessionToken);
            var client = new PairedRemoteClient(phone);
            RemoteHostInvite readOnly = await host.StartAsync(phone.Offer.DeviceId, (_, _) => Task.CompletedTask, timeout.Token);
            HostReply snapshot = await client.SnapshotAsync(readOnly, timeout.Token);
            Assert.True(snapshot.Ok);
            Assert.Equal("existing-desktop-owner", snapshot.Device);
            Assert.Contains(snapshot.Tasks, row => row.Id == task);
            var cancel = new HostRequest { RequestId = Guid.NewGuid().ToString("N"), Command = HostCommand.CancelTask, TaskId = task };
            Assert.Equal("permission_denied", (await client.ExecuteAsync(readOnly, cancel, timeout.Token)).ErrorCode);
            await host.StopAsync(phone.Offer.DeviceId, timeout.Token);
            desktop.Trust(phone.Offer, [HostPermission.Read, HostPermission.CancelTasks]);
            RemoteHostInvite allowed = await host.StartAsync(phone.Offer.DeviceId, (_, _) => Task.CompletedTask, timeout.Token);
            Assert.True((await client.ExecuteAsync(allowed, cancel, timeout.Token)).Ok);
            Assert.False(Py.Truthy(fixture.Api.wait_task(task, 5L)["ok"]));
            await host.RevokeAsync(phone.Offer.DeviceId, timeout.Token);
            Assert.Null(desktop.Find(phone.Offer.DeviceId));
        }
        finally
        {
            fixture.Api.cancel_task(task);
            fixture.Api.wait_task(task, 5L);
            await relay.StopAsync();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private sealed class Secrets : IRemoteSecretStore
    {
        private readonly Dictionary<string, string> keys = [];
        public string Read(string key) => keys.GetValueOrDefault(key, "");
        public void Write(string key, string value) => keys[key] = value;
        public void Delete(string key) => keys.Remove(key);
    }
}
