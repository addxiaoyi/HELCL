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
public sealed record PairedDeviceActivity(string DeviceId, long LastConnected);
public sealed record PairedInvitationRevision(string DeviceId, string Channel, long Revision);

public sealed class RemotePairingStore
{
    private const int MaxDevices = 16;
    private const int MaxFileBytes = 1024 * 1024;
    private const int MaxSnapshotReloads = 2;
    private const int ActivityWriteIntervalSeconds = 60;
    private static readonly byte[] Context = Encoding.UTF8.GetBytes("HelL device pairing v1");
    private readonly object gate = new();
    private readonly string path;
    private readonly IRemoteSecretStore secrets;
    private State state;
    private string keyId = "";
    private sealed record State(string DeviceId, string Name, string Identity, PairedDevice[] Devices)
    {
        public RemoteHostInvite[] Invitations { get; init; } = [];
        public PairedDeviceActivity[] Activity { get; init; } = [];
        public PairedInvitationRevision[] InvitationRevisions { get; init; } = [];
        public string SelectedHostId { get; init; } = "";
    }
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

    public RemoteHostInvite[] Invitations()
    {
        lock (gate) return state.Invitations.Select(invite => invite with { }).ToArray();
    }
    public string SelectedHostId { get { lock (gate) return state.SelectedHostId; } }
    public long InvitationRevision(string deviceId)
    {
        ValidateId(deviceId);
        lock (gate) return state.InvitationRevisions.SingleOrDefault(item => item.DeviceId == deviceId)?.Revision ?? 0;
    }
    public void SelectHost(string deviceId)
    {
        ValidateId(deviceId);
        lock (gate)
        {
            if (!state.Devices.Any(device => device.DeviceId == deviceId)) throw new UnauthorizedAccessException("The selected host is not paired.");
            if (state.SelectedHostId == deviceId) return;
            Commit(state with { SelectedHostId = deviceId });
        }
    }

    public long? LastConnected(string deviceId)
    {
        ValidateId(deviceId);
        lock (gate) return state.Activity.SingleOrDefault(item => item.DeviceId == deviceId)?.LastConnected;
    }

    public void RecordConnection(string deviceId)
    {
        ValidateId(deviceId);
        lock (gate)
        {
            if (!state.Devices.Any(device => device.DeviceId == deviceId))
                throw new UnauthorizedAccessException("Only a paired device can have connection activity recorded.");
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            PairedDeviceActivity? previous = state.Activity.SingleOrDefault(item => item.DeviceId == deviceId);
            // Live polling must not rotate the native encryption key for every successful snapshot.
            if (previous is not null && now >= previous.LastConnected && now - previous.LastConnected < ActivityWriteIntervalSeconds) return;
            PairedDeviceActivity[] next = state.Activity.Where(item => item.DeviceId != deviceId)
                .Append(new PairedDeviceActivity(deviceId, now)).ToArray();
            Commit(state with { Activity = next });
        }
    }

    public void SaveInvitation(RemoteHostInvite invitation, long revision = 0)
    {
        ValidateStoredInvitation(invitation);
        if (revision < 0) throw new ArgumentOutOfRangeException(nameof(revision));
        lock (gate)
        {
            PairedDevice? host = state.Devices.SingleOrDefault(device => device.DeviceId == invitation.Host.DeviceId);
            if (host is null || host.Fingerprint != invitation.Host.Fingerprint.ToUpperInvariant()
                || invitation.ClientDeviceId != state.DeviceId)
                throw new UnauthorizedAccessException("The invitation does not belong to a currently paired host.");
            RemoteHostInvite[] next = state.Invitations.Where(saved => saved.Host.DeviceId != host.DeviceId)
                .Append(invitation with { }).ToArray();
            if (next.Length > MaxDevices) throw new InvalidOperationException("Too many saved host invitations.");
            PairedInvitationRevision? previous = state.InvitationRevisions.SingleOrDefault(item => item.DeviceId == host.DeviceId);
            string channel = invitation.Feed?.Channel ?? "";
            if (previous?.Channel == channel && revision > 0 && revision <= previous.Revision)
                throw new InvalidDataException("The saved invitation revision cannot move backwards.");
            long nextRevision = revision == 0 && previous?.Channel == channel ? previous.Revision : revision;
            PairedInvitationRevision[] revisions = state.InvitationRevisions.Where(item => item.DeviceId != host.DeviceId)
                .Append(new PairedInvitationRevision(host.DeviceId, channel, nextRevision)).ToArray();
            Commit(state with { Invitations = next, InvitationRevisions = revisions, SelectedHostId = host.DeviceId });
        }
    }

