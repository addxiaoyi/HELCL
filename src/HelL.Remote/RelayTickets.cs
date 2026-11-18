using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HelL.Remote;

public sealed record RelayTicket(string Room, string Role, long Expires);
public sealed record RelayRoom(string Room, string HostTicket, string ClientTicket, long Expires);

public sealed class RelayTickets
{
    private readonly byte[] key;
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    public RelayTickets(string signingKey)
    {
        try { key = Convert.FromBase64String(signingKey); }
        catch (FormatException ex) { throw new ArgumentException("Relay signing key must be Base64.", nameof(signingKey), ex); }
        if (key.Length != 32) throw new ArgumentException("Relay signing key must contain exactly 32 random bytes.", nameof(signingKey));
    }

    public RelayRoom Issue(DateTimeOffset now)
    {
        string room = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        long expires = now.Add(Lifetime).ToUnixTimeSeconds();
        return new(room, Sign(new(room, "host", expires)), Sign(new(room, "client", expires)), expires);
    }

    public RelayTicket? Validate(string ticket, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(ticket) || ticket.Length > 1024) return null;
        string[] parts = ticket.Split('.');
        if (parts.Length != 2) return null;
        try
        {
            byte[] payload = Decode(parts[0]);
            byte[] supplied = Decode(parts[1]);
            byte[] expected = HMACSHA256.HashData(key, payload);
            if (!CryptographicOperations.FixedTimeEquals(supplied, expected)) return null;
            RelayTicket? claims = JsonSerializer.Deserialize<RelayTicket>(payload);
            if (claims is null || claims.Room is null || claims.Role is not ("host" or "client") ||
                claims.Room.Length != 48 || claims.Room.Any(ch => !Uri.IsHexDigit(ch)) ||
                claims.Expires <= now.ToUnixTimeSeconds() || claims.Expires > now.Add(Lifetime).ToUnixTimeSeconds()) return null;
            return claims;
        }
        catch (Exception ex) when (ex is FormatException or JsonException) { return null; }
    }

    private string Sign(RelayTicket claims)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(claims);
        return Encode(payload) + "." + Encode(HMACSHA256.HashData(key, payload));
    }

    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Decode(string encoded)
    {
        string padded = encoded.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(padded.PadRight((padded.Length + 3) / 4 * 4, '='));
    }
}
