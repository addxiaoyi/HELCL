using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using HelL.Contracts;

namespace HelL.Remote;

public interface IRemoteSecretStore
{
    string Read(string key);
    void Write(string key, string value);
    void Delete(string key);
}

public sealed record RemotePairingOffer(string DeviceId, string Name, string Fingerprint);
public sealed record PairedDevice(string DeviceId, string Name, string Fingerprint, HostPermission[] Permissions);

public sealed class RemotePairingStore
{
    private const int MaxDevices = 16;
    private const int MaxFileBytes = 1024 * 1024;
    private const int MaxSnapshotReloads = 2;
    private static readonly byte[] Context = Encoding.UTF8.GetBytes("HelL device pairing v1");
    private readonly object gate = new();
    private readonly string path;
    private readonly IRemoteSecretStore secrets;
    private State state;
    private string keyId = "";
    private sealed record State(string DeviceId, string Name, string Identity, PairedDevice[] Devices);
    private sealed record Envelope(int Schema, string KeyId, string Nonce, string Ciphertext, string Tag);

    public event Action<string>? Changed;
    public string CleanupWarning { get; private set; } = "";
    public RemotePairingOffer Offer
    {
        get
        {
            lock (gate)
            {
                using X509Certificate2 identity = Open(state.Identity);
                return new(state.DeviceId, state.Name, DeviceTunnel.Fingerprint(identity));
            }
        }
    }

