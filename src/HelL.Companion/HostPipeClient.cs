using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using HelL.Contracts;

namespace HelL.Companion;

public static class HostPipeClient
{
    public static async Task<HostReply> ExecuteAsync(string pipeName, string sessionToken,
        HostRequest request, CancellationToken cancellation)
    {
        if (string.IsNullOrWhiteSpace(pipeName) || pipeName.Length > 128 || !pipeName.StartsWith("hell-host-", StringComparison.Ordinal) ||
            pipeName.Any(ch => !char.IsLetterOrDigit(ch) && ch != '-'))
            throw new ArgumentException("A local HelL pipe name is required.", nameof(pipeName));
        if (string.IsNullOrWhiteSpace(sessionToken) || sessionToken.Length > 128)
            throw new ArgumentException("A host session token is required.", nameof(sessionToken));
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The WPF companion pipe requires Windows.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, TokenImpersonationLevel.Identification);
        await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
        await HostFrames.WriteAsync(pipe, JsonSerializer.SerializeToUtf8Bytes(new HostPipeHello { SessionToken = sessionToken }), timeout.Token).ConfigureAwait(false);
        byte[] payload = await HostFrames.ReadAsync(pipe, timeout.Token).ConfigureAwait(false)
            ?? throw new IOException("The local host rejected the companion connection.");
        HostPipeWelcome welcome = JsonSerializer.Deserialize<HostPipeWelcome>(payload)
            ?? throw new InvalidDataException("The local host handshake is invalid.");
        if (!welcome.Ok || welcome.Protocol != HostProtocol.Version || string.IsNullOrWhiteSpace(welcome.Device))
            throw new InvalidDataException("The local host handshake is incompatible.");
        await HostFrames.WriteAsync(pipe, JsonSerializer.SerializeToUtf8Bytes(request), timeout.Token).ConfigureAwait(false);
        payload = await HostFrames.ReadAsync(pipe, timeout.Token).ConfigureAwait(false)
            ?? throw new EndOfStreamException("The local host did not return a reply.");
        HostReply reply = JsonSerializer.Deserialize<HostReply>(payload)
            ?? throw new InvalidDataException("The local host reply is invalid.");
        if (reply.Protocol != HostProtocol.Version || reply.RequestId != request.RequestId || reply.Device != welcome.Device)
            throw new InvalidDataException("The local host reply does not match the request.");
        return reply;
    }
}