    public void RemoveInvitation(string deviceId)
    {
        ValidateId(deviceId);
        lock (gate) Commit(state with
        {
            Invitations = state.Invitations.Where(invite => invite.Host.DeviceId != deviceId).ToArray(),
            InvitationRevisions = state.InvitationRevisions.Where(item => item.DeviceId != deviceId).ToArray()
        });
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
        lock (gate) Commit(state with
        {
            Devices = state.Devices.Where(device => device.DeviceId != deviceId).ToArray(),
            Invitations = state.Invitations.Where(invite => invite.Host.DeviceId != deviceId).ToArray(),
            Activity = state.Activity.Where(item => item.DeviceId != deviceId).ToArray(),
            InvitationRevisions = state.InvitationRevisions.Where(item => item.DeviceId != deviceId).ToArray(),
            SelectedHostId = state.SelectedHostId == deviceId ? "" : state.SelectedHostId
        });
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
            loaded = loaded with { Invitations = loaded.Invitations ?? [], Activity = loaded.Activity ?? [], InvitationRevisions = loaded.InvitationRevisions ?? [] };
            if (loaded.Devices is null || loaded.Devices.Length > MaxDevices || loaded.Devices.Any(device => device is null)
                || loaded.Invitations.Length > MaxDevices || loaded.Activity.Length > MaxDevices || loaded.InvitationRevisions.Length > MaxDevices
                || loaded.Invitations.Any(invite => invite?.Host is null) || loaded.Activity.Any(item => item is null)
                || loaded.InvitationRevisions.Any(item => item is null) || loaded.SelectedHostId is null) throw Corrupt();
            using X509Certificate2 identity = Open(loaded.Identity);
            if (!identity.HasPrivateKey || loaded.Devices.Select(device => device.DeviceId).Distinct().Count() != loaded.Devices.Length) throw Corrupt();
            foreach (PairedDevice device in loaded.Devices)
            {
                ValidateOffer(new(device.DeviceId, device.Name, device.Fingerprint));
                if (device.DeviceId == loaded.DeviceId || device.Permissions is null || !device.Permissions.Contains(HostPermission.Read)
                    || device.Permissions.Any(permission => !Enum.IsDefined(permission))) throw Corrupt();
            }
            if (loaded.Activity.Select(item => item.DeviceId).Distinct().Count() != loaded.Activity.Length
                || loaded.Activity.Any(item => !loaded.Devices.Any(device => device.DeviceId == item.DeviceId) || item.LastConnected < 0)) throw Corrupt();
            if (loaded.Invitations.Select(invite => invite.Host.DeviceId).Distinct().Count() != loaded.Invitations.Length) throw Corrupt();
            foreach (RemoteHostInvite invite in loaded.Invitations)
            {
                ValidateStoredInvitation(invite);
                if (invite.ClientDeviceId != loaded.DeviceId || !loaded.Devices.Any(device => device.DeviceId == invite.Host.DeviceId
                    && device.Fingerprint == invite.Host.Fingerprint.ToUpperInvariant())) throw Corrupt();
            }
            if (loaded.SelectedHostId.Length != 0 && !loaded.Devices.Any(device => device.DeviceId == loaded.SelectedHostId)) throw Corrupt();
            if (loaded.InvitationRevisions.Select(item => item.DeviceId).Distinct().Count() != loaded.InvitationRevisions.Length
                || loaded.InvitationRevisions.Any(item => item.Revision < 0 || !loaded.Invitations.Any(invite => invite.Host.DeviceId == item.DeviceId
                    && (invite.Feed?.Channel ?? "") == item.Channel))) throw Corrupt();
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
            keyWritten = true;
            secrets.Write(nextId, keyText);
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
    private static void ValidateStoredInvitation(RemoteHostInvite invite)
    {
        if (invite?.Host is null) throw Corrupt();
        ValidateOffer(invite.Host);
        ValidateId(invite.ClientDeviceId);
        if (invite.Room is null || invite.Room.Length != 48 || invite.Room.Any(ch => !Uri.IsHexDigit(ch))
            || string.IsNullOrWhiteSpace(invite.ClientTicket) || invite.ClientTicket.Length > 1024 || invite.ClientTicket.Any(char.IsControl)
            || invite.Expires <= 0 || invite.Expires > DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds()) throw Corrupt();
        _ = InvitationFeedCodec.Relay(invite.Relay);
        if (invite.Feed is { } feed)
        {
            if (feed.Channel is null || feed.Channel.Length != 48 || feed.Channel.Any(ch => !Uri.IsHexDigit(ch))
                || feed.ReaderToken is null || feed.ReaderToken.Length != 64 || feed.ReaderToken.Any(ch => !Uri.IsHexDigit(ch))
                || feed.Expires <= 0 || feed.Expires > DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds()) throw Corrupt();
            byte[] key = Convert.FromBase64String(feed.EncryptionKey);
            try { if (key.Length != 32) throw Corrupt(); }
            finally { CryptographicOperations.ZeroMemory(key); }
        }
    }
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
