using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
using HelL.Contracts;

namespace HelL.Remote;

public sealed class PairedRemoteClient(RemotePairingStore pairings)
{
    private readonly RemotePairingStore pairings = pairings ?? throw new ArgumentNullException(nameof(pairings));

    public async Task<HostReply> ExecuteAsync(RemoteHostInvite invite, HostRequest request, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(invite);
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        pairings.Refresh();
        PairedDevice? host = pairings.Find(invite.Host.DeviceId);
        if (host is null || host.Fingerprint != invite.Host.Fingerprint || invite.ClientDeviceId != pairings.Offer.DeviceId)
            throw new UnauthorizedAccessException("Confirm both device fingerprints before using this remote invitation.");
        if (invite.Expires <= DateTimeOffset.UtcNow.ToUnixTimeSeconds() || invite.Room is null || invite.Room.Length != 48
            || invite.Room.Any(ch => !Uri.IsHexDigit(ch))) throw new InvalidDataException("The remote invitation is expired or invalid.");
        using X509Certificate2 identity = pairings.OpenIdentity();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return await RemoteHostChannel.ExecuteAsync(new Uri(invite.Relay), invite.ClientTicket,
                    identity, host.Fingerprint, request, timeout.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < 2 && !timeout.IsCancellationRequested && ex is IOException or WebSocketException)
            {
                // Reuse the request ID so a lost cancellation receipt cannot repeat a host operation.
                await Task.Delay(TimeSpan.FromMilliseconds(500 * (attempt + 1)), timeout.Token).ConfigureAwait(false);
            }
        }
    }

    public Task<HostReply> SnapshotAsync(RemoteHostInvite invite, CancellationToken cancellation) => ExecuteAsync(invite,
        new HostRequest { RequestId = Guid.NewGuid().ToString("N"), Command = HostCommand.Snapshot }, cancellation);
}
