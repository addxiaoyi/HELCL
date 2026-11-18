using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HelL.Remote;

public sealed class InvitationFeedPublisher
{
    private sealed record Saved(InvitationFeedDescriptor Descriptor, long Revision, string Relay, string Host, string Client, bool Revoked = false);
    private readonly HttpClient http;
    private readonly Uri relay;
    private readonly string operatorToken;
    private readonly IRemoteSecretStore secrets;
    private readonly string storageKey;
    private readonly SemaphoreSlim writes = new(1, 1);
    private Saved state;
    private bool revoked;
    public InvitationFeedDescriptor Descriptor => state.Descriptor;

    public InvitationFeedPublisher(HttpClient http, Uri relay, string operatorToken, IRemoteSecretStore secrets,
        string hostDeviceId, string clientDeviceId)
    {
        this.http = http ?? throw new ArgumentNullException(nameof(http));
        this.relay = InvitationFeedCodec.Relay((relay ?? throw new ArgumentNullException(nameof(relay))).AbsoluteUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorToken);
        this.operatorToken = operatorToken;
        this.secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
        if (!Guid.TryParseExact(hostDeviceId, "N", out _) || !Guid.TryParseExact(clientDeviceId, "N", out _))
            throw new ArgumentException("Explicit host and client device identifiers are required.");
        string origin = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(relay.AbsoluteUri)))[..16];
        storageKey = "invitation-feed:" + hostDeviceId + ":" + clientDeviceId + ":" + origin;
        string saved = secrets.Read(storageKey);
        if (saved.Length > 4096) throw Corrupt();
        if (saved.Length != 0)
        {
            try { state = JsonSerializer.Deserialize<Saved>(saved) ?? throw Corrupt(); }
            catch (JsonException) { throw Corrupt(); }
            if (state.Descriptor is null || state.Revision < 0 || state.Relay != relay.AbsoluteUri
                || state.Host != hostDeviceId || state.Client != clientDeviceId) throw Corrupt();
            if (!state.Revoked && state.Descriptor.Expires > DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            { InvitationFeedCodec.Validate(state.Descriptor); return; }
        }
        state = new(InvitationFeedCodec.Create(DateTimeOffset.UtcNow.AddDays(7)), 0, relay.AbsoluteUri, hostDeviceId, clientDeviceId);
        Save(state);
    }

    public async Task<RemoteHostInvite> PublishAsync(RemoteHostInvite invitation, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(invitation);
        await writes.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            if (revoked) throw new InvalidOperationException("This invitation subscription was revoked.");
            if (invitation.Host is null || invitation.Host.DeviceId != state.Host || invitation.ClientDeviceId != state.Client
                || InvitationFeedCodec.Relay(invitation.Relay) != relay) throw new UnauthorizedAccessException("The invitation belongs to a different device pairing.");
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            budget.CancelAfter(TimeSpan.FromSeconds(15));
            long revision = checked(state.Revision + 1);
            InvitationFeedEnvelope encrypted = InvitationFeedCodec.Protect(invitation, state.Descriptor, revision);
            Saved next = state with { Revision = revision };
            // Reserving the revision before transmission permits gaps but prevents reuse after a lost HTTP receipt.
            Save(next);
            state = next;
            string readerHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state.Descriptor.ReaderToken)));
            using var request = new HttpRequestMessage(HttpMethod.Put, new Uri(relay, "v1/invitations/" + state.Descriptor.Channel));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", operatorToken);
            request.Content = JsonContent.Create(new InvitationFeedWrite(readerHash, state.Descriptor.Expires, encrypted), options: new JsonSerializerOptions());
            using HttpResponseMessage response = await http.SendAsync(request, budget.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.StatusCode != HttpStatusCode.NoContent) throw new InvalidDataException("The relay did not confirm the encrypted invitation update.");
            return invitation with { Feed = state.Descriptor };
        }
        finally { writes.Release(); }
    }

    public async Task RevokeAsync(CancellationToken cancellation)
    {
        await writes.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            revoked = true;
            var failures = new List<Exception>();
            if (!state.Revoked)
            {
                Saved retired = state with { Revoked = true };
                try { Save(retired); state = retired; }
                catch (Exception error) when (error is not OutOfMemoryException)
                { failures.Add(new IOException("The key store could not record the invitation revocation (" + error.GetType().Name + ").")); }
            }
            try
            {
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                budget.CancelAfter(TimeSpan.FromSeconds(15));
                using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri(relay, "v1/invitations/" + state.Descriptor.Channel));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", operatorToken);
                using HttpResponseMessage response = await http.SendAsync(request, budget.Token).ConfigureAwait(false);
                if (response.StatusCode != HttpStatusCode.NotFound) response.EnsureSuccessStatusCode();
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            { failures.Add(new IOException("The relay could not confirm the invitation revocation (" + error.GetType().Name + ").")); }
            try { secrets.Delete(storageKey); }
            catch (Exception error) when (error is not OutOfMemoryException)
            { failures.Add(new IOException("The retired invitation key could not be removed (" + error.GetType().Name + ").")); }
            if (failures.Count != 0) throw new IOException("Invitation revocation needs cleanup. This publisher has stopped sending updates.", new AggregateException(failures));
        }
        finally { writes.Release(); }
    }

    private static InvalidDataException Corrupt() => new("The saved invitation subscription is invalid. Restore its key-store entry before publishing updates.");

    private void Save(Saved next)
    {
        string encoded = JsonSerializer.Serialize(next);
        secrets.Write(storageKey, encoded);
        if (secrets.Read(storageKey) != encoded) throw new IOException("The system key store did not retain the invitation subscription.");
    }
}
