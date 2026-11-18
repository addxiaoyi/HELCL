using HelL.Contracts;
using Xunit;

namespace HelL.Remote.Tests;

public sealed class HostFrameTests
{
    [Fact]
    public async Task FramesRoundTripAndKeepMessageBoundaries()
    {
        using var stream = new MemoryStream();
        await HostFrames.WriteAsync(stream, "first"u8.ToArray(), CancellationToken.None);
        await HostFrames.WriteAsync(stream, "second"u8.ToArray(), CancellationToken.None);
        stream.Position = 0;
        Assert.Equal("first"u8.ToArray(), await HostFrames.ReadAsync(stream, CancellationToken.None));
        Assert.Equal("second"u8.ToArray(), await HostFrames.ReadAsync(stream, CancellationToken.None));
        Assert.Null(await HostFrames.ReadAsync(stream, CancellationToken.None));
    }

    [Theory]
    [InlineData(new byte[] { 0, 0, 0, 0 })]
    [InlineData(new byte[] { 255, 255, 255, 255 })]
    [InlineData(new byte[] { 1, 0, 16, 0 })]
    public async Task FrameHeadersRejectEmptyOrOversizedPayloads(byte[] header)
    {
        using var stream = new MemoryStream(header);
        await Assert.ThrowsAsync<InvalidDataException>(() => HostFrames.ReadAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task TruncatedAndOversizedHandshakeFramesAreRejected()
    {
        using var truncated = new MemoryStream(new byte[] { 5, 0, 0, 0, 1 });
        await Assert.ThrowsAsync<EndOfStreamException>(() => HostFrames.ReadAsync(truncated, CancellationToken.None));
        using var largeHello = new MemoryStream(new byte[] { 1, 16, 0, 0 });
        await Assert.ThrowsAsync<InvalidDataException>(() => HostFrames.ReadAsync(largeHello, CancellationToken.None, 4096));
    }
}
