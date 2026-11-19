using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace HelL.Remote;

public sealed class InvitationFeedClient(HttpClient http)
{
    private const int MaxBackoffSteps = 6;
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MaxRateLimitDelay = TimeSpan.FromHours(1);
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
        if (IsTransient(response.StatusCode))
            throw new RetryableFeedException(response.StatusCode, RetryAfter(response.Headers.RetryAfter));
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
        int failures = 0;
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            InvitationFeedUpdate? update;
            TimeSpan? retryAfter = null;
            bool retry = false;
            try
            {
                update = await ReadAsync(current, revision, cancellation).ConfigureAwait(false);
                failures = 0;
            }
            catch (HttpRequestException error) when (error.StatusCode is null || IsTransient(error.StatusCode.Value))
            { retryAfter = (error as RetryableFeedException)?.RetryAfter; update = null; retry = true; }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            { update = null; retry = true; }
            catch (IOException)
            { update = null; retry = true; }
            cancellation.ThrowIfCancellationRequested();
            if (retry)
            {
                failures = Math.Min(failures + 1, MaxBackoffSteps);
                TimeSpan backoff = TimeSpan.FromMilliseconds(Math.Min(MaxBackoff.TotalMilliseconds, interval.TotalMilliseconds * (1 << (failures - 1))));
                if (retryAfter > backoff) backoff = retryAfter.Value;
                await Task.Delay(backoff, cancellation).ConfigureAwait(false);
                continue;
            }
            if (update is not null) { revision = update.Revision; current = update.Invitation; yield return update; }
            await Task.Delay(interval, cancellation).ConfigureAwait(false);
        }
    }

    private static bool IsTransient(HttpStatusCode status) => status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
        || (int)status is >= 500 and <= 599;

    private static TimeSpan? RetryAfter(RetryConditionHeaderValue? header)
    {
        TimeSpan? delay = header?.Delta ?? (header?.Date is { } date ? date - DateTimeOffset.UtcNow : null);
        if (delay is null || delay <= TimeSpan.Zero) return null;
        if (delay > MaxRateLimitDelay) throw new InvalidDataException("The invitation relay requested an unsupported retry delay.");
        return delay;
    }

    private sealed class RetryableFeedException(HttpStatusCode status, TimeSpan? retryAfter)
        : HttpRequestException("The invitation relay is temporarily unavailable.", null, status)
    {
        internal TimeSpan? RetryAfter { get; } = retryAfter;
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
