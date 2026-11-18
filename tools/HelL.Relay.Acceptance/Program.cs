using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HelL.Contracts;
using HelL.Remote;

try
{
    await Probe.RunAsync(args.Single());
    Console.WriteLine("Relay acceptance phase passed: " + args.Single());
}
catch (Exception failure)
{
    Console.Error.WriteLine("Relay acceptance failed: " + failure.GetType().Name + ": " + failure.Message);
    Environment.ExitCode = 1;
}

internal static class Probe
{
    private static readonly Uri Relay = new("https://relay-proxy/");
    private const string StateFile = "/probe/state.json";
    private const string FeedPlaintext = "encrypted-runner-acceptance-invitation";
    private sealed record Saved(string RevokedRoomTicket, string ActiveRoomTicket, string Channel, string Reader,
        string Key, InvitationFeedWrite Write, string RevokedChannel, string RevokedReader);

    internal static async Task RunAsync(string phase)
    {
        using var http = new HttpClient { BaseAddress = Relay, Timeout = TimeSpan.FromSeconds(30) };
        if (phase == "untrusted")
        {
            try
            {
                using HttpResponseMessage unexpected = await http.GetAsync("health");
                throw new InvalidOperationException("The generated server CA was trusted before explicit installation.");
            }
            catch (HttpRequestException failure) when (failure.InnerException is AuthenticationException) { return; }
        }
        using HttpResponseMessage health = await http.GetAsync("health");
        Require(health.IsSuccessStatusCode, "HTTPS health failed with the explicitly installed CA.");
        string operatorToken = Environment.GetEnvironmentVariable("HELL_ACCEPTANCE_OPERATOR_TOKEN")
            ?? throw new InvalidOperationException("The private test operator credential is missing.");
        if (phase == "seed") await SeedAsync(http, operatorToken);
        else if (phase == "after-restart") await AfterRestartAsync(http, operatorToken);
        else throw new ArgumentException("Unknown acceptance phase.");
    }

    private static async Task SeedAsync(HttpClient http, string operatorToken)
    {
        await RoundTripAsync(http, operatorToken);
        RelayRoom revoked = await RemoteHostChannel.CreateRoomAsync(http, Relay, operatorToken, CancellationToken.None);
        RelayRoom active = await RemoteHostChannel.CreateRoomAsync(http, Relay, operatorToken, CancellationToken.None);
        await RemoteHostChannel.RevokeRoomAsync(http, Relay, operatorToken, revoked.Room, CancellationToken.None);
        await StatusAsync(http, HttpMethod.Get, "v1/tunnel", revoked.ClientTicket, HttpStatusCode.Unauthorized);

        string channel = RandomHex(24);
        string reader = RandomHex(32);
        byte[] key = RandomNumberGenerator.GetBytes(32);
        InvitationFeedWrite first = Encrypt(reader, key, 1);
        InvitationFeedWrite next = Encrypt(reader, key, 7) with { Expires = first.Expires };
        await StatusAsync(http, HttpMethod.Put, Feed(channel), operatorToken, HttpStatusCode.NoContent, first);
        await StatusAsync(http, HttpMethod.Put, Feed(channel), operatorToken, HttpStatusCode.NoContent, next);
        await StatusAsync(http, HttpMethod.Put, Feed(channel), operatorToken, HttpStatusCode.Conflict, first);
        await StatusAsync(http, HttpMethod.Put, Feed(channel), operatorToken, HttpStatusCode.Conflict,
            next with { ReaderHash = Hash(RandomHex(32)), Envelope = next.Envelope with { Revision = 8 } });
        await StatusAsync(http, HttpMethod.Get, Feed(channel), RandomHex(32), HttpStatusCode.Unauthorized);
        await StatusAsync(http, HttpMethod.Get, Feed(channel), operatorToken, HttpStatusCode.Unauthorized);
        await ReadAndDecryptAsync(http, channel, reader, key, next);

        string revokedChannel = RandomHex(24);
        string revokedReader = RandomHex(32);
        await StatusAsync(http, HttpMethod.Put, Feed(revokedChannel), operatorToken, HttpStatusCode.NoContent, Encrypt(revokedReader, key, 1));
        await StatusAsync(http, HttpMethod.Delete, Feed(revokedChannel), operatorToken, HttpStatusCode.NoContent);
        await StatusAsync(http, HttpMethod.Get, Feed(revokedChannel), revokedReader, HttpStatusCode.Unauthorized);
        string persisted = await File.ReadAllTextAsync("/relay-state/rooms.json.invitations");
        foreach (string secret in new[] { operatorToken, reader, revokedReader, Convert.ToBase64String(key), FeedPlaintext })
            Require(!persisted.Contains(secret, StringComparison.Ordinal), "The server persisted a plaintext credential or invitation.");
        Require(persisted.Contains(next.ReaderHash, StringComparison.Ordinal), "The reader hash was not persisted.");
        Directory.CreateDirectory("/probe");
        await File.WriteAllTextAsync(StateFile, JsonSerializer.Serialize(new Saved(revoked.ClientTicket, active.ClientTicket,
            channel, reader, Convert.ToBase64String(key), next, revokedChannel, revokedReader)));
    }

