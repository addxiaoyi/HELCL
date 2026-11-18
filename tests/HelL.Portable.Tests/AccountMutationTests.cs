using HelL.Core;
using HelL.Core.Launcher;
using HelL.Core.Platform;
using HelL.Tests;
using Xunit;

namespace HelL.Portable.Tests;

public sealed class AccountMutationTests : IDisposable
{
    private readonly TestHome home = new();
    private readonly ISecretStore previous = PlatformServices.SecretStore;
    private readonly TrackedSecrets keys = new();
    private static string StoredKey => Auth._keyring_name("old-access");

    public AccountMutationTests() => PlatformServices.SecretStore = keys;

    [Theory]
    [InlineData("add")]
    [InlineData("replace")]
    [InlineData("remove")]
    [InlineData("select")]
    public void FailedAccountCommitPreservesMemoryAndStoredCredentials(string operation)
    {
        AccountManager manager = Seed(retained: true);
        List<object> accounts = manager.accounts;
        byte[] saved = File.ReadAllBytes(Auth.ACCOUNTS_FILE);
        string[] previousKeys = keys.Keys.Keys.Order().ToArray();
        if (OperatingSystem.IsWindows())
        {
            using var held = new FileStream(Auth.ACCOUNTS_FILE, FileMode.Open, FileAccess.Read, FileShare.Read);
            Assert.ThrowsAny<Exception>(() => Mutate(manager, operation));
        }
        else
        {
            string backup = Auth.ACCOUNTS_FILE + ".commit-fixture";
            bool blocked = false;
            keys.AfterWrite = () =>
            {
                if (blocked) return;
                File.Move(Auth.ACCOUNTS_FILE, backup);
                Directory.CreateDirectory(Auth.ACCOUNTS_FILE);
                blocked = true;
            };
            try
            {
                Assert.Throws<IOException>(() => Mutate(manager, operation));
                Assert.True(blocked);
                Assert.NotEmpty(keys.Writes);
                if (operation is "add" or "replace") Assert.Contains(keys.Writes, key => !previousKeys.Contains(key));
            }
            finally
            {
                keys.AfterWrite = null;
                if (Directory.Exists(Auth.ACCOUNTS_FILE)) Directory.Delete(Auth.ACCOUNTS_FILE);
                if (File.Exists(backup)) File.Move(backup, Auth.ACCOUNTS_FILE);
            }
        }
        Assert.Same(accounts, manager.accounts);
        Assert.Equal("Player", manager.active);
        Assert.Equal("old-access", manager.GetAccount("Player")["access_token"]);
        Assert.Equal("old-access", keys.Keys[StoredKey]);
        Assert.Equal(previousKeys, keys.Keys.Keys.Order().ToArray());
        Assert.Equal(saved, File.ReadAllBytes(Auth.ACCOUNTS_FILE));
        Assert.Equal("old-access", new AccountManager().GetAccount("Player")["access_token"]);
    }

    [Fact]
    public void CommittedRemovalPreservesAKeyStillReferencedByAnotherAccount()
    {
        AccountManager manager = Seed(shared: true);
        manager.RemoveAccount("Player");
        Assert.Equal("old-access", manager.GetAccount("Other")["access_token"]);
        Assert.Equal("old-access", new AccountManager().GetAccount("Other")["access_token"]);
        if (!OperatingSystem.IsWindows()) Assert.Contains(StoredKey, keys.Keys.Keys);
    }

    [Fact]
    public void CommittedRemovalDeletesOnlyUnreferencedOldKeys()
    {
        AccountManager manager = Seed();
        manager.RemoveAccount("Player");
        Assert.Null(new AccountManager().GetAccount("Player"));
        Assert.DoesNotContain(StoredKey, keys.Keys.Keys);
    }

