using System.Text.Json;
using HelL.Core;
using HelL.Portable;

internal static class CredentialProbe
{
    public static int Run(string root)
    {
        if (!Path.IsPathFullyQualified(root)) throw new ArgumentException("Credential test needs an absolute isolated directory.");
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
            throw new ArgumentException("Credential test refuses a nonempty directory.");
        Directory.CreateDirectory(root);
        var vault = new AccountVault(root, SystemSecretStore.Create());
        try
        {
            string token = "fixture-" + Guid.NewGuid().ToString("N") + new string('X', 12000);
            vault.Save(PyDict.Of("access_token", token, "name", "CredentialProbe"));
            string file = File.ReadAllText(Path.Combine(root, "portable-account.json"));
            if (file.Contains(token) || file.Contains("CredentialProbe")) throw new IOException("Plaintext account was written to disk.");
            if (!Equals(new AccountVault(root, SystemSecretStore.Create()).Load()!["access_token"], token))
                throw new IOException("Native credential store roundtrip failed.");
            vault.Remove();
            if (vault.Load() != null || vault.CleanupWarning != null) throw new IOException("Native test key cleanup failed.");
            string report = JsonSerializer.Serialize(new { ok = true, nativeKeyring = true, encryptedAccount = true,
                removed = true, runtime = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier });
            File.WriteAllText(Path.Combine(root, "credential-probe.json"), report);
            Console.WriteLine(report);
            return 0;
        }
        finally { vault.Remove(); }
    }
}
