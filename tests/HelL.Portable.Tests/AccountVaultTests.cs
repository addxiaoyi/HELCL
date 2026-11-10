using System.Security.Cryptography;
using System.Text.Json.Nodes;
using HelL.Core;
using HelL.Core.Platform;
using HelL.Portable;
using Xunit;

namespace HelL.Portable.Tests;

public sealed class AccountVaultTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "hell-vault-test-" + Guid.NewGuid().ToString("N"));
    internal sealed class MemoryStore : ISecretStore
    {
        public Dictionary<string, string> Keys { get; } = new();
        public bool FailWrites;
        public string GetPassword(string service, string username) => Keys.GetValueOrDefault(username, "");
        public void SetPassword(string service, string username, string password)
        {
            if (FailWrites) throw new IOException("Test keyring locked");
            Keys[username] = password;
        }
        public void DeletePassword(string service, string username) => Keys.Remove(username);
    }

    internal static PyDict Account(string token = "fictional-access-token") => PyDict.Of("type", "microsoft",
        "name", "TestPlayer", "uuid", "8667ba71-b85a-4004-af54-457a9734eed7", "access_token", token,
        "refresh_token", "fictional-refresh-token", "expires_at", 0L);

    [Fact]
    public void EncryptedAccountRoundtripsWithoutPlaintextOrKeysOnDisk()
    {
        var store = new MemoryStore();
        var vault = new AccountVault(root, store);
        vault.Save(Account(new string('X', 12000)));
        string disk = File.ReadAllText(Path.Combine(root, "portable-account.json"));
        Assert.DoesNotContain("fictional", disk);
        Assert.DoesNotContain("TestPlayer", disk);
        Assert.DoesNotContain(new string('X', 100), disk);
        Assert.DoesNotContain(store.Keys.Single().Value, disk);
        Assert.Equal(44, store.Keys.Single().Value.Length);
        Assert.Equal(new string('X', 12000), new AccountVault(root, store).Load()!["access_token"]);
        vault.Remove();
        Assert.Null(vault.Load());
        Assert.Empty(store.Keys);
    }

    [Fact]
    public void RefreshRotationDeletesPreviousKeyOnlyAfterCommit()
    {
        var store = new MemoryStore();
        var vault = new AccountVault(root, store);
        vault.Save(Account());
        string oldId = store.Keys.Single().Key;
        store.FailWrites = true;
        Assert.Throws<IOException>(() => vault.Save(Account("replacement-token")));
        Assert.Equal("fictional-access-token", vault.Load()!["access_token"]);
        Assert.Contains(oldId, store.Keys.Keys);
        store.FailWrites = false;
        vault.Save(Account("replacement-token"));
        Assert.Equal("replacement-token", vault.Load()!["access_token"]);
        Assert.Single(store.Keys);
        Assert.DoesNotContain(oldId, store.Keys.Keys);
    }

    [Fact]
    public void MissingKeyAndTamperedCiphertextAreRejected()
    {
        var store = new MemoryStore();
        var vault = new AccountVault(root, store);
        vault.Save(Account());
        Assert.Throws<InvalidOperationException>(() => new AccountVault(root, new MemoryStore()).Load());
        string path = Path.Combine(root, "portable-account.json");
        JsonObject envelope = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        byte[] cipher = Convert.FromBase64String(envelope["Ciphertext"]!.GetValue<string>());
        cipher[0] ^= 1;
        envelope["Ciphertext"] = Convert.ToBase64String(cipher);
        File.WriteAllText(path, envelope.ToJsonString());
        Assert.Throws<InvalidDataException>(() => vault.Load());
        Assert.Single(store.Keys);
    }

    [Fact]
    public void ExplicitReloginAndLogoutCanRecoverMalformedRecords()
    {
        Directory.CreateDirectory(root);
        var vault = new AccountVault(root, new MemoryStore());
        string file = Path.Combine(root, "portable-account.json");
        File.WriteAllText(file, "broken-json");
        Assert.Throws<InvalidDataException>(() => vault.Load());
        vault.Save(Account());
        Assert.Equal("TestPlayer", vault.Load()!["name"]);
        File.WriteAllText(file, "broken-json");
        vault.Remove();
        Assert.Null(vault.Load());
    }

    [Fact]
    public void NativeWindowsCredentialsCanPersistALargeEncryptedAccount()
    {
        if (!OperatingSystem.IsWindows()) return;
        var vault = new AccountVault(root, SystemSecretStore.Create());
        try
        {
            vault.Save(Account(new string('Z', 12000)));
            Assert.Equal(new string('Z', 12000), new AccountVault(root, SystemSecretStore.Create()).Load()!["access_token"]);
        }
        finally { vault.Remove(); }
        Assert.Null(vault.CleanupWarning);
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
}
