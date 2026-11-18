using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HelL.Remote;

public static class InvitationFeedCodec
{
    private static readonly JsonSerializerOptions Json = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    public static InvitationFeedDescriptor Create(DateTimeOffset expires)
    {
        var descriptor = new InvitationFeedDescriptor(Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant(),
            Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant(),
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), expires.ToUnixTimeSeconds());
        Validate(descriptor);
        return descriptor;
    }

    public static void Validate(InvitationFeedDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (!Hex(descriptor.Channel, 48) || !Hex(descriptor.ReaderToken, 64)
            || descriptor.Expires <= now || descriptor.Expires > DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds()) throw Invalid();
        byte[] key = Key(descriptor);
        CryptographicOperations.ZeroMemory(key);
    }

    public static InvitationFeedEnvelope Protect(RemoteHostInvite invitation, InvitationFeedDescriptor descriptor, long revision)
    {
        Validate(descriptor);
        ValidateInvite(invitation);
        if (revision < 1) throw new ArgumentOutOfRangeException(nameof(revision));
        byte[] key = Key(descriptor);
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(invitation with { Feed = descriptor }, Json);
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] cipher = new byte[plain.Length];
        byte[] tag = new byte[16];
        try
        {
            if (plain.Length > 16384) throw Invalid();
            using (var aes = new AesGcm(key, 16)) aes.Encrypt(nonce, plain, cipher, tag, Aad(descriptor.Channel, revision));
            return new(revision, Convert.ToBase64String(nonce), Convert.ToBase64String(cipher), Convert.ToBase64String(tag));
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plain); }
    }

    public static RemoteHostInvite Unprotect(InvitationFeedEnvelope envelope, RemoteHostInvite current, long afterRevision)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ValidateInvite(current, allowExpired: true);
        InvitationFeedDescriptor descriptor = current.Feed ?? throw Invalid();
        Validate(descriptor);
        if (envelope.Revision <= afterRevision || envelope.Revision < 1) throw Invalid();
        byte[] key = Key(descriptor);
        byte[]? plain = null;
        try
        {
            if (envelope.Ciphertext is null || envelope.Ciphertext.Length > 21848) throw Invalid();
            byte[] nonce = Convert.FromBase64String(envelope.Nonce);
            byte[] tag = Convert.FromBase64String(envelope.Tag);
            byte[] cipher = Convert.FromBase64String(envelope.Ciphertext);
            if (nonce.Length != 12 || tag.Length != 16 || cipher.Length is < 1 or > 16384) throw Invalid();
            plain = new byte[cipher.Length];
            using (var aes = new AesGcm(key, 16)) aes.Decrypt(nonce, cipher, tag, plain, Aad(descriptor.Channel, envelope.Revision));
            RemoteHostInvite next = JsonSerializer.Deserialize<RemoteHostInvite>(plain, Json) ?? throw Invalid();
            ValidateInvite(next);
            if (next.Host.DeviceId != current.Host.DeviceId || !string.Equals(next.Host.Fingerprint, current.Host.Fingerprint, StringComparison.OrdinalIgnoreCase)
                || next.ClientDeviceId != current.ClientDeviceId || Relay(next.Relay) != Relay(current.Relay)
                || next.Feed != descriptor) throw Invalid();
            return next;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or CryptographicException or JsonException)
        { throw Invalid(); }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            if (plain is not null) CryptographicOperations.ZeroMemory(plain);
        }
    }

    public static Uri Relay(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out Uri? relay) || !string.IsNullOrEmpty(relay.UserInfo)
            || relay.Query.Length != 0 || relay.Fragment.Length != 0 || !relay.AbsolutePath.EndsWith('/')
            || (relay.Scheme != "https" && !(relay.Scheme == "http" && relay.IsLoopback)))
            throw new ArgumentException("Use an HTTPS relay base URL ending in '/'. HTTP is limited to loopback tests.", nameof(address));
        return relay;
    }

    private static void ValidateInvite(RemoteHostInvite invitation, bool allowExpired = false)
    {
        ArgumentNullException.ThrowIfNull(invitation);
        if (invitation.Host is null || !Guid.TryParseExact(invitation.Host.DeviceId, "N", out _)
            || !Guid.TryParseExact(invitation.ClientDeviceId, "N", out _) || !Hex(invitation.Host.Fingerprint, 64)
            || string.IsNullOrWhiteSpace(invitation.Host.Name) || invitation.Host.Name.Length > 80 || invitation.Host.Name.Any(char.IsControl)
            || !Hex(invitation.Room, 48) || string.IsNullOrWhiteSpace(invitation.ClientTicket) || invitation.ClientTicket.Length > 1024
            || invitation.ClientTicket.Any(char.IsControl) || (!allowExpired && invitation.Expires <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            || invitation.Expires > DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds()) throw Invalid();
        _ = Relay(invitation.Relay);
    }

    private static byte[] Key(InvitationFeedDescriptor descriptor)
    {
        try
        {
            if (descriptor.EncryptionKey is null || descriptor.EncryptionKey.Length != 44 || descriptor.EncryptionKey.Any(char.IsWhiteSpace)) throw Invalid();
            byte[] key = Convert.FromBase64String(descriptor.EncryptionKey);
            if (key.Length == 32) return key;
            CryptographicOperations.ZeroMemory(key);
            throw Invalid();
        }
        catch (FormatException) { throw Invalid(); }
    }

    private static byte[] Aad(string channel, long revision) => Encoding.UTF8.GetBytes("HelL invitation feed v1:" + channel + ":" + revision.ToString(System.Globalization.CultureInfo.InvariantCulture));
    private static bool Hex(string? text, int size) => text is not null && text.Length == size && text.All(Uri.IsHexDigit);
    private static InvalidDataException Invalid() => new("The invitation update is expired, damaged, out of sequence or does not match this pairing.");
}
