using System.Security.Cryptography;
using System.Text;
using Android.Content;
using Android.Security.Keystore;
using HelL.Core.Platform;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using CipherMode = Javax.Crypto.CipherMode;

namespace HelL.Android;

internal sealed class AndroidSecretStore(Context context) : ISecretStore
{
    private const string Provider = "AndroidKeyStore";
    private const string Transformation = "AES/GCM/NoPadding";
    private static readonly object Gate = new();
    private readonly ISharedPreferences preferences = context.GetSharedPreferences("hell-account-keys", FileCreationMode.Private)
        ?? throw new InvalidOperationException("AndroidSecretStore: 无法打开应用凭据记录。");

    public string GetPassword(string service, string username)
    {
        string alias = Alias(service, username);
        lock (Gate)
        {
            string? stored = preferences.GetString(alias, null);
            if (stored == null) return "";
            var (iv, encrypted) = CredentialEnvelope.Read(stored);
            using var store = OpenStore();
            using var key = store.GetKey(alias, null) as ISecretKey
                ?? throw new InvalidOperationException("AndroidSecretStore: 设备密钥不可用，请重新登录。");
            using var cipher = Cipher.GetInstance(Transformation)
                ?? throw new InvalidOperationException("AndroidSecretStore: 系统加密服务不可用。");
            using var parameters = new GCMParameterSpec(128, iv);
            cipher.Init(CipherMode.DecryptMode, key, parameters);
            cipher.UpdateAAD(Encoding.UTF8.GetBytes(alias));
            byte[] plain = cipher.DoFinal(encrypted) ?? throw new CryptographicException("AndroidSecretStore: 凭据解密失败。");
            try { return Encoding.UTF8.GetString(plain); }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
    }

    public void SetPassword(string service, string username, string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        string alias = Alias(service, username);
        byte[] plain = Encoding.UTF8.GetBytes(password);
        try
        {
            if (plain.Length is < 1 or > CredentialEnvelope.MaxSecretBytes) throw new ArgumentException("应用密钥长度无效。", nameof(password));
            lock (Gate)
            {
                using var store = OpenStore();
                bool created = !store.ContainsAlias(alias);
                try
                {
                    if (created) CreateKey(alias);
                    using var key = store.GetKey(alias, null) as ISecretKey
                        ?? throw new InvalidOperationException("AndroidSecretStore: 无法读取设备密钥。");
                    using var cipher = Cipher.GetInstance(Transformation)
                        ?? throw new InvalidOperationException("AndroidSecretStore: 系统加密服务不可用。");
                    cipher.Init(CipherMode.EncryptMode, key);
                    cipher.UpdateAAD(Encoding.UTF8.GetBytes(alias));
                    byte[] encrypted = cipher.DoFinal(plain) ?? throw new CryptographicException("AndroidSecretStore: 凭据加密失败。");
                    byte[] iv = cipher.GetIV() ?? throw new CryptographicException("AndroidSecretStore: 系统未生成加密随机数。");
                    string envelope = CredentialEnvelope.Write(iv, encrypted);
                    using var edit = preferences.Edit() ?? throw new IOException("AndroidSecretStore: 无法编辑凭据记录。");
                    if (!edit.PutString(alias, envelope)!.Commit()) throw new IOException("AndroidSecretStore: 未能保存凭据记录。");
                }
                catch
                {
                    // Only a newly allocated alias can be discarded; the previous account must remain decryptable.
                    if (created)
                    {
                        try
                        {
                            using var edit = preferences.Edit() ?? throw new IOException("Credential cleanup editor unavailable.");
                            if (!edit.Remove(alias)!.Commit()) throw new IOException("Credential cleanup commit failed.");
                            if (store.ContainsAlias(alias)) store.DeleteEntry(alias);
                        }
                        catch (Exception cleanupError)
                        {
                            System.Diagnostics.Trace.TraceWarning("AndroidSecretStore.SetPassword cleanup: " + cleanupError.Message);
                        }
                    }
                    throw;
                }
            }
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    public void DeletePassword(string service, string username)
    {
        string alias = Alias(service, username);
        lock (Gate)
        {
            using var edit = preferences.Edit() ?? throw new IOException("AndroidSecretStore: 无法编辑凭据记录。");
            if (!edit.Remove(alias)!.Commit()) throw new IOException("AndroidSecretStore: 未能删除凭据记录。");
            using var store = OpenStore();
            if (store.ContainsAlias(alias)) store.DeleteEntry(alias);
        }
    }

    private static KeyStore OpenStore()
    {
        var store = KeyStore.GetInstance(Provider) ?? throw new InvalidOperationException("AndroidSecretStore: 设备密钥库不可用。");
        try { store.Load(null); return store; }
        catch { store.Dispose(); throw; }
    }

    private static void CreateKey(string alias)
    {
        using var builder = new KeyGenParameterSpec.Builder(alias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt);
        using var parameters = builder.SetKeySize(256)!.SetBlockModes(KeyProperties.BlockModeGcm)!
            .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)!.SetRandomizedEncryptionRequired(true)!.Build();
        using var generator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, Provider)
            ?? throw new InvalidOperationException("AndroidSecretStore: 设备不支持应用密钥生成。");
        generator.Init(parameters);
        using var key = generator.GenerateKey();
    }

    private static string Alias(string service, string username)
    {
        if (string.IsNullOrWhiteSpace(service) || string.IsNullOrWhiteSpace(username) || service.Length > 256 || username.Length > 256
            || service.Any(char.IsControl) || username.Any(char.IsControl))
            throw new ArgumentException("凭据标识无效。");
        return "hell.account." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(service + "\0" + username))).ToLowerInvariant();
    }
}
