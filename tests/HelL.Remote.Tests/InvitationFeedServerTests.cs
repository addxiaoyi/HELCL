using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using HelL.Remote;
using Xunit;

namespace HelL.Remote.Tests;

public sealed class InvitationFeedServerTests
{
    [Fact]
    public async Task PublishingRequiresOperatorAndReadingRequiresIndependentReaderCredential()
    {
        await using var relay = await RunningRelay.StartAsync();
        string channel = Channel();
        string reader = Token();
        InvitationFeedWrite publication = Write(reader);
        using HttpResponseMessage anonymous = await relay.Http.PutAsJsonAsync(PathFor(channel), publication);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        relay.Authorize();
        using HttpResponseMessage published = await relay.Http.PutAsJsonAsync(PathFor(channel), publication);
        Assert.Equal(HttpStatusCode.NoContent, published.StatusCode);
        using HttpResponseMessage operatorRead = await relay.Http.GetAsync(PathFor(channel));
        Assert.Equal(HttpStatusCode.Unauthorized, operatorRead.StatusCode);
        relay.Http.DefaultRequestHeaders.Authorization = new("Bearer", reader);
        using HttpResponseMessage read = await relay.Http.GetAsync(PathFor(channel));
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(publication.Envelope, await read.Content.ReadFromJsonAsync<InvitationFeedEnvelope>());
        Assert.Equal("no-store", read.Headers.CacheControl?.ToString());
        Assert.Equal("\"1\"", read.Headers.ETag?.ToString());
        using var conditional = new HttpRequestMessage(HttpMethod.Get, PathFor(channel));
        conditional.Headers.IfNoneMatch.ParseAdd("\"1\"");
        using HttpResponseMessage unchanged = await relay.Http.SendAsync(conditional);
        Assert.Equal(HttpStatusCode.NotModified, unchanged.StatusCode);
        Assert.Equal("no-store", unchanged.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task ReplayedOrCredentialChangingUpdatesAreRejectedAndRevokedChannelsCannotRevive()
    {
        await using var relay = await RunningRelay.StartAsync();
        relay.Authorize();
        string channel = Channel();
        InvitationFeedWrite first = Write(Token());
        using HttpResponseMessage created = await relay.Http.PutAsJsonAsync(PathFor(channel), first);
        Assert.Equal(HttpStatusCode.NoContent, created.StatusCode);
        using HttpResponseMessage replay = await relay.Http.PutAsJsonAsync(PathFor(channel), first);
        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
        InvitationFeedWrite next = first with { Envelope = first.Envelope with { Revision = 7 } };
        using HttpResponseMessage changedReader = await relay.Http.PutAsJsonAsync(PathFor(channel), next with { ReaderHash = Hash(Token()) });
        using HttpResponseMessage changedExpiry = await relay.Http.PutAsJsonAsync(PathFor(channel), next with { Expires = next.Expires + 1 });
        Assert.Equal(HttpStatusCode.Conflict, changedReader.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, changedExpiry.StatusCode);
        using HttpResponseMessage updated = await relay.Http.PutAsJsonAsync(PathFor(channel), next);
        Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);
        using HttpResponseMessage deleted = await relay.Http.DeleteAsync(PathFor(channel));
        using HttpResponseMessage repeated = await relay.Http.DeleteAsync(PathFor(channel));
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, repeated.StatusCode);
        using HttpResponseMessage resurrected = await relay.Http.PutAsJsonAsync(PathFor(channel), next with { Envelope = next.Envelope with { Revision = 8 } });
        Assert.Equal(HttpStatusCode.Conflict, resurrected.StatusCode);
    }

