using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using HelL.Remote;

namespace HelL.Relay;

internal sealed class InvitationFeedStore
{
    internal const int MaxPayloadBytes = 32 * 1024;
    private const int MaxCipherBytes = 16 * 1024;
    // Base64 '+' must not inflate into six-byte HTML escapes in the bounded registry.
    private static readonly JsonSerializerOptions StoredJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private readonly object gate = new();
    private readonly Dictionary<string, SavedFeed> feeds = new(StringComparer.Ordinal);
    private readonly int capacity;
    private readonly string? statePath;

    private sealed record SavedFeed(string Channel, string ReaderHash, long Expires,
        InvitationFeedEnvelope Envelope, bool Revoked);

    internal InvitationFeedStore(RelayLimits limits)
    {
        capacity = limits.MaxInvitationChannels;
        statePath = string.IsNullOrWhiteSpace(limits.StatePath) ? null : Path.GetFullPath(limits.StatePath + ".invitations");
        Load();
        Save();
    }

    internal static bool ValidChannel(string? channel) => channel is { Length: 48 } && channel.All(Uri.IsHexDigit);
    internal static string CanonicalChannel(string channel) => channel.ToLowerInvariant();

    internal InvitationFeedEnvelope? Read(string channel, string readerToken)
    {
        if (!ValidChannel(channel) || readerToken.Length != 64 || !readerToken.All(Uri.IsHexDigit)) return null;
        byte[] supplied = SHA256.HashData(Encoding.UTF8.GetBytes(readerToken));
        lock (gate)
        {
            if (!feeds.TryGetValue(CanonicalChannel(channel), out SavedFeed? feed) || feed.Revoked ||
                feed.Expires <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return null;
            return CryptographicOperations.FixedTimeEquals(supplied, Convert.FromHexString(feed.ReaderHash)) ? feed.Envelope : null;
        }
    }

    internal void Publish(string channel, InvitationFeedWrite write)
    {
        Validate(write);
        channel = CanonicalChannel(channel);
        lock (gate)
        {
            PurgeExpired();
            feeds.TryGetValue(channel, out SavedFeed? previous);
            if (previous is not null && (previous.Revoked || write.Expires != previous.Expires ||
                write.Envelope.Revision <= previous.Envelope.Revision || !CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(write.ReaderHash), Convert.FromHexString(previous.ReaderHash))))
                throw new RelayAdmissionException(409, "This invitation update conflicts with the channel state.");
            if (previous is null && feeds.Count >= capacity)
                throw new RelayAdmissionException(503, "Invitation channel capacity reached.");
            feeds[channel] = new(channel, Convert.ToHexString(Convert.FromHexString(write.ReaderHash)), write.Expires, write.Envelope, false);
            try { Save(); }
            catch
            {
                if (previous is null) feeds.Remove(channel); else feeds[channel] = previous;
                throw;
            }
        }
    }

    internal bool Revoke(string channel)
    {
        channel = CanonicalChannel(channel);
        lock (gate)
        {
            PurgeExpired();
            if (!feeds.TryGetValue(channel, out SavedFeed? previous)) return false;
            if (previous.Revoked) return true;
            feeds[channel] = previous with { Revoked = true };
            try { Save(); }
            catch { feeds[channel] = previous; throw; }
            return true;
        }
    }

    private static void Validate(InvitationFeedWrite? write, bool allowExpired = false)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (write is null || write.ReaderHash is not { Length: 64 } || !write.ReaderHash.All(Uri.IsHexDigit) ||
            write.Expires <= (allowExpired ? 0 : now) || write.Expires > DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds() ||
            write.Envelope is null || write.Envelope.Revision < 1 || !EncodedLength(write.Envelope.Nonce, 12, 12) ||
            !EncodedLength(write.Envelope.Tag, 16, 16) || !EncodedLength(write.Envelope.Ciphertext, 1, MaxCipherBytes))
            throw new RelayAdmissionException(400, "The invitation envelope is invalid.");
    }

    private static bool EncodedLength(string? encoded, int minimum, int maximum)
    {
        if (encoded is null || encoded.Length > (maximum + 2) / 3 * 4) return false;
        Span<byte> decoded = stackalloc byte[maximum];
        return Convert.TryFromBase64String(encoded, decoded, out int count) && count >= minimum && count <= maximum;
    }

    private void PurgeExpired()
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (string channel in feeds.Where(row => row.Value.Expires <= now).Select(row => row.Key).ToArray()) feeds.Remove(channel);
    }

    private void Load()
    {
        if (statePath is null || !File.Exists(statePath)) return;
        if (new FileInfo(statePath).Length > (long)capacity * (MaxPayloadBytes + 1024))
            throw new InvalidDataException("The invitation registry exceeds its bounded capacity.");
        SavedFeed[] saved;
        try
        {
            saved = JsonSerializer.Deserialize<SavedFeed[]>(File.ReadAllBytes(statePath))
                ?? throw new InvalidDataException("The invitation registry is invalid.");
        }
        catch (JsonException ex) { throw new InvalidDataException("The invitation registry is invalid.", ex); }
        if (saved.Length > capacity) throw new InvalidDataException("The invitation registry exceeds its bounded capacity.");
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (SavedFeed feed in saved)
        {
            if (feed is null || !ValidChannel(feed.Channel)) throw new InvalidDataException("The invitation registry contains an invalid channel.");
            try { Validate(new(feed.ReaderHash, feed.Expires, feed.Envelope), allowExpired: true); }
            catch (RelayAdmissionException ex) { throw new InvalidDataException("The invitation registry contains an invalid envelope.", ex); }
            if (feed.Expires <= now) continue;
            if (!feeds.TryAdd(CanonicalChannel(feed.Channel), feed)) throw new InvalidDataException("The invitation registry contains duplicate channels.");
        }
    }

    private void Save()
    {
        if (statePath is null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
        string temporary = statePath + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(file, feeds.Values.ToArray(), StoredJson);
                file.Flush(flushToDisk: true);
            }
            File.Move(temporary, statePath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
