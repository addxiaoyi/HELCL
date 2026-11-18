using System.Text.Json;

namespace HelL.Android;

internal static class CredentialEnvelope
{
    internal const int MaxSecretBytes = 4096;
    private sealed record Envelope(int Schema, string? Iv, string? Ciphertext);

    internal static (byte[] Iv, byte[] Ciphertext) Read(string stored)
    {
        if (string.IsNullOrEmpty(stored) || stored.Length > 16384)
            throw new InvalidDataException("凭据记录大小无效，请重新登录。");
        Envelope envelope;
        try { envelope = JsonSerializer.Deserialize<Envelope>(stored) ?? throw new JsonException("Empty envelope."); }
        catch (JsonException error) { throw new InvalidDataException("凭据记录已损坏，请重新登录。", error); }
        if (envelope.Schema != 1 || string.IsNullOrEmpty(envelope.Iv) || string.IsNullOrEmpty(envelope.Ciphertext))
            throw new InvalidDataException("凭据格式不受支持，请重新登录。");
        byte[] iv, encrypted;
        try { iv = Convert.FromBase64String(envelope.Iv); encrypted = Convert.FromBase64String(envelope.Ciphertext); }
        catch (FormatException error) { throw new InvalidDataException("凭据编码无效，请重新登录。", error); }
        if (iv.Length != 12 || encrypted.Length is < 16 or > MaxSecretBytes + 16)
            throw new InvalidDataException("凭据格式不受支持，请重新登录。");
        return (iv, encrypted);
    }

    internal static string Write(byte[] iv, byte[] encrypted)
    {
        if (iv.Length != 12 || encrypted.Length is < 16 or > MaxSecretBytes + 16)
            throw new InvalidDataException("系统加密输出格式无效。");
        return JsonSerializer.Serialize(new Envelope(1, Convert.ToBase64String(iv), Convert.ToBase64String(encrypted)));
    }
}
