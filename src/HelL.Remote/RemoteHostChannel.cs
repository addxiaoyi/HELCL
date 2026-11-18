using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Security;
using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using HelL.Contracts;

namespace HelL.Remote;

public static class RemoteHostChannel
{
    public static async Task RevokeRoomAsync(HttpClient http, Uri relay, string operatorToken, string room,
        CancellationToken cancellation)
    {
        using HttpRequestMessage request = RoomRequest(HttpMethod.Delete, relay, operatorToken, room);
        using HttpResponseMessage response = await http.SendAsync(request, cancellation).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public static async Task<RelayRoom> RenewRoomAsync(HttpClient http, Uri relay, string operatorToken, string room,
        CancellationToken cancellation)
    {
        using HttpRequestMessage request = RoomRequest(HttpMethod.Post, relay, operatorToken, room, "/renew");
        using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return ValidateRoom(await response.Content.ReadFromJsonAsync<RelayRoom>(cancellation).ConfigureAwait(false));
    }

    public static async Task<RelayRoom> CreateRoomAsync(HttpClient http, Uri relay, string operatorToken,
        CancellationToken cancellation)
    {
        ValidateRelay(relay);
        if (string.IsNullOrWhiteSpace(operatorToken)) throw new ArgumentException("A relay operator token is required.", nameof(operatorToken));
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(relay, "v1/rooms"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", operatorToken);
        using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return ValidateRoom(await response.Content.ReadFromJsonAsync<RelayRoom>(cancellation).ConfigureAwait(false));
    }

    private static RelayRoom ValidateRoom(RelayRoom? room)
    {
        if (room is null) throw new InvalidDataException("The relay returned an empty room.");
        if (room.Room is null || room.Room.Length != 48 || room.HostTicket is null || room.ClientTicket is null ||
            room.HostTicket.Length is < 1 or > 1024 || room.ClientTicket.Length is < 1 or > 1024 ||
            room.Expires <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()) throw new InvalidDataException("The relay returned an invalid room.");
        return room;
    }

    public static async Task ServeAsync(Uri relay, string hostTicket, X509Certificate2 hostIdentity,
        string pairedClientFingerprint, Func<HostRequest, CancellationToken, Task<HostReply>> execute,
        CancellationToken cancellation)
        => await ServeAsync(relay, hostTicket, hostIdentity, pairedClientFingerprint, execute,
            new RemoteConnectionOptions(), cancellation).ConfigureAwait(false);

    public static async Task ServeAsync(Uri relay, string hostTicket, X509Certificate2 hostIdentity,
        string pairedClientFingerprint, Func<HostRequest, CancellationToken, Task<HostReply>> execute,
        RemoteConnectionOptions options, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(execute);
        options.Validate();
        using ClientWebSocket socket = await ConnectSocketAsync(relay, hostTicket, options.ConnectTimeout, cancellation).ConfigureAwait(false);
        using var handshake = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        handshake.CancelAfter(options.HandshakeTimeout);
        await using SslStream tls = await DeviceTunnel.AcceptAsync(new WebSocketStream(socket, true), hostIdentity,
            pairedClientFingerprint, handshake.Token).ConfigureAwait(false);
        handshake.CancelAfter(Timeout.InfiniteTimeSpan);
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                using var requestTime = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                requestTime.CancelAfter(options.RequestTimeout);
                byte[]? payload = await HostFrames.ReadAsync(tls, requestTime.Token).ConfigureAwait(false);
                if (payload is null) return;
                HostRequest request = JsonSerializer.Deserialize<HostRequest>(payload)
                    ?? throw new InvalidDataException("The remote host request is empty.");
                request.Validate();
                HostReply reply = await execute(request, requestTime.Token).ConfigureAwait(false);
                if (reply.Protocol != HostProtocol.Version || reply.RequestId != request.RequestId || string.IsNullOrWhiteSpace(reply.Device))
                    throw new InvalidDataException("The host handler returned an incompatible reply.");
                await HostFrames.WriteAsync(tls, JsonSerializer.SerializeToUtf8Bytes(reply), requestTime.Token).ConfigureAwait(false);
            }
        }
        finally { socket.Abort(); }
    }

