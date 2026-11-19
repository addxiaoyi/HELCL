using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Android.Content;

namespace HelL.Android;

internal static class CredentialSmoke
{
    private const string Service = "HelL isolated credential smoke";
    private const string Username = "ui-smoke-persistent-key";

    public static void Save(Context context, string evidenceRoot)
    {
        var store = new AndroidSecretStore(context);
        string fixture = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        try
        {
            store.SetPassword(Service, Username, fixture);
            if (store.GetPassword(Service, Username) != fixture) throw new IOException("Android credential roundtrip failed.");
            using var preferences = context.GetSharedPreferences("hell-account-keys", FileCreationMode.Private)
                ?? throw new IOException("Android credential preferences unavailable.");
            if (preferences.All?.Values.Any(value => value?.ToString()?.Contains(fixture, StringComparison.Ordinal) == true) == true)
                throw new IOException("Android credential preferences contain plaintext.");
            Directory.CreateDirectory(evidenceRoot);
            File.WriteAllText(Path.Combine(evidenceRoot, "mobile-credentials.json"), JsonSerializer.Serialize(new
            {
                ok = true, encrypted = true,
                sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fixture)))
            }));
        }
        catch
        {
            store.DeletePassword(Service, Username);
            throw;
        }
    }

    public static void Restore(Context context, string evidenceRoot)
    {
        using var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(evidenceRoot, "mobile-credentials.json")));
        var store = new AndroidSecretStore(context);
        string fixture = store.GetPassword(Service, Username);
        string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fixture)));
        if (fixture.Length == 0 || digest != expected.RootElement.GetProperty("sha256").GetString())
            throw new IOException("Android credential did not survive process restart.");
        store.DeletePassword(Service, Username);
        if (store.GetPassword(Service, Username).Length != 0) throw new IOException("Android credential removal failed.");
        File.WriteAllText(Path.Combine(evidenceRoot, "mobile-credentials-restart.json"), "{\"ok\":true,\"restored\":true,\"removed\":true}");
    }
}