    [Theory]
    [InlineData("add", "broken-json-with-secret")]
    [InlineData("replace", "[]")]
    [InlineData("remove", "{\"accounts\":{}}")]
    [InlineData("select", "{\"accounts\":[null]}")]
    [InlineData("add", "{\"accounts\":[{\"name\":\"Player\",\"access_token\":42}]}")]
    public void UnreadableAccountInventoryCannotBeReplacedOrCleaned(string operation, string saved)
    {
        AccountManager manager = Seed();
        List<object> accounts = manager.accounts;
        File.WriteAllText(Auth.ACCOUNTS_FILE, saved);
        var error = Assert.Throws<InvalidDataException>(() => Mutate(manager, operation));
        Assert.DoesNotContain("broken-json-with-secret", error.ToString());
        Assert.Same(accounts, manager.accounts);
        Assert.Equal("Player", manager.active);
        Assert.Equal("old-access", manager.GetAccount("Player")["access_token"]);
        Assert.Equal(saved, File.ReadAllText(Auth.ACCOUNTS_FILE));
        Assert.Equal("old-access", keys.Keys[StoredKey]);
    }

    [Fact]
    public void LockedOldInventoryCannotDeleteAStillReferencedKey()
    {
        AccountManager manager = Seed();
        // Retain the keyring form so this also exercises compensation on Windows, where new tokens use DPAPI.
        manager.GetAccount("Player")["access_token"] = "keyring:" + StoredKey;
        List<object> accounts = manager.accounts;
        byte[] saved = File.ReadAllBytes(Auth.ACCOUNTS_FILE);
        using (var held = new FileStream(Auth.ACCOUNTS_FILE, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Exception? error = Record.Exception(manager.Save);
            Assert.NotNull(error);
            Assert.Same(accounts, manager.accounts);
            Assert.Equal("Player", manager.active);
            Assert.Equal("old-access", keys.Keys[StoredKey]);
            Assert.IsType<IOException>(error);
        }
        Assert.Equal(saved, File.ReadAllBytes(Auth.ACCOUNTS_FILE));
        Assert.Equal("old-access", new AccountManager().GetAccount("Player")["access_token"]);
    }

    private AccountManager Seed(bool shared = false, bool retained = false)
    {
        keys.Keys[StoredKey] = "old-access";
        var accounts = new List<object> { PyDict.Of("name", "Player", "type", "microsoft", "access_token", "keyring:" + StoredKey) };
        if (shared) accounts.Add(PyDict.Of("name", "Other", "type", "microsoft", "access_token", "keyring:" + StoredKey));
        else accounts.Add(PyDict.Of("name", "Offline", "type", "offline"));
        if (retained)
        {
            string key = Auth._keyring_name("retained-access");
            keys.Keys[key] = "retained-access";
            accounts.Add(PyDict.Of("name", "Retained", "type", "microsoft", "access_token", "keyring:" + key));
        }
        Utils.WriteJson(Auth.ACCOUNTS_FILE, PyDict.Of("accounts", accounts, "active", "Player"));
        return new AccountManager();
    }

    private static void Mutate(AccountManager manager, string operation)
    {
        switch (operation)
        {
            case "add": manager.AddAccount(PyDict.Of("name", "New", "type", "microsoft", "access_token", "new-access")); break;
            case "replace": manager.AddAccount(PyDict.Of("name", "Player", "type", "microsoft", "access_token", "replacement")); break;
            case "remove": manager.RemoveAccount("Player"); break;
            case "select": manager.SetActive("Offline"); break;
            default: throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    public void Dispose()
    {
        PlatformServices.SecretStore = previous;
        home.Dispose();
    }

    private sealed class TrackedSecrets : ISecretStore
    {
        public Dictionary<string, string> Keys { get; } = new();
        public List<string> Writes { get; } = new();
        public Action? AfterWrite { get; set; }
        public string GetPassword(string service, string username) => Keys.GetValueOrDefault(username, "");
        public void SetPassword(string service, string username, string password)
        {
            Keys[username] = password;
            Writes.Add(username);
            AfterWrite?.Invoke();
        }
        public void DeletePassword(string service, string username) => Keys.Remove(username);
    }
}
