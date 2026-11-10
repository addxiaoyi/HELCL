#nullable enable
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HelL.Core;
using HelL.Core.Platform;

namespace HelL.Portable;

public sealed class AccountVault
{
    private const string Service = "HelL portable account encryption";
    private readonly string path;
    private readonly ISecretStore store;
    public string? CleanupWarning { get; private set; }
    private sealed record Envelope(int Schema, string KeyId, string Nonce, string Ciphertext, string Tag);

    public AccountVault(string root, ISecretStore store)
    {
        path = Path.Combine(Path.GetFullPath(root), "portable-account.json");
        this.store = store;
    }

    public PyDict? Load()
    {
        if (!File.Exists(path)) return null;
        Envelope envelope = ReadEnvelope();
        string keyText = store.GetPassword(Service, envelope.KeyId);
        if (string.IsNullOrEmpty(keyText))
            throw new InvalidOperationException("本机无法解密已保存账号。便携数据移到另一台电脑后需要重新登录。");
        byte[] key = Convert.FromBase64String(keyText), cipher = Convert.FromBase64String(envelope.Ciphertext);
        byte[] plain = new byte[cipher.Length];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(Convert.FromBase64String(envelope.Nonce), cipher, Convert.FromBase64String(envelope.Tag), plain, Aad(envelope.KeyId));
            return PyJson.Loads(Encoding.UTF8.GetString(plain)) as PyDict
                ?? throw new InvalidDataException("账号记录损坏，请重新登录。");
        }
        catch (CryptographicException ex) { throw new InvalidDataException("账号记录无法解密或已损坏，请重新登录。", ex); }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plain); }
    }

    public void Save(PyDict account)
    {
        Envelope? previous = PreviousEnvelope();
        string id = Guid.NewGuid().ToString("N");
        byte[] key = RandomNumberGenerator.GetBytes(32), nonce = RandomNumberGenerator.GetBytes(12), tag = new byte[16];
        byte[] plain = Encoding.UTF8.GetBytes(PyJson.Dumps(account));
        byte[] cipher = new byte[plain.Length];
        string temporary = path + "." + id + ".tmp";
        bool stored = false, committed = false;
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(nonce, plain, cipher, tag, Aad(id));
            store.SetPassword(Service, id, Convert.ToBase64String(key));
            stored = true;
            if (store.GetPassword(Service, id) != Convert.ToBase64String(key))
                throw new IOException("系统凭据库未能保存应用密钥，本次账号仅保留在内存。");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Envelope(1, id, Convert.ToBase64String(nonce),
                Convert.ToBase64String(cipher), Convert.ToBase64String(tag))));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temporary, path, overwrite: true);
            committed = true;
            if (previous != null) DeleteKey(previous.KeyId);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plain);
            if (File.Exists(temporary)) File.Delete(temporary);
            if (stored && !committed) DeleteKey(id);
        }
    }

    public void Remove()
    {
        if (!File.Exists(path)) return;
        Envelope? envelope = PreviousEnvelope();
        File.Delete(path);
        if (envelope != null) DeleteKey(envelope.KeyId);
    }

    private Envelope? PreviousEnvelope()
    {
        if (!File.Exists(path)) return null;
        try { return ReadEnvelope(); }
        catch (InvalidDataException)
        {
            CleanupWarning = "已替换或移除损坏的账号记录；无法识别的旧钥匙环条目可在系统中手动清理。";
            return null;
        }
    }

    private void DeleteKey(string id)
    {
        try { store.DeletePassword(Service, id); }
        catch (Exception) { CleanupWarning = "账号密文已移除或替换，但旧的系统密钥未清理，可稍后在系统钥匙环中清理 HelL 条目。"; }
    }

    private Envelope ReadEnvelope()
    {
        if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("账号记录过大，请检查便携数据目录。");
        try
        {
            var envelope = JsonSerializer.Deserialize<Envelope>(File.ReadAllText(path));
            if (envelope == null || envelope.Schema != 1 || !Guid.TryParseExact(envelope.KeyId, "N", out _))
                throw new InvalidDataException("账号记录格式不受支持，请备份后重新登录。");
            return envelope;
        }
        catch (JsonException ex) { throw new InvalidDataException("账号记录损坏，请备份后重新登录。", ex); }
    }

    private static byte[] Aad(string id) => Encoding.UTF8.GetBytes("HelL portable account v1:" + id);
}
