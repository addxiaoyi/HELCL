using System.IO.Pipes;
using System.Security.Principal;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using HelL.Companion;
using HelL.Contracts;
using HelL.Core;
using HelL.Tests;
using Xunit;

namespace HelL.Portable.Tests;

[SupportedOSPlatform("windows")]
public sealed class HostPipeBoundaryTests
{
    [WindowsPipeTheory]
    [InlineData("\"Role\":\"companion\"")]
    [InlineData("\"Protocol\":1")]
    [InlineData("\"Protocol\":99,\"Protocol\":1,\"Role\":\"companion\"")]
    public async Task MissingOrDuplicateHelloFieldsCannotUseConstructorDefaults(string fields)
    {
        using var fixture = new BackendFixture();
        using var host = HostPipeServer.Start(fixture.Api, "pipe-owner", [HostPermission.Read]);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        try
        {
            using var peer = Client(host.Name);
            await peer.ConnectAsync(deadline.Token);
            string json = "{" + fields + ",\"SessionToken\":" + JsonSerializer.Serialize(host.SessionToken) + "}";
            await HostFrames.WriteAsync(peer, Encoding.UTF8.GetBytes(json), deadline.Token);
            Assert.Null(await HostFrames.ReadAsync(peer, deadline.Token));
        }
        finally { host.Dispose(); await host.Completion.WaitAsync(TimeSpan.FromSeconds(2)); }
    }

    [WindowsPipeTheory]
    [InlineData("\"Command\":2")]
    [InlineData("\"Protocol\":1,\"Command\":1,\"Command\":2")]
    public async Task AmbiguousRequestFieldsCannotCancelTheOwnersTask(string fields)
    {
        using var fixture = new BackendFixture();
        using var finish = new ManualResetEventSlim();
        using var started = new ManualResetEventSlim();
        CancellationToken taskToken = default;
        string task = fixture.Api.StartTask("IPC protocol boundary", context =>
        { taskToken = context.Token; started.Set(); finish.Wait(context.Token); return "complete"; });
        Assert.True(started.Wait(TimeSpan.FromSeconds(2)));
        using var host = HostPipeServer.Start(fixture.Api, "pipe-owner", [HostPermission.Read, HostPermission.CancelTasks]);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        try
        {
            using var peer = Client(host.Name);
            await peer.ConnectAsync(deadline.Token);
            await Hello(peer, host.SessionToken, deadline.Token);
            string json = "{" + fields + ",\"RequestId\":\"" + Guid.NewGuid().ToString("N") + "\",\"TaskId\":\"" + task + "\"}";
            await HostFrames.WriteAsync(peer, Encoding.UTF8.GetBytes(json), deadline.Token);
            HostReply reply = JsonSerializer.Deserialize<HostReply>((await HostFrames.ReadAsync(peer, deadline.Token))!)!;
            Assert.False(taskToken.IsCancellationRequested);
            Assert.False(reply.Ok);
            Assert.Equal("invalid_request", reply.ErrorCode);
            await HostFrames.WriteAsync(peer, JsonSerializer.SerializeToUtf8Bytes(Snapshot()), deadline.Token);
            Assert.True(JsonSerializer.Deserialize<HostReply>((await HostFrames.ReadAsync(peer, deadline.Token))!)!.Ok);
        }
        finally { finish.Set(); host.Dispose(); await host.Completion.WaitAsync(TimeSpan.FromSeconds(2)); }
    }

