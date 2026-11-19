using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using HelL.Contracts;
using HelL.Remote;

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
        HostPipeWelcome welcome = ReadMessage<HostPipeWelcome>(payload, "Protocol", "Ok", "Device");
        if (!welcome.Ok || welcome.Protocol != HostProtocol.Version || string.IsNullOrWhiteSpace(welcome.Device))
            throw new InvalidDataException("The local host handshake is incompatible.");
        await HostFrames.WriteAsync(pipe, JsonSerializer.SerializeToUtf8Bytes(request), timeout.Token).ConfigureAwait(false);
        payload = await HostFrames.ReadAsync(pipe, timeout.Token).ConfigureAwait(false)
            ?? throw new EndOfStreamException("The local host did not return a reply.");
        HostReply reply = HostWireMessages.ReadReply(payload, request);
        if (reply.Device != welcome.Device)
            throw new InvalidDataException("The local host reply does not match the request.");
        return reply;
    }

    private static T ReadMessage<T>(byte[] payload, params string[] required) where T : class
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty field in document.RootElement.EnumerateObject())
                if (!names.Add(field.Name)) throw new JsonException();
            foreach (string field in required)
                if (!names.Contains(field)) throw new JsonException();
            return JsonSerializer.Deserialize<T>(payload) ?? throw new JsonException();
        }
        catch (JsonException) { throw new InvalidDataException("The local host message is malformed or incompatible."); }
    }
}
