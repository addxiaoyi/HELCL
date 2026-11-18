using System.Security.Cryptography;
using HelL.Contracts;
using HelL.Remote;
using Xunit;

namespace HelL.Remote.Tests;

public sealed class RemotePairingTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "hell-pairing-" + Guid.NewGuid().ToString("N"));
    private readonly Secrets secrets = new();

    [Fact]
    public void IdentityAndExplicitTrustSurviveRestartWithoutPlaintextSecrets()
    {
        var store = new RemotePairingStore(root, "desktop", secrets);
        using var client = DeviceTunnel.CreateIdentity("phone");
        var offer = new RemotePairingOffer(Guid.NewGuid().ToString("N"), "phone", DeviceTunnel.Fingerprint(client));
        Assert.Null(store.Find(offer.DeviceId));
        store.Trust(offer, [HostPermission.Read]);
        var reopened = new RemotePairingStore(root, "desktop", secrets);
        Assert.Equal(store.Offer, reopened.Offer);
        Assert.Equal(offer.Fingerprint, reopened.Find(offer.DeviceId)!.Fingerprint);
        Assert.Equal([HostPermission.Read], reopened.Find(offer.DeviceId)!.Permissions);
        string disk = File.ReadAllText(Path.Combine(root, "remote-pairing.json"));
        Assert.DoesNotContain(offer.Fingerprint, disk);
        Assert.DoesNotContain(secrets.Keys.Values.Single(), disk);
        using var identity = reopened.OpenIdentity();
        Assert.True(identity.HasPrivateKey);
        Assert.Equal(store.Offer.Fingerprint, DeviceTunnel.Fingerprint(identity));
    }

    [Fact]
    public void FailedTrustCommitKeepsThePreviousIdentityAndPermissions()
    {
        var store = new RemotePairingStore(root, "desktop", secrets);
        using var client = DeviceTunnel.CreateIdentity("phone");
        var offer = new RemotePairingOffer(Guid.NewGuid().ToString("N"), "phone", DeviceTunnel.Fingerprint(client));
        store.Trust(offer, [HostPermission.Read]);
        secrets.FailWrites = true;
        Assert.Throws<IOException>(() => store.Trust(offer, [HostPermission.Read, HostPermission.CancelTasks]));
        Assert.Equal([HostPermission.Read], store.Find(offer.DeviceId)!.Permissions);
        Assert.Single(secrets.Keys);
    }

    [Fact]
    public void RevocationPersistsAndReturnedPermissionsCannotModifyTrust()
    {
        var store = new RemotePairingStore(root, "desktop", secrets);
        using var client = DeviceTunnel.CreateIdentity("phone");
        var offer = new RemotePairingOffer(Guid.NewGuid().ToString("N"), "phone", DeviceTunnel.Fingerprint(client));
        store.Trust(offer, [HostPermission.Read]);
        store.Find(offer.DeviceId)!.Permissions[0] = HostPermission.CancelTasks;
        Assert.Equal([HostPermission.Read], store.Find(offer.DeviceId)!.Permissions);
        store.Revoke(offer.DeviceId);
        Assert.Null(new RemotePairingStore(root, "desktop", secrets).Find(offer.DeviceId));
    }

    [Fact]
    public void ReplacingAPinnedCertificateRequiresRevocationAndMalformedStateFailsClosed()
    {
        var store = new RemotePairingStore(root, "desktop", secrets);
        using var first = DeviceTunnel.CreateIdentity("first");
        using var second = DeviceTunnel.CreateIdentity("second");
        string id = Guid.NewGuid().ToString("N");
        store.Trust(new(id, "phone", DeviceTunnel.Fingerprint(first)), [HostPermission.Read]);
        Assert.Throws<InvalidOperationException>(() => store.Trust(new(id, "phone", DeviceTunnel.Fingerprint(second)), [HostPermission.Read]));
        string file = Path.Combine(root, "remote-pairing.json");
        File.WriteAllText(file, "{\"Schema\":1,\"KeyId\":\"not-an-id\"}");
        Assert.Throws<InvalidDataException>(() => new RemotePairingStore(root, "desktop", secrets));
    }

    [Fact]
    public void ASavedIdentityCannotBeOpenedWithoutItsSystemKey()
    {
        _ = new RemotePairingStore(root, "desktop", secrets);
        Assert.Throws<InvalidOperationException>(() => new RemotePairingStore(root, "desktop", new Secrets()));
    }

    private sealed class Secrets : IRemoteSecretStore
    {
        public Dictionary<string, string> Keys { get; } = [];
        public bool FailWrites { get; set; }
        public string Read(string key) => Keys.GetValueOrDefault(key, "");
        public void Write(string key, string value)
        {
            if (FailWrites) throw new IOException("The test key store is locked.");
            Keys[key] = value;
        }
        public void Delete(string key) => Keys.Remove(key);
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