    [Fact]
    public async Task ReaderBudgetsAreIndependentByChannelAndFromAnonymousFailures()
    {
        await using var relay = await RunningRelay.StartAsync(new()
        {
            ["Relay:AnonymousRequestsPerMinute"] = "1", ["Relay:InvitationReadsPerMinute"] = "2"
        });
        relay.Authorize();
        string first = Channel();
        string second = Channel();
        string reader = Token();
        using HttpResponseMessage firstPut = await relay.Http.PutAsJsonAsync(PathFor(first), Write(reader));
        using HttpResponseMessage secondPut = await relay.Http.PutAsJsonAsync(PathFor(second), Write(reader));
        Assert.Equal(HttpStatusCode.NoContent, firstPut.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, secondPut.StatusCode);
        relay.Http.DefaultRequestHeaders.Authorization = new("Bearer", Token());
        using HttpResponseMessage wrong = await relay.Http.GetAsync(PathFor(first));
        using HttpResponseMessage anonymousLimited = await relay.Http.GetAsync(PathFor(first));
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, anonymousLimited.StatusCode);
        relay.Http.DefaultRequestHeaders.Authorization = new("Bearer", reader);
        using HttpResponseMessage read = await relay.Http.GetAsync(PathFor(first));
        using HttpResponseMessage readAgain = await relay.Http.GetAsync(PathFor(first));
        using HttpResponseMessage readerLimited = await relay.Http.GetAsync(PathFor(first));
        using HttpResponseMessage independent = await relay.Http.GetAsync(PathFor(second));
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(HttpStatusCode.OK, readAgain.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, readerLimited.StatusCode);
        Assert.Equal("no-store", readerLimited.Headers.CacheControl?.ToString());
        Assert.Equal(HttpStatusCode.OK, independent.StatusCode);
    }

    [Fact]
    public async Task RevokedCredentialsReturnToTheAnonymousBudget()
    {
        await using var relay = await RunningRelay.StartAsync(new() { ["Relay:AnonymousRequestsPerMinute"] = "1" });
        string channel = Channel();
        string reader = Token();
        relay.Authorize();
        using HttpResponseMessage created = await relay.Http.PutAsJsonAsync(PathFor(channel), Write(reader));
        using HttpResponseMessage deleted = await relay.Http.DeleteAsync(PathFor(channel));
        Assert.Equal(HttpStatusCode.NoContent, created.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        relay.Http.DefaultRequestHeaders.Authorization = new("Bearer", reader);
        using HttpResponseMessage first = await relay.Http.GetAsync(PathFor(channel));
        using HttpResponseMessage second = await relay.Http.GetAsync(PathFor(channel));
        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    }

    [Fact]
    public async Task RevokedUnexpiredChannelsContinueToConsumeBoundedCapacity()
    {
        await using var relay = await RunningRelay.StartAsync(new() { ["Relay:MaxInvitationChannels"] = "1" });
        relay.Authorize();
        string channel = Channel();
        using HttpResponseMessage created = await relay.Http.PutAsJsonAsync(PathFor(channel), Write(Token()));
        using HttpResponseMessage deleted = await relay.Http.DeleteAsync(PathFor(channel));
        using HttpResponseMessage full = await relay.Http.PutAsJsonAsync(PathFor(Channel()), Write(Token()));
        Assert.Equal(HttpStatusCode.NoContent, created.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, full.StatusCode);
    }

    [Theory]
    [InlineData("revision")]
    [InlineData("reader")]
    [InlineData("expired")]
    [InlineData("future")]
    [InlineData("nonce")]
    [InlineData("tag")]
    [InlineData("cipher")]
    [InlineData("empty")]
    [InlineData("envelope")]
    public async Task InvalidEnvelopeFieldsAreRejected(string fault)
    {
        await using var relay = await RunningRelay.StartAsync();
        relay.Authorize();
        InvitationFeedWrite valid = Write(Token());
        InvitationFeedWrite invalid = fault switch
        {
            "revision" => valid with { Envelope = valid.Envelope with { Revision = 0 } },
            "reader" => valid with { ReaderHash = new string('g', 64) },
            "expired" => valid with { Expires = DateTimeOffset.UtcNow.AddSeconds(-1).ToUnixTimeSeconds() },
            "future" => valid with { Expires = DateTimeOffset.UtcNow.AddDays(31).ToUnixTimeSeconds() },
            "nonce" => valid with { Envelope = valid.Envelope with { Nonce = Convert.ToBase64String(new byte[13]) } },
            "tag" => valid with { Envelope = valid.Envelope with { Tag = Convert.ToBase64String(new byte[15]) } },
            "cipher" => valid with { Envelope = valid.Envelope with { Ciphertext = Convert.ToBase64String(new byte[16385]) } },
            "empty" => valid with { Envelope = valid.Envelope with { Ciphertext = "" } },
            "envelope" => valid with { Envelope = null! },
            _ => throw new ArgumentOutOfRangeException(nameof(fault))
        };
        using HttpResponseMessage denied = await relay.Http.PutAsJsonAsync(PathFor(Channel()), invalid);
        Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BodyLimitAppliesBeforeJsonParsingWithOrWithoutContentLength(bool chunked)
    {
        await using var relay = await RunningRelay.StartAsync();
        relay.Authorize();
        byte[] oversized = Enumerable.Repeat((byte)' ', 32769).ToArray();
        using HttpContent body = chunked ? new UnknownLengthContent(oversized) : new ByteArrayContent(oversized);
        using HttpResponseMessage denied = await relay.Http.PutAsync(PathFor(Channel()), body);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, denied.StatusCode);
    }

    [Fact]
    public async Task UnknownPlaintextFieldsAndMalformedChannelsAreRejected()
    {
        await using var relay = await RunningRelay.StartAsync();
        relay.Authorize();
        InvitationFeedWrite write = Write(Token());
        string json = JsonSerializer.Serialize(new { write.ReaderHash, write.Expires, write.Envelope, PlaintextInvite = "must-not-be-accepted" });
        using var body = new StringContent(json, Encoding.UTF8, "application/json");
        using HttpResponseMessage unknown = await relay.Http.PutAsync(PathFor(Channel()), body);
        using HttpResponseMessage invalidChannel = await relay.Http.PutAsJsonAsync(PathFor("not-hex"), write);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidChannel.StatusCode);
    }

    [Fact]
    public async Task RestartRetainsReaderHashRevisionAndRevocationWithoutStoringPlaintextOrSecrets()
    {
        string directory = Path.Combine(Path.GetTempPath(), "hell-invitation-" + Guid.NewGuid().ToString("N"));
        string state = Path.Combine(directory, "rooms.json");
        string reader = Token();
        string revokedReader = Token();
        string channel = Channel();
        string revokedChannel = Channel();
        string plaintext = "private-client-invite-sentinel";
        byte[] key = RandomNumberGenerator.GetBytes(32);
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] clear = Encoding.UTF8.GetBytes(plaintext);
        byte[] cipher = new byte[clear.Length];
        byte[] tag = new byte[16];
        using (var aes = new AesGcm(key, 16)) aes.Encrypt(nonce, clear, cipher, tag);
        InvitationFeedWrite write = Write(reader) with { Envelope = new(9, Convert.ToBase64String(nonce), Convert.ToBase64String(cipher), Convert.ToBase64String(tag)) };
        try
        {
            await using (var first = await RunningRelay.StartAsync(new() { ["Relay:StatePath"] = state }))
            {
                first.Authorize();
                using HttpResponseMessage created = await first.Http.PutAsJsonAsync(PathFor(channel), write);
                using HttpResponseMessage other = await first.Http.PutAsJsonAsync(PathFor(revokedChannel), Write(revokedReader));
                using HttpResponseMessage revoked = await first.Http.DeleteAsync(PathFor(revokedChannel));
                Assert.Equal(HttpStatusCode.NoContent, created.StatusCode);
                Assert.Equal(HttpStatusCode.NoContent, other.StatusCode);
                Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
                string persisted = await File.ReadAllTextAsync(state + ".invitations");
                Assert.DoesNotContain(reader, persisted, StringComparison.Ordinal);
                Assert.DoesNotContain(revokedReader, persisted, StringComparison.Ordinal);
                Assert.DoesNotContain(first.OperatorToken, persisted, StringComparison.Ordinal);
                Assert.DoesNotContain(Convert.ToBase64String(key), persisted, StringComparison.Ordinal);
                Assert.DoesNotContain(plaintext, persisted, StringComparison.Ordinal);
                Assert.Contains(write.ReaderHash, persisted, StringComparison.Ordinal);
                using JsonDocument registry = JsonDocument.Parse(persisted);
                JsonElement saved = registry.RootElement.EnumerateArray().Single(row => row.GetProperty("Channel").GetString() == channel);
                Assert.Equal(write.Envelope.Ciphertext, saved.GetProperty("Envelope").GetProperty("Ciphertext").GetString());
            }
            await using var restarted = await RunningRelay.StartAsync(new() { ["Relay:StatePath"] = state });
            restarted.Http.DefaultRequestHeaders.Authorization = new("Bearer", reader);
            using HttpResponseMessage read = await restarted.Http.GetAsync(PathFor(channel));
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            Assert.Equal(write.Envelope, await read.Content.ReadFromJsonAsync<InvitationFeedEnvelope>());
            restarted.Http.DefaultRequestHeaders.Authorization = new("Bearer", revokedReader);
            using HttpResponseMessage denied = await restarted.Http.GetAsync(PathFor(revokedChannel));
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            restarted.Authorize();
            using HttpResponseMessage replay = await restarted.Http.PutAsJsonAsync(PathFor(channel), write);
            using HttpResponseMessage resurrect = await restarted.Http.PutAsJsonAsync(PathFor(revokedChannel), Write(revokedReader));
            Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, resurrect.StatusCode);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task CorruptPersistentStatePreventsStartup()
    {
        string directory = Path.Combine(Path.GetTempPath(), "hell-invitation-corrupt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string state = Path.Combine(directory, "rooms.json");
        try
        {
            await File.WriteAllTextAsync(state + ".invitations", "{corrupt");
            await Assert.ThrowsAsync<InvalidDataException>(() => RunningRelay.StartAsync(new() { ["Relay:StatePath"] = state }));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task MaximumCipherWithEscapableBase64CharactersSurvivesRestart()
    {
        string directory = Path.Combine(Path.GetTempPath(), "hell-invitation-boundary-" + Guid.NewGuid().ToString("N"));
        string state = Path.Combine(directory, "rooms.json");
        string channel = Channel();
        string reader = Token();
        string cipher = new string('+', 21844) + "+w==";
        Assert.Equal(16384, Convert.FromBase64String(cipher).Length);
        InvitationFeedWrite write = Write(reader) with { Envelope = Write(reader).Envelope with { Ciphertext = cipher } };
        string json = JsonSerializer.Serialize(write, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        Assert.True(Encoding.UTF8.GetByteCount(json) < 32768);
        try
        {
            var settings = new Dictionary<string, string?> { ["Relay:StatePath"] = state, ["Relay:MaxInvitationChannels"] = "1" };
            await using (var first = await RunningRelay.StartAsync(settings))
            {
                first.Authorize();
                using var body = new StringContent(json, Encoding.UTF8, "application/json");
                using HttpResponseMessage stored = await first.Http.PutAsync(PathFor(channel), body);
                Assert.Equal(HttpStatusCode.NoContent, stored.StatusCode);
            }
            await using var restarted = await RunningRelay.StartAsync(settings);
            restarted.Http.DefaultRequestHeaders.Authorization = new("Bearer", reader);
            using HttpResponseMessage read = await restarted.Http.GetAsync(PathFor(channel));
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            Assert.Equal(write.Envelope, await read.Content.ReadFromJsonAsync<InvitationFeedEnvelope>());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    private static string Channel() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
    private static string Token() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static string PathFor(string channel) => "v1/invitations/" + channel;
    private static InvitationFeedWrite Write(string reader) => new(Hash(reader), DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds(),
        new(1, Convert.ToBase64String(RandomNumberGenerator.GetBytes(12)),
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)), Convert.ToBase64String(RandomNumberGenerator.GetBytes(16))));

    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }
}
