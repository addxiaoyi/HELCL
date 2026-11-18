using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HelL.Contracts;

// Length-prefixing keeps a malformed peer from forcing unbounded JSON allocations.
public static class HostFrames
{
    public static async Task<byte[]?> ReadAsync(Stream stream, CancellationToken cancellation, int maximumBytes = HostProtocol.MaxMessageBytes)
    {
        if (stream == null) throw new ArgumentNullException(nameof(stream));
        if (maximumBytes < 1 || maximumBytes > HostProtocol.MaxMessageBytes) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        byte[] header = new byte[4];
        int first = await stream.ReadAsync(header, 0, header.Length, cancellation).ConfigureAwait(false);
        if (first == 0) return null;
        await ReadRemaining(stream, header, first, cancellation).ConfigureAwait(false);
        uint size = (uint)(header[0] | header[1] << 8 | header[2] << 16 | header[3] << 24);
        if (size == 0 || size > maximumBytes) throw new InvalidDataException("Host frame size is invalid.");
        byte[] payload = new byte[(int)size];
        await ReadRemaining(stream, payload, 0, cancellation).ConfigureAwait(false);
        return payload;
    }

    public static async Task WriteAsync(Stream stream, byte[] payload, CancellationToken cancellation)
    {
        if (stream == null) throw new ArgumentNullException(nameof(stream));
        if (payload == null) throw new ArgumentNullException(nameof(payload));
        if (payload.Length == 0 || payload.Length > HostProtocol.MaxMessageBytes) throw new InvalidDataException("Host frame size is invalid.");
        int size = payload.Length;
        byte[] header = { (byte)size, (byte)(size >> 8), (byte)(size >> 16), (byte)(size >> 24) };
        await stream.WriteAsync(header, 0, header.Length, cancellation).ConfigureAwait(false);
        await stream.WriteAsync(payload, 0, payload.Length, cancellation).ConfigureAwait(false);
        await stream.FlushAsync(cancellation).ConfigureAwait(false);
    }

    private static async Task ReadRemaining(Stream stream, byte[] bytes, int read, CancellationToken cancellation)
    {
        while (read < bytes.Length)
        {
            int count = await stream.ReadAsync(bytes, read, bytes.Length - read, cancellation).ConfigureAwait(false);
            if (count == 0) throw new EndOfStreamException("The host connection ended inside a frame.");
            read += count;
        }
    }
}

public sealed class HostPipeHello
{
    public int Protocol { get; set; } = HostProtocol.Version;
    public string Role { get; set; } = "companion";
    public string SessionToken { get; set; } = "";
}

public sealed class HostPipeWelcome
{
    public int Protocol { get; set; } = HostProtocol.Version;
    public bool Ok { get; set; }
    public string Device { get; set; } = "";
}