    private static async Task AfterRestartAsync(HttpClient http, string operatorToken)
    {
        Saved saved = JsonSerializer.Deserialize<Saved>(await File.ReadAllTextAsync(StateFile))
            ?? throw new InvalidDataException("The isolated probe state is missing.");
        await StatusAsync(http, HttpMethod.Get, "v1/tunnel", saved.RevokedRoomTicket, HttpStatusCode.Unauthorized);
        await StatusAsync(http, HttpMethod.Get, "v1/tunnel", saved.ActiveRoomTicket, HttpStatusCode.BadRequest);
        await ReadAndDecryptAsync(http, saved.Channel, saved.Reader, Convert.FromBase64String(saved.Key), saved.Write);
        await StatusAsync(http, HttpMethod.Put, Feed(saved.Channel), operatorToken, HttpStatusCode.Conflict, saved.Write);
        await StatusAsync(http, HttpMethod.Get, Feed(saved.RevokedChannel), saved.RevokedReader, HttpStatusCode.Unauthorized);
        await StatusAsync(http, HttpMethod.Put, Feed(saved.RevokedChannel), operatorToken, HttpStatusCode.Conflict, Encrypt(saved.RevokedReader, Convert.FromBase64String(saved.Key), 10));
        for (int attempt = 0; attempt < 11; attempt++)
        {
            using HttpResponseMessage denied = await SendAsync(http, HttpMethod.Post, "v1/rooms", "invalid");
            Require(denied.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.TooManyRequests,
                "Anonymous traffic did not receive an authentication/rate rejection.");
        }
        using HttpResponseMessage limited = await SendAsync(http, HttpMethod.Post, "v1/rooms", "invalid");
        Require(limited.StatusCode == HttpStatusCode.TooManyRequests, "The anonymous budget was not enforced through Nginx.");
        RelayRoom authenticated = await RemoteHostChannel.CreateRoomAsync(http, Relay, operatorToken, CancellationToken.None);
        await RemoteHostChannel.RevokeRoomAsync(http, Relay, operatorToken, authenticated.Room, CancellationToken.None);
        await ReadAndDecryptAsync(http, saved.Channel, saved.Reader, Convert.FromBase64String(saved.Key), saved.Write);
    }

