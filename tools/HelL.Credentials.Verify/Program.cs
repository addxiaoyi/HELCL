using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using HelL.Contracts;
using HelL.Core;
using HelL.Core.Launcher;
using HelL.Core.Platform;
using HelL.Portable;
using HelL.Remote;

internal static class Program
{
    private static string stage = "arguments";

    private static int Main(string[] args)
    {
        try
        {
            if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
                throw new PlatformNotSupportedException("Run this verifier on Linux or macOS.");
            string platform = OperatingSystem.IsLinux() ? "linux" : OperatingSystem.IsMacOS() ? "macos" : "unsupported";
            if (args.Length != 2 || args[0] != platform || platform == "unsupported" || !Path.IsPathFullyQualified(args[1]))
                throw new ArgumentException("Use linux|macos <absolute-report-path> on the matching native host.");
            string root = Path.Combine(Path.GetTempPath(), "hell-native-credentials-" + Guid.NewGuid().ToString("N"));
            if (!OperatingSystem.IsWindows())
                Directory.CreateDirectory(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var secrets = new ScopedSecrets(SystemSecretStore.Create());
            ISecretStore previousSecrets = PlatformServices.SecretStore;
            string previousRoot = Utils.ROOT;
            try
            {
                stage = "native-secret-roundtrip";
                VerifySecretStore(secrets);
                stage = "native-account-restart-and-failed-write";
                VerifyVault(Path.Combine(root, "vault"), secrets);
                stage = "account-file-failure";
                VerifyAccountMutations(Path.Combine(root, "accounts"), secrets);
                stage = "native-device-identity-recovery";
                VerifyIdentity(Path.Combine(root, "pairing"), secrets);
            }
            finally
            {
                PlatformServices.SecretStore = previousSecrets;
                Utils.SetRoot(previousRoot);
                Config.Reset();
                string checkedStage = stage;
                stage = "native-secret-cleanup";
                try { secrets.Cleanup(); }
                finally { Directory.Delete(root, recursive: true); }
                stage = checkedStage;
            }
            stage = "report";
            var report = new
            {
                schema = 1,
                ok = true,
                platform,
                framework = RuntimeInformation.FrameworkDescription,
                native_secret_roundtrip = true,
                account_restart = true,
                failed_key_write_injected = true,
                account_file_failure_preserves_state = true,
                device_identity_restart = true,
                failed_pairing_write_injected = true,
                native_entries_cleaned = true
            };
            Directory.CreateDirectory(Path.GetDirectoryName(args[1])!);
            string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(args[1], json);
            Console.WriteLine(json);
            return 0;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Native error payloads and encrypted records may contain secret material.
            Console.Error.WriteLine("Native credential verification failed at " + stage + " (" + ex.GetType().Name + ").");
            return 1;
        }
    }

    private static void VerifySecretStore(ScopedSecrets secrets)
    {
        const string service = "roundtrip";
        const string key = "fictional-key";
        string first = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        string second = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        secrets.SetPassword(service, key, first);
        Require(secrets.GetPassword(service, key) == first, "The native store did not retain a key.");
        secrets.SetPassword(service, key, second);
        Require(secrets.GetPassword(service, key) == second, "The native store did not update a key.");
        secrets.DeletePassword(service, key);
        secrets.DeletePassword(service, key);
        Require(secrets.GetPassword(service, key) == "", "The native store did not delete a key.");
    }

    private static void VerifyVault(string root, ScopedSecrets secrets)
    {
        var vault = new AccountVault(root, secrets);
        string token = "fictional-access-" + new string('x', 12000);
        var account = PyDict.Of("type", "microsoft", "name", "NativeFixture", "uuid", "8667ba71-b85a-4004-af54-457a9734eed7",
            "access_token", token, "refresh_token", "fictional-refresh", "expires_at", 0L);
        vault.Save(account);
        Require(new AccountVault(root, secrets).Load()?["access_token"] as string == token, "The encrypted account did not survive reopening.");
        string path = Path.Combine(root, "portable-account.json");
        byte[] saved = File.ReadAllBytes(path);
        Require(!File.ReadAllText(path).Contains("fictional", StringComparison.Ordinal), "The account file contains plaintext credentials.");
        secrets.RejectWrites = true;
        try { ExpectIoFailure(() => vault.Save(account), "A rejected vault write unexpectedly succeeded."); }
        finally { secrets.RejectWrites = false; }
        Require(saved.SequenceEqual(File.ReadAllBytes(path)), "A failed vault write changed the account file.");
        Require(new AccountVault(root, secrets).Load()?["access_token"] as string == token, "A failed vault write removed the previous key.");
        vault.Remove();
        Require(vault.Load() == null && vault.CleanupWarning == null, "The vault did not remove its native key cleanly.");
    }

    private static void VerifyAccountMutations(string root, ScopedSecrets secrets)
    {
        Utils.SetRoot(root);
        Config.Reset();
        PlatformServices.SecretStore = secrets;
        var manager = new AccountManager();
        manager.AddAccount(PyDict.Of("type", "microsoft", "name", "Saved", "access_token", "fictional-manager-access", "refresh_token", "fictional-manager-refresh"));
        manager.AddAccount(manager.OfflineAccount("Offline"));
        manager.SetActive("Saved");
        List<object> accounts = manager.accounts;
        byte[] saved = File.ReadAllBytes(Auth.ACCOUNTS_FILE);
        Action[] changes =
        [
            () => manager.AddAccount(manager.OfflineAccount("New")),
            () => manager.AddAccount(PyDict.Of("type", "microsoft", "name", "Saved", "access_token", "fictional-replacement")),
            () => manager.RemoveAccount("Saved"),
            () => manager.SetActive("Offline")
        ];
        foreach (Action change in changes)
        {
            using (var held = new FileStream(Auth.ACCOUNTS_FILE, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                ExpectIoFailure(change, "A locked account file unexpectedly accepted a change.");
            Require(ReferenceEquals(accounts, manager.accounts) && (manager.active as string) == "Saved", "A failed account write changed memory state.");
            Require(saved.SequenceEqual(File.ReadAllBytes(Auth.ACCOUNTS_FILE)), "A failed account write changed the saved file.");
            Require(new AccountManager().GetAccount("Saved")?["access_token"] as string == "fictional-manager-access", "A failed account write removed a native credential.");
        }
    }

    private static void VerifyIdentity(string root, ScopedSecrets secrets)
    {
        var native = new PairingSecrets(secrets);
        var pairings = new RemotePairingStore(root, "Native verification", native);
        RemotePairingOffer identity = pairings.Offer;
        using var peer = DeviceTunnel.CreateIdentity("Fictional peer");
        var offer = new RemotePairingOffer(Guid.NewGuid().ToString("N"), "Fictional peer", DeviceTunnel.Fingerprint(peer));
        pairings.Trust(offer, [HostPermission.Read]);
        var reopened = new RemotePairingStore(root, "Native verification", native);
        Require(reopened.Offer == identity && reopened.Find(offer.DeviceId)?.Fingerprint == offer.Fingerprint, "The device identity or pairing did not survive reopening.");
        using (var restored = reopened.OpenIdentity())
            Require(restored.HasPrivateKey && DeviceTunnel.Fingerprint(restored) == identity.Fingerprint, "The restored device private key does not match its fingerprint.");
        string path = Path.Combine(root, "remote-pairing.json");
        byte[] saved = File.ReadAllBytes(path);
        secrets.RejectWrites = true;
        try { ExpectIoFailure(() => reopened.Trust(offer, [HostPermission.Read, HostPermission.CancelTasks]), "A rejected pairing write unexpectedly succeeded."); }
        finally { secrets.RejectWrites = false; }
        Require(saved.SequenceEqual(File.ReadAllBytes(path)), "A failed pairing write changed the saved identity.");
        var retained = new RemotePairingStore(root, "Native verification", native);
        Require(retained.Offer == identity && retained.Find(offer.DeviceId)!.Permissions.SequenceEqual([HostPermission.Read]), "A failed pairing write changed the restored identity or permissions.");
        retained.Revoke(offer.DeviceId);
        Require(new RemotePairingStore(root, "Native verification", native).Find(offer.DeviceId) == null, "Device revocation did not survive reopening.");
    }

    private static void ExpectIoFailure(Action operation, string message)
    {
        try { operation(); }
        catch (IOException) { return; }
        throw new InvalidOperationException(message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class PairingSecrets(ScopedSecrets secrets) : IRemoteSecretStore
    {
        public string Read(string key) => secrets.GetPassword("pairing", key);
        public void Write(string key, string value) => secrets.SetPassword("pairing", key, value);
        public void Delete(string key) => secrets.DeletePassword("pairing", key);
    }

    private sealed class ScopedSecrets(ISecretStore native) : ISecretStore
    {
        private readonly string prefix = "HelL native verification " + Guid.NewGuid().ToString("N") + " ";
        private readonly HashSet<(string Service, string Key)> owned = [];
        public bool RejectWrites { get; set; }
        public string GetPassword(string service, string username) => native.GetPassword(prefix + service, username);
        public void SetPassword(string service, string username, string password)
        {
            if (RejectWrites) throw new IOException("The verification fixture rejected this key write.");
            owned.Add((prefix + service, username));
            native.SetPassword(prefix + service, username, password);
        }
        public void DeletePassword(string service, string username)
        {
            native.DeletePassword(prefix + service, username);
            Require(native.GetPassword(prefix + service, username) == "", "A verification key was not removed.");
            owned.Remove((prefix + service, username));
        }
        public void Cleanup()
        {
            var failures = new List<Exception>();
            foreach ((string service, string key) in owned)
            {
                try
                {
                    native.DeletePassword(service, key);
                    Require(native.GetPassword(service, key) == "", "A verification key was not removed.");
                }
                catch (Exception ex) when (ex is not OutOfMemoryException) { failures.Add(ex); }
            }
            if (failures.Count != 0) throw new AggregateException("Verification credentials could not all be removed.", failures);
            owned.Clear();
        }
    }
}
