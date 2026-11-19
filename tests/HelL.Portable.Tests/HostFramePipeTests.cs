using System.IO.Pipes;
using HelL.Contracts;
using Xunit;

namespace HelL.Portable.Tests;

public sealed class HostFramePipeTests
{
    [Fact]
    public async Task CancellationReleasesAFrameReadFromASlowPeer()
    {
        await using var pair = await Pipes.Connect();
        await pair.Client.WriteAsync(new byte[] { 20, 0, 0, 0, 1 });
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => HostFrames.ReadAsync(pair.Server, stop.Token))
            .WaitAsync(TimeSpan.FromSeconds(2));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosingAPeerReleasesAPartialFrame(bool payload)
    {
        await using var pair = await Pipes.Connect();
        await pair.Client.WriteAsync(payload ? new byte[] { 20, 0, 0, 0, 1 } : new byte[] { 20, 0 });
        pair.Client.Dispose();
        await Assert.ThrowsAsync<EndOfStreamException>(() => HostFrames.ReadAsync(pair.Server, CancellationToken.None))
            .WaitAsync(TimeSpan.FromSeconds(2));
    }
    [Fact]
    public async Task CancellationReleasesAFrameWriteWhenThePeerStopsReading()
    {
        await using var pair = await Pipes.Connect();
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => HostFrames.WriteAsync(pair.Server,
            new byte[HostProtocol.MaxMessageBytes], stop.Token)).WaitAsync(TimeSpan.FromSeconds(2));
    }
    private sealed class Pipes : IAsyncDisposable
    {
        public NamedPipeServerStream Server { get; }
        public NamedPipeClientStream Client { get; }
        private Pipes(string name)
        {
            Server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 128, 128);
            Client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        }
        public static async Task<Pipes> Connect()
        {
            var pair = new Pipes("hell-frame-test-" + Guid.NewGuid().ToString("N"));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                Task accept = pair.Server.WaitForConnectionAsync(deadline.Token);
                await pair.Client.ConnectAsync(deadline.Token);
                await accept;
                return pair;
            }
            catch { await pair.DisposeAsync(); throw; }
        }
        public ValueTask DisposeAsync() { Client.Dispose(); Server.Dispose(); return ValueTask.CompletedTask; }
    }
}
