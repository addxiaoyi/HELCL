using System.Net.WebSockets;
using HelL.Contracts;

namespace HelL.Remote;

// Presents ordered WebSocket binary fragments to TLS without inventing an encryption protocol.
public sealed class WebSocketStream : Stream
{
    private readonly WebSocket socket;
    private readonly bool leaveOpen;
    private readonly SemaphoreSlim sendLock = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly object gate = new();
    private bool disposed;
    private int activeOperations;
    private int messageBytes;

    public WebSocketStream(WebSocket socket, bool leaveOpen = false)
    {
        this.socket = socket ?? throw new ArgumentNullException(nameof(socket));
        this.leaveOpen = leaveOpen;
    }

    public override bool CanRead { get { lock (gate) return !disposed; } }
    public override bool CanSeek => false;
    public override bool CanWrite => CanRead;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { lock (gate) ObjectDisposedException.ThrowIf(disposed, this); }
    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        Flush();
        return cancellationToken.IsCancellationRequested ? Task.FromCanceled(cancellationToken) : Task.CompletedTask;
    }
    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer, offset, count).GetAwaiter().GetResult();
    public override void Write(byte[] buffer, int offset, int count) => WriteAsync(buffer, offset, count).GetAwaiter().GetResult();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var target = new ArraySegment<byte>(buffer, offset, count);
        CancellationTokenSource operation = BeginOperation(cancellationToken);
        try
        {
            operation.Token.ThrowIfCancellationRequested();
            if (count == 0) return 0;
            while (true)
            {
                WebSocketReceiveResult fragment = await socket.ReceiveAsync(target, operation.Token).ConfigureAwait(false);
                if (fragment.MessageType == WebSocketMessageType.Close) return 0;
                if (fragment.MessageType != WebSocketMessageType.Binary) throw new IOException("The encrypted tunnel only accepts binary frames.");
                messageBytes += fragment.Count;
                if (messageBytes > HostProtocol.MaxMessageBytes) throw new IOException("The encrypted tunnel message size limit was exceeded.");
                if (fragment.EndOfMessage) messageBytes = 0;
                if (fragment.Count > 0) return fragment.Count;
            }
        }
        finally { EndOperation(operation); }
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var source = new ArraySegment<byte>(buffer, offset, count);
        if (count > HostProtocol.MaxMessageBytes) throw new IOException("The encrypted tunnel message size limit was exceeded.");
        CancellationTokenSource operation = BeginOperation(cancellationToken);
        try
        {
            await sendLock.WaitAsync(operation.Token).ConfigureAwait(false);
            try
            {
                Task sending;
                lock (gate)
                {
                    if (disposed) throw new OperationCanceledException(lifetime.Token);
                    operation.Token.ThrowIfCancellationRequested();
                    sending = socket.SendAsync(source, WebSocketMessageType.Binary, true, operation.Token);
                }
                await sending.ConfigureAwait(false);
            }
            finally { sendLock.Release(); }
        }
        finally { EndOperation(operation); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                activeOperations++;
            }
            try
            {
                lifetime.Cancel();
                if (!leaveOpen) socket.Dispose();
            }
            finally { EndOperation(null); }
        }
        base.Dispose(disposing);
    }

    private CancellationTokenSource BeginOperation(CancellationToken cancellation)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.Token);
            activeOperations++;
            return operation;
        }
    }

    private void EndOperation(CancellationTokenSource? operation)
    {
        operation?.Dispose();
        lock (gate)
        {
            if (--activeOperations != 0 || !disposed) return;
            lifetime.Dispose();
            sendLock.Dispose();
        }
    }
}
