using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace HelL.Remote;

public sealed class InvitationFeedClient(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient http = http ?? throw new ArgumentNullException(nameof(http));

    public async Task<InvitationFeedUpdate?> ReadAsync(RemoteHostInvite current, long afterRevision, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(current);
        InvitationFeedDescriptor descriptor = current.Feed ?? throw new InvalidOperationException("This invitation does not include automatic updates.");
        InvitationFeedCodec.Validate(descriptor);
        if (afterRevision < 0) throw new ArgumentOutOfRangeException(nameof(afterRevision));
        Uri endpoint = new(InvitationFeedCodec.Relay(current.Relay), "v1/invitations/" + descriptor.Channel);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", descriptor.ReaderToken);
        if (afterRevision > 0) request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue("\"" + afterRevision + "\""));
        using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            if (afterRevision == 0) throw new InvalidDataException("The first invitation update cannot be empty.");
            return null;
        }
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri is { } final && final != endpoint)
            throw new InvalidDataException("Invitation updates cannot redirect to another address.");
        byte[] bytes = await ReadBoundedAsync(response.Content, timeout.Token).ConfigureAwait(false);
        InvitationFeedEnvelope envelope;
        try { envelope = JsonSerializer.Deserialize<InvitationFeedEnvelope>(bytes, Json) ?? throw new JsonException(); }
        catch (JsonException) { throw new InvalidDataException("The invitation update response is invalid."); }
        return new(envelope.Revision, InvitationFeedCodec.Unprotect(envelope, current, afterRevision));
    }

    public async IAsyncEnumerable<InvitationFeedUpdate> WatchAsync(RemoteHostInvite current,
        [EnumeratorCancellation] CancellationToken cancellation, TimeSpan? pollInterval = null, long afterRevision = 0)
    {
        TimeSpan interval = pollInterval ?? TimeSpan.FromSeconds(5);
        if (interval < TimeSpan.FromMilliseconds(100) || interval > TimeSpan.FromMinutes(5)) throw new ArgumentOutOfRangeException(nameof(pollInterval));
        if (afterRevision < 0) throw new ArgumentOutOfRangeException(nameof(afterRevision));
        long revision = afterRevision;
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            InvitationFeedUpdate? update = await ReadAsync(current, revision, cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            if (update is not null) { revision = update.Revision; current = update.Invitation; yield return update; }
            await Task.Delay(interval, cancellation).ConfigureAwait(false);
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, CancellationToken cancellation)
    {
        const int limit = 32768;
        if (content.Headers.ContentLength > limit) throw new InvalidDataException("The invitation update response is too large.");
        await using Stream stream = await content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
        using var bytes = new MemoryStream();
        byte[] buffer = new byte[4096];
        int count;
        while ((count = await stream.ReadAsync(buffer, cancellation).ConfigureAwait(false)) != 0)
        {
            if (bytes.Length + count > limit) throw new InvalidDataException("The invitation update response is too large.");
            bytes.Write(buffer, 0, count);
        }
        return bytes.ToArray();
    }
}
