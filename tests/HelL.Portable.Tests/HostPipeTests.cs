using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using HelL.Companion;
using HelL.Contracts;
using HelL.Core;
using HelL.Portable;
using HelL.Tests;
using Xunit;

namespace HelL.Portable.Tests;

public sealed class HostPipeTests
{
    [Fact]
    public async Task ProtectedPipeDispatchesToItsOwnerAndRejectsAnotherSession()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new BackendFixture();
        using var server = HostPipeServer.Start(fixture.Api, "pipe-owner", new[] { HostPermission.Read });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var request = new HostRequest { RequestId = Guid.NewGuid().ToString("N"), Command = HostCommand.Snapshot };
        HostReply reply = await HostPipeClient.ExecuteAsync(server.Name, server.SessionToken, request, timeout.Token);
        Assert.True(reply.Ok);
        Assert.Equal("pipe-owner", reply.Device);
        string pipeName = server.Name;
        Exception? rejected = await Record.ExceptionAsync(() => HostPipeClient.ExecuteAsync(pipeName,
            Convert.ToBase64String(new byte[32]), request, timeout.Token));
        Assert.True(rejected is IOException, rejected?.ToString());
        request.Command = HostCommand.CancelTask; request.TaskId = "not-running";
        Assert.Equal("permission_denied", (await HostPipeClient.ExecuteAsync(server.Name, server.SessionToken, request, timeout.Token)).ErrorCode);
        server.Dispose();
        await server.Completion.WaitAsync(timeout.Token);
    }

    [Fact]
    public async Task PipeRejectsAnotherRoleAndReturnsStructuredMalformedJsonErrors()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new BackendFixture();
        using var server = HostPipeServer.Start(fixture.Api, "pipe-owner", new[] { HostPermission.Read });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using (NamedPipeClientStream stranger = LocalPipe(server.Name))
        {
            await stranger.ConnectAsync(timeout.Token);
            await HostFrames.WriteAsync(stranger, JsonSerializer.SerializeToUtf8Bytes(new HostPipeHello
                { Role = "arbitrary-executor", SessionToken = server.SessionToken }), timeout.Token);
            Assert.Null(await HostFrames.ReadAsync(stranger, timeout.Token));
        }
        using (NamedPipeClientStream client = LocalPipe(server.Name))
        {
            await client.ConnectAsync(timeout.Token);
            await HostFrames.WriteAsync(client, JsonSerializer.SerializeToUtf8Bytes(new HostPipeHello { SessionToken = server.SessionToken }), timeout.Token);
            HostPipeWelcome welcome = JsonSerializer.Deserialize<HostPipeWelcome>((await HostFrames.ReadAsync(client, timeout.Token))!)!;
            Assert.True(welcome.Ok);
            await HostFrames.WriteAsync(client, Encoding.UTF8.GetBytes("{not JSON"), timeout.Token);
            HostReply reply = JsonSerializer.Deserialize<HostReply>((await HostFrames.ReadAsync(client, timeout.Token))!)!;
            Assert.False(reply.Ok); Assert.Equal("invalid_request", reply.ErrorCode);
        }
        server.Dispose();
        await server.Completion.WaitAsync(timeout.Token);
    }

    [Fact]
    public void RootLeaseAndPortableHomeCannotOwnTheSameDirectory()
    {
        string root = Path.Combine(Path.GetTempPath(), "hell-lease-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (RootLease owner = RootLease.Acquire(root)) Assert.Throws<IOException>(() => PortableHome.Open(root));
            using (PortableHome portable = PortableHome.Open(root)) Assert.Throws<IOException>(() => RootLease.Acquire(root));
            Assert.True(File.Exists(Path.Combine(root, ".launcher.lock")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task AnonymousPipeIdentityCannotUseAKnownSessionToken()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new BackendFixture();
        using var server = HostPipeServer.Start(fixture.Api, "pipe-owner", new[] { HostPermission.Read });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        string sessionToken = server.SessionToken;
        using (var anonymous = new NamedPipeClientStream(".", server.Name, PipeDirection.InOut,
            PipeOptions.Asynchronous, TokenImpersonationLevel.Anonymous))
        {
            Exception? denied = await Record.ExceptionAsync(async () =>
            {
                await anonymous.ConnectAsync(timeout.Token);
                await HostFrames.WriteAsync(anonymous, JsonSerializer.SerializeToUtf8Bytes(new HostPipeHello { SessionToken = sessionToken }), timeout.Token);
                Assert.Null(await HostFrames.ReadAsync(anonymous, timeout.Token));
            });
            Assert.True(denied is null or IOException, denied?.ToString());
        }
        var request = new HostRequest { RequestId = Guid.NewGuid().ToString("N"), Command = HostCommand.Snapshot };
        Assert.True((await HostPipeClient.ExecuteAsync(server.Name, server.SessionToken, request, timeout.Token)).Ok);
        server.Dispose(); await server.Completion.WaitAsync(timeout.Token);
    }

    private static NamedPipeClientStream LocalPipe(string name) => new(".", name, PipeDirection.InOut,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, TokenImpersonationLevel.Identification);
}
