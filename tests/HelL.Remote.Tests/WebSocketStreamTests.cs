using System.Net.WebSockets;
using System.Reflection;
using HelL.Contracts;
using Xunit;

namespace HelL.Remote.Tests;

public sealed class WebSocketStreamTests
{
    [Fact]
    public async Task DisposalCancelsPendingIoAndRejectsFurtherOperationsWithoutOwningTheSocket()
    {
        using var socket = new StubSocket();
        var stream = new WebSocketStream(socket, leaveOpen: true);
        Task<int> reading = stream.ReadAsync(new byte[1], 0, 1, CancellationToken.None);
        await socket.Receiving.Task.WaitAsync(TimeSpan.FromSeconds(2));
        stream.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading);
        Assert.False(stream.CanRead);
        Assert.False(stream.CanWrite);
        Assert.False(socket.Disposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => stream.WriteAsync(new byte[1], 0, 1, CancellationToken.None));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => stream.ReadAsync(Array.Empty<byte>(), 0, 0, CancellationToken.None));
        stream.Dispose();
    }

    [Fact]
    public async Task DisposalCancelsBothActiveAndQueuedWriters()
    {
        using var socket = new StubSocket { BlockSend = true };
        var stream = new WebSocketStream(socket, leaveOpen: true);
        Task sending = stream.WriteAsync(new byte[1], 0, 1, CancellationToken.None);
        await socket.Sending.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Task queued = stream.WriteAsync(new byte[1], 0, 1, CancellationToken.None);
        stream.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sending);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        Assert.Equal(1, socket.SendCalls);
    }

    [Fact]
    public async Task DisposalRejectsAWriterWhoseLockIsGrantedBeforeItsLinkedCancellation()
    {
        using var socket = new StubSocket();
        using var stream = new WebSocketStream(socket, leaveOpen: true);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var sendLock = Assert.IsType<SemaphoreSlim>(typeof(WebSocketStream).GetField("sendLock", flags)!.GetValue(stream));
        var lifetime = Assert.IsType<CancellationTokenSource>(typeof(WebSocketStream).GetField("lifetime", flags)!.GetValue(stream));
        await sendLock.WaitAsync();
        Task queued = stream.WriteAsync(new byte[1], 0, 1, CancellationToken.None);
        bool completedBeforeLinkedCancellation = false;
        // LIFO cancellation grants the lock while the writer's linked token has not yet been notified.
        using CancellationTokenRegistration releaseFirst = lifetime.Token.Register(() =>
        {
            sendLock.Release();
            completedBeforeLinkedCancellation = Task.WhenAny(queued, Task.Delay(TimeSpan.FromSeconds(5))).GetAwaiter().GetResult() == queued;
        });
        stream.Dispose();
        Assert.True(completedBeforeLinkedCancellation, "The controlled writer did not finish before cancellation propagation resumed.");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        Assert.Equal(0, socket.SendCalls);
    }

    [Fact]
    public async Task TunnelRejectsTextAndOversizedWrites()
    {
        using var socket = new StubSocket();
        socket.Incoming.Enqueue(new(1, WebSocketMessageType.Text, true));
        using var stream = new WebSocketStream(socket, leaveOpen: true);
        await Assert.ThrowsAsync<IOException>(() => stream.ReadAsync(new byte[1], 0, 1, CancellationToken.None));
        await Assert.ThrowsAsync<IOException>(() => stream.WriteAsync(new byte[HostProtocol.MaxMessageBytes + 1], 0,
            HostProtocol.MaxMessageBytes + 1, CancellationToken.None));
        Assert.Equal(0, socket.SendCalls);
    }

    [Fact]
    public async Task FragmentLimitsResetOnlyAtTheEndOfEachMessage()
    {
        using var socket = new StubSocket();
        for (int message = 0; message < 2; message++)
            for (int part = 0; part < HostProtocol.MaxMessageBytes / 65536; part++)
                socket.Incoming.Enqueue(new(65536, WebSocketMessageType.Binary,
                    part == HostProtocol.MaxMessageBytes / 65536 - 1));
        for (int part = 0; part <= HostProtocol.MaxMessageBytes / 65536; part++)
            socket.Incoming.Enqueue(new(65536, WebSocketMessageType.Binary, false));
        using var stream = new WebSocketStream(socket, leaveOpen: true);
        byte[] buffer = new byte[65536];
        for (int part = 0; part < HostProtocol.MaxMessageBytes / 65536 * 3; part++)
            Assert.Equal(buffer.Length, await stream.ReadAsync(buffer, 0, buffer.Length, CancellationToken.None));
        await Assert.ThrowsAsync<IOException>(() => stream.ReadAsync(buffer, 0, buffer.Length, CancellationToken.None));
    }
}

internal sealed class StubSocket : WebSocket
{
    private WebSocketState state = WebSocketState.Open;
    internal Queue<WebSocketReceiveResult> Incoming { get; } = new();
    internal TaskCompletionSource<bool> Receiving { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource<bool> Sending { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal bool BlockSend { get; init; }
    internal Action? OnSend { get; init; }
    internal bool Disposed { get; private set; }
    internal int SentBytes { get; private set; }
    internal int SendCalls { get; private set; }
    public override WebSocketCloseStatus? CloseStatus => null;
    public override string? CloseStatusDescription => null;
    public override string? SubProtocol => null;
    public override WebSocketState State => state;
    public override void Abort() => state = WebSocketState.Aborted;
    public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
    { state = WebSocketState.Closed; return Task.CompletedTask; }
    public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        => CloseAsync(closeStatus, statusDescription, cancellationToken);
    public override void Dispose() { Disposed = true; state = WebSocketState.Closed; }
    public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
    {
        Receiving.TrySetResult(true);
        if (Incoming.TryDequeue(out WebSocketReceiveResult? fragment)) return fragment;
        await Task.Delay(Timeout.Infinite, cancellationToken);
        throw new InvalidOperationException("The stub receive must end by cancellation.");
    }
    public override async Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
    {
        SendCalls++;
        SentBytes += buffer.Count;
        Sending.TrySetResult(true);
        OnSend?.Invoke();
        if (BlockSend) await Task.Delay(Timeout.Infinite, cancellationToken);
    }
}
