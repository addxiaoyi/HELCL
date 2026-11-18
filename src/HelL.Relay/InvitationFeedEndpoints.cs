using System.Text.Json;
using System.Text.Json.Serialization;
using HelL.Remote;

namespace HelL.Relay;

internal static class InvitationFeedEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 8, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    internal static void Map(WebApplication app, InvitationFeedStore feeds,
        Func<HttpContext, bool> isOperator, Func<HttpContext, string> bearer)
    {
        app.MapPut("/v1/invitations/{channel}", async (HttpContext context, string channel) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (!isOperator(context)) return Results.Unauthorized();
            if (!InvitationFeedStore.ValidChannel(channel)) return Results.BadRequest();
            try
            {
                InvitationFeedWrite? write = await ReadBounded(context.Request, context.RequestAborted).ConfigureAwait(false);
                if (write is null) return Results.BadRequest();
                feeds.Publish(channel, write);
                return Results.NoContent();
            }
            catch (RelayAdmissionException ex) { return Results.StatusCode(ex.StatusCode); }
            catch (JsonException) { return Results.BadRequest(); }
        }).RequireRateLimiting("entry");
        app.MapGet("/v1/invitations/{channel}", (HttpContext context, string channel) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            InvitationFeedEnvelope? envelope = feeds.Read(channel, bearer(context));
            if (envelope is null) return Results.Unauthorized();
            string etag = "\"" + envelope.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\"";
            context.Response.Headers.ETag = etag;
            return context.Request.Headers.IfNoneMatch.ToString() == etag
                ? Results.StatusCode(StatusCodes.Status304NotModified) : Results.Ok(envelope);
        }).RequireRateLimiting("entry");
        app.MapDelete("/v1/invitations/{channel}", (HttpContext context, string channel) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (!isOperator(context)) return Results.Unauthorized();
            if (!InvitationFeedStore.ValidChannel(channel)) return Results.BadRequest();
            return feeds.Revoke(channel) ? Results.NoContent() : Results.NotFound();
        }).RequireRateLimiting("entry");
    }

    private static async Task<InvitationFeedWrite?> ReadBounded(HttpRequest request, CancellationToken cancellation)
    {
        if (request.ContentLength > InvitationFeedStore.MaxPayloadBytes)
            throw new RelayAdmissionException(413, "The invitation body exceeds its size limit.");
        byte[] body = new byte[InvitationFeedStore.MaxPayloadBytes + 1];
        int received = 0;
        while (received < body.Length)
        {
            int count = await request.Body.ReadAsync(body.AsMemory(received, body.Length - received), cancellation).ConfigureAwait(false);
            if (count == 0) break;
            received += count;
        }
        if (received > InvitationFeedStore.MaxPayloadBytes)
            throw new RelayAdmissionException(413, "The invitation body exceeds its size limit.");
        return JsonSerializer.Deserialize<InvitationFeedWrite>(body.AsSpan(0, received), Json);
    }
}