    [WindowsPipeTheory]
    [InlineData("welcome-protocol")]
    [InlineData("reply-protocol")]
    [InlineData("reply-tasks")]
    [InlineData("reply-duplicate-ok")]
    [InlineData("reply-null-tasks")]
    [InlineData("reply-contradiction")]
    [InlineData("reply-oversized-message")]
    [InlineData("reply-invalid-progress")]
    [InlineData("reply-duplicate-task-field")]
    public async Task ClientRejectsIncompleteOrContradictoryWireReplies(string fault)
    {
        string name = "hell-host-" + Guid.NewGuid().ToString("N");
        using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        HostRequest request = Snapshot();
        Task serving = Task.Run(async () =>
        {
            try
            {
                await pipe.WaitForConnectionAsync(deadline.Token);
                Assert.NotNull(await HostFrames.ReadAsync(pipe, deadline.Token));
                string welcome = fault == "welcome-protocol" ? "{\"Ok\":true,\"Device\":\"pipe-owner\"}"
                    : JsonSerializer.Serialize(new HostPipeWelcome { Ok = true, Device = "pipe-owner" });
                await HostFrames.WriteAsync(pipe, Encoding.UTF8.GetBytes(welcome), deadline.Token);
                if (await HostFrames.ReadAsync(pipe, deadline.Token) is null) return;
                string prefix = "{\"RequestId\":\"" + request.RequestId + "\",\"Device\":\"pipe-owner\",";
                string reply = prefix + (fault switch
                {
                    "reply-protocol" => "\"Ok\":true,\"Tasks\":[]}",
                    "reply-tasks" => "\"Protocol\":1,\"Ok\":true}",
                    "reply-duplicate-ok" => "\"Protocol\":1,\"Ok\":false,\"Ok\":true,\"Tasks\":[]}",
                    "reply-null-tasks" => "\"Protocol\":1,\"Ok\":true,\"Tasks\":null}",
                    "reply-contradiction" => "\"Protocol\":1,\"Ok\":true,\"ErrorCode\":\"denied\",\"Error\":\"denied\",\"Tasks\":[]}",
                    "reply-oversized-message" => "\"Protocol\":1,\"Ok\":true,\"Tasks\":[{\"Id\":\"task\",\"State\":\"failed\",\"Message\":" + JsonSerializer.Serialize(new string('x', 513)) + "}]}",
                    "reply-invalid-progress" => "\"Protocol\":1,\"Ok\":true,\"Tasks\":[{\"Id\":\"task\",\"State\":\"running\",\"ProgressPercent\":-1}]}",
                    "reply-duplicate-task-field" => "\"Protocol\":1,\"Ok\":true,\"Tasks\":[{\"Id\":\"old\",\"Id\":\"new\",\"State\":\"running\"}]}",
                    _ => "\"Protocol\":1,\"Ok\":true,\"Tasks\":[]}"
                });
                await HostFrames.WriteAsync(pipe, Encoding.UTF8.GetBytes(reply), deadline.Token);
            }
            catch (IOException) { }
        });
        await Assert.ThrowsAsync<InvalidDataException>(() => HostPipeClient.ExecuteAsync(name,
            Convert.ToBase64String(new byte[32]), request, deadline.Token));
        await serving.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [WindowsPipeFact]
    public async Task AnUnauthenticatedSlowPeerCannotOutlastTheRealClientsBudget()
    {
        using var fixture = new BackendFixture();
        using var host = HostPipeServer.Start(fixture.Api, "pipe-owner", [HostPermission.Read]);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try
        {
            using var slow = Client(host.Name);
            await slow.ConnectAsync(deadline.Token);
            await slow.WriteAsync(new byte[] { 100 }, deadline.Token);
            HostReply reply = await HostPipeClient.ExecuteAsync(host.Name, host.SessionToken, Snapshot(), deadline.Token);
            Assert.True(reply.Ok);
            Assert.Equal("pipe-owner", reply.Device);
        }
        finally { host.Dispose(); await host.Completion.WaitAsync(TimeSpan.FromSeconds(2)); }
    }

    [WindowsPipeTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposingTheHostClosesAPartialFrameRead(bool authenticated)
    {
        using var fixture = new BackendFixture();
        using var host = HostPipeServer.Start(fixture.Api, "pipe-owner", [HostPermission.Read]);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        using var peer = Client(host.Name);
        await peer.ConnectAsync(deadline.Token);
        if (authenticated) await Hello(peer, host.SessionToken, deadline.Token);
        await peer.WriteAsync(new byte[] { 100 }, deadline.Token);
        host.Dispose();
        await host.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Null(await HostFrames.ReadAsync(peer, deadline.Token));
    }

    [WindowsPipeFact]
    public async Task DisposingTheHostReclaimsABlockedSnapshotWrite()
    {
        using var fixture = new BackendFixture();
        // Fill the bounded snapshot with escaped text so its reply still exceeds the pipe's write buffer.
        for (int index = 0; index < 40; index++)
        {
            string id = "snapshot-" + index;
            fixture.Api.Emit("task_added", PyDict.Of("task_id", id, "title", new string('\u4e2d', 128)));
            fixture.Api.Emit("progress", PyDict.Of("task_id", id, "current", 1L, "total", 2L, "message", new string('\u4e2d', 160)));
        }
        using var host = HostPipeServer.Start(fixture.Api, "pipe-owner", [HostPermission.Read]);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        try
        {
            using var peer = Client(host.Name);
            await peer.ConnectAsync(deadline.Token);
            await Hello(peer, host.SessionToken, deadline.Token);
            await HostFrames.WriteAsync(peer, JsonSerializer.SerializeToUtf8Bytes(Snapshot()), deadline.Token);
            var prefix = new byte[4];
            await peer.ReadExactlyAsync(prefix, deadline.Token);
            Assert.True(BitConverter.ToUInt32(prefix) > 64 * 1024);
            host.Dispose();
            await host.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally { host.Dispose(); await host.Completion.WaitAsync(TimeSpan.FromSeconds(2)); }
    }

    private static HostRequest Snapshot() => new() { RequestId = Guid.NewGuid().ToString("N"), Command = HostCommand.Snapshot };
    private static NamedPipeClientStream Client(string name) => new(".", name, PipeDirection.InOut,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, TokenImpersonationLevel.Identification);
    private static async Task Hello(Stream peer, string token, CancellationToken cancellation)
    {
        await HostFrames.WriteAsync(peer, JsonSerializer.SerializeToUtf8Bytes(new HostPipeHello { SessionToken = token }), cancellation);
        Assert.True(JsonSerializer.Deserialize<HostPipeWelcome>((await HostFrames.ReadAsync(peer, cancellation))!)!.Ok);
    }
    public sealed class WindowsPipeFactAttribute : FactAttribute
    { public WindowsPipeFactAttribute() { if (!OperatingSystem.IsWindows()) Skip = "Protected same-user Windows pipe verification runs on Windows."; } }
    public sealed class WindowsPipeTheoryAttribute : TheoryAttribute
    { public WindowsPipeTheoryAttribute() { if (!OperatingSystem.IsWindows()) Skip = "Protected same-user Windows pipe verification runs on Windows."; } }
}