    private static async Task RoundTripAsync(HttpClient http, string operatorToken)
    {
        RelayRoom room = await RemoteHostChannel.CreateRoomAsync(http, Relay, operatorToken, CancellationToken.None);
        using var host = DeviceTunnel.CreateIdentity("acceptance-host");
        using var client = DeviceTunnel.CreateIdentity("acceptance-client");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        Task serving = RemoteHostChannel.ServeAsync(Relay, room.HostTicket, host, DeviceTunnel.Fingerprint(client),
            (request, _) => Task.FromResult(new HostReply { RequestId = request.RequestId, Device = "runner-host", Ok = true }), timeout.Token);
        var request = new HostRequest { RequestId = Guid.NewGuid().ToString("N"), Command = HostCommand.Snapshot };
        HostReply reply = await RemoteHostChannel.ExecuteAsync(Relay, room.ClientTicket, client,
            DeviceTunnel.Fingerprint(host), request, timeout.Token);
        Require(reply.Ok && reply.Device == "runner-host", "The pinned TLS command did not round-trip over WSS.");
        try { await serving.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (Exception failure) when (failure is IOException or WebSocketException or OperationCanceledException) { }
        await RemoteHostChannel.RevokeRoomAsync(http, Relay, operatorToken, room.Room, timeout.Token);
    }

    private static async Task ReadAndDecryptAsync(HttpClient http, string channel, string reader, byte[] key, InvitationFeedWrite expected)
    {
        using HttpResponseMessage read = await SendAsync(http, HttpMethod.Get, Feed(channel), reader);
        Require(read.StatusCode == HttpStatusCode.OK && read.Headers.CacheControl?.ToString() == "no-store", "The reader could not fetch an uncached encrypted feed.");
        InvitationFeedEnvelope envelope = (await read.Content.ReadFromJsonAsync<InvitationFeedEnvelope>())
            ?? throw new InvalidDataException("The encrypted feed is empty.");
        Require(envelope == expected.Envelope, "The feed envelope changed across transport or restart.");
        byte[] cipher = Convert.FromBase64String(envelope.Ciphertext);
        byte[] clear = new byte[cipher.Length];
        using (var aes = new AesGcm(key, 16)) aes.Decrypt(Convert.FromBase64String(envelope.Nonce), cipher, Convert.FromBase64String(envelope.Tag), clear);
        Require(Encoding.UTF8.GetString(clear) == FeedPlaintext, "The feed did not decrypt to its original invitation.");
        using var conditional = new HttpRequestMessage(HttpMethod.Get, Feed(channel));
        conditional.Headers.Authorization = new("Bearer", reader);
        conditional.Headers.IfNoneMatch.ParseAdd("\"" + envelope.Revision + "\"");
        using HttpResponseMessage unchanged = await http.SendAsync(conditional);
        Require(unchanged.StatusCode == HttpStatusCode.NotModified && unchanged.Headers.CacheControl?.ToString() == "no-store", "Conditional feed reads did not return an uncached 304.");
    }

    private static InvitationFeedWrite Encrypt(string reader, byte[] key, long revision)
    {
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] clear = Encoding.UTF8.GetBytes(FeedPlaintext);
        byte[] cipher = new byte[clear.Length];
        byte[] tag = new byte[16];
        using (var aes = new AesGcm(key, 16)) aes.Encrypt(nonce, clear, cipher, tag);
        return new(Hash(reader), DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds(),
            new(revision, Convert.ToBase64String(nonce), Convert.ToBase64String(cipher), Convert.ToBase64String(tag)));
    }

    private static async Task StatusAsync(HttpClient http, HttpMethod method, string path, string token,
        HttpStatusCode expected, object? body = null)
    {
        using HttpResponseMessage response = await SendAsync(http, method, path, token, body);
        Require(response.StatusCode == expected, method + " endpoint returned " + (int)response.StatusCode + " instead of " + (int)expected + ".");
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient http, HttpMethod method, string path, string token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await http.SendAsync(request);
    }

    private static string Feed(string channel) => "v1/invitations/" + channel;
    private static string Hash(string reader) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(reader)));
    private static string RandomHex(int bytes) => Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes)).ToLowerInvariant();
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