    public static async Task<HostReply> ExecuteAsync(Uri relay, string clientTicket, X509Certificate2 clientIdentity,
        string pairedHostFingerprint, HostRequest request, CancellationToken cancellation)
        => await ExecuteAsync(relay, clientTicket, clientIdentity, pairedHostFingerprint, request,
            new RemoteConnectionOptions(), cancellation).ConfigureAwait(false);

    public static async Task<HostReply> ExecuteAsync(Uri relay, string clientTicket, X509Certificate2 clientIdentity,
        string pairedHostFingerprint, HostRequest request, RemoteConnectionOptions options, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(request);
        options.Validate();
        request.Validate();
        using var requestTime = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        requestTime.CancelAfter(options.RequestTimeout);
        using ClientWebSocket socket = await ConnectSocketAsync(relay, clientTicket, options.ConnectTimeout, requestTime.Token).ConfigureAwait(false);
        using var handshake = CancellationTokenSource.CreateLinkedTokenSource(requestTime.Token);
        handshake.CancelAfter(options.HandshakeTimeout);
        await using SslStream tls = await DeviceTunnel.ConnectAsync(new WebSocketStream(socket, true), clientIdentity,
            pairedHostFingerprint, handshake.Token).ConfigureAwait(false);
        handshake.CancelAfter(Timeout.InfiniteTimeSpan);
        try
        {
            await HostFrames.WriteAsync(tls, JsonSerializer.SerializeToUtf8Bytes(request), requestTime.Token).ConfigureAwait(false);
            byte[] payload = await HostFrames.ReadAsync(tls, requestTime.Token).ConfigureAwait(false)
                ?? throw new EndOfStreamException("The remote host ended before returning its state.");
            HostReply reply = JsonSerializer.Deserialize<HostReply>(payload)
                ?? throw new InvalidDataException("The remote host reply is empty.");
            if (reply.Protocol != HostProtocol.Version || reply.RequestId != request.RequestId || string.IsNullOrWhiteSpace(reply.Device))
                throw new InvalidDataException("The remote host reply does not match this request.");
            return reply;
        }
        finally { socket.Abort(); }
    }

    private static async Task<ClientWebSocket> ConnectSocketAsync(Uri relay, string ticket, TimeSpan timeout, CancellationToken cancellation)
    {
        ValidateRelay(relay);
        if (string.IsNullOrWhiteSpace(ticket) || ticket.Length > 1024) throw new ArgumentException("A relay room ticket is required.", nameof(ticket));
        var endpoint = new UriBuilder(new Uri(relay, "v1/tunnel")) { Scheme = relay.Scheme == "https" ? "wss" : "ws" };
        var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Authorization", "Bearer " + ticket);
        using var connecting = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        connecting.CancelAfter(timeout);
        try { await socket.ConnectAsync(endpoint.Uri, connecting.Token).ConfigureAwait(false); return socket; }
        catch { socket.Dispose(); throw; }
    }

    private static HttpRequestMessage RoomRequest(HttpMethod method, Uri relay, string operatorToken, string room, string suffix = "")
    {
        ValidateRelay(relay);
        if (string.IsNullOrWhiteSpace(operatorToken)) throw new ArgumentException("A relay operator token is required.", nameof(operatorToken));
        if (room is null || room.Length != 48 || room.Any(ch => !Uri.IsHexDigit(ch)))
            throw new ArgumentException("A relay room ID is required.", nameof(room));
        var request = new HttpRequestMessage(method, new Uri(relay, "v1/rooms/" + room + suffix));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", operatorToken);
        return request;
    }

    private static void ValidateRelay(Uri relay)
    {
        ArgumentNullException.ThrowIfNull(relay);
        if (!relay.IsAbsoluteUri || !string.IsNullOrEmpty(relay.UserInfo) || !string.IsNullOrEmpty(relay.Query) ||
            !string.IsNullOrEmpty(relay.Fragment) || (relay.Scheme != "https" && !(relay.Scheme == "http" && relay.IsLoopback)) ||
            !relay.AbsolutePath.EndsWith('/'))
            throw new ArgumentException("Use an HTTPS relay base URL ending in '/'. HTTP is allowed only for loopback tests.", nameof(relay));
    }
}