    public RemotePairingStore(string root, string name, IRemoteSecretStore secrets)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ValidateName(name);
        this.secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
        path = Path.Combine(Path.GetFullPath(root), "remote-pairing.json");
        if (File.Exists(path)) { state = Load(); return; }
        using X509Certificate2 identity = DeviceTunnel.CreateIdentity(name);
        state = new(Guid.NewGuid().ToString("N"), name, Convert.ToBase64String(identity.Export(X509ContentType.Pkcs12)), []);
        Commit(state);
    }

    public X509Certificate2 OpenIdentity() { lock (gate) return Open(state.Identity); }
    public void Refresh()
    {
        string[] changed;
        lock (gate)
        {
            string previousKey = keyId;
            State loaded = Load();
            if (loaded.DeviceId != state.DeviceId || loaded.Identity != state.Identity)
            {
                keyId = previousKey;
                throw new InvalidOperationException("This device identity changed. Stop the remote host before loading another identity.");
            }
            changed = state.Devices.Where(previous => !loaded.Devices.Any(device => device.DeviceId == previous.DeviceId
                && device.Fingerprint == previous.Fingerprint && device.Permissions.SequenceEqual(previous.Permissions)))
                .Select(device => device.DeviceId).ToArray();
            state = loaded;
        }
        foreach (string id in changed) Changed?.Invoke(id);
    }
    public PairedDevice[] Devices() { lock (gate) return state.Devices.Select(Copy).ToArray(); }
    public PairedDevice? Find(string deviceId)
    {
        ValidateId(deviceId);
        lock (gate)
        {
            PairedDevice? device = state.Devices.SingleOrDefault(peer => peer.DeviceId == deviceId);
            return device is null ? null : Copy(device);
        }
    }

    public void Trust(RemotePairingOffer offer, IReadOnlyCollection<HostPermission> permissions)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ValidateOffer(offer);
        ArgumentNullException.ThrowIfNull(permissions);
        HostPermission[] allowed = permissions.Distinct().Order().ToArray();
        if (!allowed.Contains(HostPermission.Read) || allowed.Any(permission => !Enum.IsDefined(permission)))
            throw new ArgumentException("A paired device requires explicit valid host permissions.", nameof(permissions));
        lock (gate)
        {
            if (offer.DeviceId == state.DeviceId) throw new ArgumentException("A device cannot pair with itself.", nameof(offer));
            PairedDevice? existing = state.Devices.SingleOrDefault(device => device.DeviceId == offer.DeviceId);
            string pin = offer.Fingerprint.ToUpperInvariant();
            if (existing is not null && existing.Fingerprint != pin)
                throw new InvalidOperationException("Revoke the previous device before trusting a different certificate.");
            if (existing is null && state.Devices.Length >= MaxDevices)
                throw new InvalidOperationException("Remove an unused paired device before adding another.");
            PairedDevice[] peers = state.Devices.Where(device => device.DeviceId != offer.DeviceId)
                .Append(new PairedDevice(offer.DeviceId, offer.Name, pin, allowed)).ToArray();
            Commit(state with { Devices = peers });
        }
        Changed?.Invoke(offer.DeviceId);
    }

    public void Revoke(string deviceId)
    {
        ValidateId(deviceId);
        lock (gate) Commit(state with { Devices = state.Devices.Where(device => device.DeviceId != deviceId).ToArray() });
        Changed?.Invoke(deviceId);
    }

    private State Load(int reloads = 0)
    {
        byte[]? key = null;
        byte[]? plain = null;
        try
        {
            Envelope envelope = ReadEnvelope();
            ValidateId(envelope.KeyId);
            if (envelope.Schema != 1) throw Corrupt();
            string storedKey = secrets.Read(envelope.KeyId);
            if (string.IsNullOrEmpty(storedKey))
            {
                if (reloads < MaxSnapshotReloads && ReadEnvelope().KeyId != envelope.KeyId) return Load(reloads + 1);
                throw new InvalidOperationException("The system key for this device is unavailable. Restore the key store or pair this device again.");
            }
            key = Convert.FromBase64String(storedKey);
            byte[] nonce = Convert.FromBase64String(envelope.Nonce);
            byte[] tag = Convert.FromBase64String(envelope.Tag);
            byte[] cipher = Convert.FromBase64String(envelope.Ciphertext);
            if (key.Length != 32 || nonce.Length != 12 || tag.Length != 16 || cipher.Length == 0) throw Corrupt();
            plain = new byte[cipher.Length];
            using (var aes = new AesGcm(key, 16)) aes.Decrypt(nonce, cipher, tag, plain, Context);
            State loaded = JsonSerializer.Deserialize<State>(plain) ?? throw Corrupt();
            ValidateId(loaded.DeviceId);
            ValidateName(loaded.Name);
            if (loaded.Devices is null || loaded.Devices.Length > MaxDevices || loaded.Devices.Any(device => device is null)) throw Corrupt();
            using X509Certificate2 identity = Open(loaded.Identity);
            if (!identity.HasPrivateKey || loaded.Devices.Select(device => device.DeviceId).Distinct().Count() != loaded.Devices.Length) throw Corrupt();
            foreach (PairedDevice device in loaded.Devices)
            {
                ValidateOffer(new(device.DeviceId, device.Name, device.Fingerprint));
                if (device.DeviceId == loaded.DeviceId || device.Permissions is null || !device.Permissions.Contains(HostPermission.Read)
                    || device.Permissions.Any(permission => !Enum.IsDefined(permission))) throw Corrupt();
            }
            keyId = envelope.KeyId;
            return loaded;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or FormatException or CryptographicException)
        { throw Corrupt(); }
        finally
        {
            if (key is not null) CryptographicOperations.ZeroMemory(key);
            if (plain is not null) CryptographicOperations.ZeroMemory(plain);
        }
    }

    private Envelope ReadEnvelope()
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (file.Length is < 1 or > MaxFileBytes) throw Corrupt();
        return JsonSerializer.Deserialize<Envelope>(file) ?? throw Corrupt();
    }

    private void Commit(State next)
    {
        string nextId = Guid.NewGuid().ToString("N");
        string temporary = path + "." + nextId + ".tmp";
        byte[] key = RandomNumberGenerator.GetBytes(32);
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(next);
        byte[] cipher = new byte[plain.Length];
        byte[] tag = new byte[16];
        bool keyWritten = false;
        bool committed = false;
        try
        {
            if (plain.Length > MaxFileBytes / 2) throw new InvalidOperationException("The pairing state is too large to save.");
            using (var aes = new AesGcm(key, 16)) aes.Encrypt(nonce, plain, cipher, tag, Context);
            string keyText = Convert.ToBase64String(key);
            secrets.Write(nextId, keyText);
            keyWritten = true;
            if (secrets.Read(nextId) != keyText) throw new IOException("The system key store did not retain this device key.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var lease = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            if (File.Exists(path))
            {
                using var existingFile = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                Envelope existing = JsonSerializer.Deserialize<Envelope>(existingFile) ?? throw Corrupt();
                if (existing.KeyId != keyId) throw new IOException("Device pairings changed in another session. Reload them before saving.");
            }
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                JsonSerializer.Serialize(file, new Envelope(1, nextId, Convert.ToBase64String(nonce), Convert.ToBase64String(cipher), Convert.ToBase64String(tag)));
                file.Flush(true);
            }
            File.Move(temporary, path, true);
            committed = true;
            string previous = keyId;
            keyId = nextId;
            state = next;
            if (previous.Length != 0) DeleteKey(previous);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plain);
            if (keyWritten && !committed) DeleteKey(nextId);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private void DeleteKey(string id)
    {
        try { secrets.Delete(id); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { CleanupWarning = "A retired device encryption key could not be removed from the system key store."; }
    }

    private static X509Certificate2 Open(string encoded)
    {
        byte[] bytes = Convert.FromBase64String(encoded);
        try
        {
            return DeviceTunnel.LoadIdentity(bytes);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static PairedDevice Copy(PairedDevice device) => device with { Permissions = (HostPermission[])device.Permissions.Clone() };
    private static InvalidDataException Corrupt() => new("The saved device pairing is invalid or damaged. Restore its backup or pair this device again.");
    private static void ValidateId(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("A device identifier in UUID N format is required.", nameof(id));
    }
    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80 || name.Any(char.IsControl))
            throw new ArgumentException("A device name of up to 80 visible characters is required.", nameof(name));
    }
    private static void ValidateOffer(RemotePairingOffer offer)
    {
        ValidateId(offer.DeviceId);
        ValidateName(offer.Name);
        if (offer.Fingerprint is null || offer.Fingerprint.Length != 64 || offer.Fingerprint.Any(ch => !Uri.IsHexDigit(ch)))
            throw new ArgumentException("A SHA-256 device fingerprint is required.", nameof(offer));
    }
}
