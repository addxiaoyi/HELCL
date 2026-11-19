using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HelL.Contracts;
using HelL.Remote;
using Xunit;

namespace HelL.Remote.Tests;

public sealed class RemoteInvitationPersistenceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "hell-invitation-store-" + Guid.NewGuid().ToString("N"));
    private readonly Secrets secrets = new();
    private readonly RemotePairingStore store;
    private readonly RemotePairingOffer host;

    public RemoteInvitationPersistenceTests()
    {
        store = new RemotePairingStore(root, "phone", secrets);
        using var identity = DeviceTunnel.CreateIdentity("host");
        host = new(Guid.NewGuid().ToString("N"), "host", DeviceTunnel.Fingerprint(identity));
        store.Trust(host, [HostPermission.Read]);
    }

    [Fact]
    public void OriginalSchemaWithoutInvitationFieldsRemainsReadableAndUpgradesOnNextWrite()
    {
        Rewrite(plain =>
        {
            plain.Remove("Invitations"); plain.Remove("Activity"); plain.Remove("InvitationRevisions"); plain.Remove("SelectedHostId");
        });
        var reopened = new RemotePairingStore(root, "phone", secrets);
        Assert.Empty(reopened.Invitations());
        Assert.Equal(store.Offer, reopened.Offer);
        reopened.SaveInvitation(Invite());
        Assert.Single(new RemotePairingStore(root, "phone", secrets).Invitations());
    }

    [Fact]
    public void AKeyStoreWriteWithLostReceiptIsCompensatedWithoutChangingTheInvitation()
    {
        store.SaveInvitation(Invite());
        byte[] original = File.ReadAllBytes(Path.Combine(root, "remote-pairing.json"));
        secrets.LoseWriteReceipt = true;
        Assert.Throws<IOException>(() => store.SaveInvitation(Invite() with { Room = new string('d', 48) }));
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(root, "remote-pairing.json")));
        Assert.Single(secrets.Keys);
        Assert.Equal(new string('a', 48), Assert.Single(store.Invitations()).Room);
        Assert.Equal(new string('a', 48), Assert.Single(new RemotePairingStore(root, "phone", secrets).Invitations()).Room);
    }

    [Fact]
    public void AStaleWriterCannotRestoreAnInvitationAfterRevocation()
    {
        store.SaveInvitation(Invite());
        var stale = new RemotePairingStore(root, "phone", secrets);
        store.Revoke(host.DeviceId);
        Assert.Throws<IOException>(() => stale.SaveInvitation(Invite()));
        Assert.Empty(new RemotePairingStore(root, "phone", secrets).Invitations());
    }

    [Theory]
    [InlineData("Invitations", "[null]")]
    [InlineData("Activity", "[null]")]
    [InlineData("InvitationRevisions", "[null]")]
    [InlineData("SelectedHostId", "null")]
    public void InvalidEncryptedStateFailsClosedWithoutOverwritingTheFile(string field, string encoded)
    {
        Rewrite(plain => plain[field] = JsonNode.Parse(encoded));
        byte[] original = File.ReadAllBytes(Path.Combine(root, "remote-pairing.json"));
        Assert.Throws<InvalidDataException>(() => new RemotePairingStore(root, "phone", secrets));
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(root, "remote-pairing.json")));
    }

    private RemoteHostInvite Invite() => new(host, store.Offer.DeviceId, "https://relay.example/", new string('a', 48), "private-ticket", DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds());
    private void Rewrite(Action<JsonObject> edit)
    {
        string file = Path.Combine(root, "remote-pairing.json");
        JsonObject envelope = JsonNode.Parse(File.ReadAllBytes(file))!.AsObject();
        byte[] key = Convert.FromBase64String(secrets.Read(envelope["KeyId"]!.GetValue<string>()));
        byte[] cipher = Convert.FromBase64String(envelope["Ciphertext"]!.GetValue<string>());
        byte[] plain = new byte[cipher.Length];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(Convert.FromBase64String(envelope["Nonce"]!.GetValue<string>()), cipher,
            Convert.FromBase64String(envelope["Tag"]!.GetValue<string>()), plain, Encoding.UTF8.GetBytes("HelL device pairing v1"));
        JsonObject decoded = JsonNode.Parse(plain)!.AsObject();
        edit(decoded);
        byte[] replaced = JsonSerializer.SerializeToUtf8Bytes(decoded);
        byte[] next = new byte[replaced.Length];
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] tag = new byte[16];
        aes.Encrypt(nonce, replaced, next, tag, Encoding.UTF8.GetBytes("HelL device pairing v1"));
        envelope["Ciphertext"] = Convert.ToBase64String(next); envelope["Nonce"] = Convert.ToBase64String(nonce); envelope["Tag"] = Convert.ToBase64String(tag);
        File.WriteAllText(file, envelope.ToJsonString());
        CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plain); CryptographicOperations.ZeroMemory(replaced);
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    private sealed class Secrets : IRemoteSecretStore
    {
        public Dictionary<string, string> Keys { get; } = [];
        public bool LoseWriteReceipt { get; set; }
        public string Read(string key) => Keys.GetValueOrDefault(key, "");
        public void Write(string key, string value) { Keys[key] = value; if (LoseWriteReceipt) throw new IOException("receipt lost"); }
        public void Delete(string key) => Keys.Remove(key);
    }
}
