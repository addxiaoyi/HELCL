using System.Net.WebSockets;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using HelL.Remote;
using Microsoft.AspNetCore.HttpOverrides;

namespace HelL.Relay;

public static class RelayApp
{
    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        configure?.Invoke(builder);
        string operatorToken = builder.Configuration["Relay:OperatorToken"] ?? "";
        if (Encoding.UTF8.GetByteCount(operatorToken) < 32)
            throw new InvalidOperationException("Relay:OperatorToken must contain at least 32 random bytes.");
        var tickets = new RelayTickets(builder.Configuration["Relay:SigningKey"] ?? "");
        byte[] operatorHash = SHA256.HashData(Encoding.UTF8.GetBytes(operatorToken));
        var limits = new RelayLimits();
        builder.Configuration.GetSection("Relay").Bind(limits);
        limits.Validate();
        var rooms = new RelayRooms(limits);
        var invitations = new InvitationFeedStore(limits);
        builder.Services.AddSingleton(rooms);
        string[] proxies = builder.Configuration.GetSection("Relay:KnownProxies").Get<string[]>() ?? [];
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (string address in proxies)
                options.KnownProxies.Add(IPAddress.TryParse(address, out IPAddress? parsed) ? parsed :
                    throw new InvalidOperationException("Relay:KnownProxies must contain literal trusted proxy IP addresses."));
        });
        bool IsOperator(HttpContext context) => CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(Bearer(context))), operatorHash);
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (rejected, _) =>
            {
                rejected.HttpContext.Response.Headers.RetryAfter = "60";
                rejected.HttpContext.Response.Headers.CacheControl = "no-store";
                return ValueTask.CompletedTask;
            };
            options.AddPolicy("entry", context =>
            {
                bool tunnel = context.Request.Path == "/v1/tunnel";
                bool invitationRead = HttpMethods.IsGet(context.Request.Method) && context.Request.Path.StartsWithSegments("/v1/invitations");
                string? channel = context.Request.RouteValues["channel"] as string;
                if (invitationRead && channel is not null && invitations.Read(channel, Bearer(context)) is not null)
                    return RateLimitPartition.GetFixedWindowLimiter("invitation:" + InvitationFeedStore.CanonicalChannel(channel),
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = limits.InvitationReadsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
                        });
                RelayTicket? claim = tunnel ? tickets.Validate(Bearer(context), DateTimeOffset.UtcNow) : null;
                if (claim is not null && rooms.IsAllowed(claim))
                    return RateLimitPartition.GetFixedWindowLimiter("ticket:" + claim.Room + ":" + claim.Role,
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = limits.ConnectionsPerWindow, Window = limits.ConnectionWindow, QueueLimit = 0
                        });
                bool authenticated = !tunnel && !invitationRead && IsOperator(context);
                string partition = authenticated ? "operator" : "anonymous:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
                return RateLimitPartition.GetFixedWindowLimiter(partition, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = authenticated ? limits.OperatorRequestsPerMinute : limits.AnonymousRequestsPerMinute,
                    Window = TimeSpan.FromMinutes(1), QueueLimit = 0
                });
            });
        });
        WebApplication app = builder.Build();
        if (proxies.Length > 0) app.UseForwardedHeaders();
        app.UseRateLimiter();
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
        app.MapGet("/health", () => Results.Ok(new { ok = true, protocol = 1 }));
        InvitationFeedEndpoints.Map(app, invitations, IsOperator, Bearer);
        app.MapPost("/v1/rooms", (HttpContext context) =>
        {
            if (!IsOperator(context)) return Results.Unauthorized();
            context.Response.Headers.CacheControl = "no-store";
            try
            {
                RelayRoom room = tickets.Issue(DateTimeOffset.UtcNow);
                rooms.Register(room);
                return Results.Ok(room);
            }
            catch (RelayAdmissionException ex) { return Results.StatusCode(ex.StatusCode); }
        }).RequireRateLimiting("entry");
        app.MapDelete("/v1/rooms/{room}", (HttpContext context, string room) =>
        {
            if (!IsOperator(context)) return Results.Unauthorized();
            context.Response.Headers.CacheControl = "no-store";
            return rooms.Revoke(room) ? Results.NoContent() : Results.NotFound();
        }).RequireRateLimiting("entry");
        app.MapPost("/v1/rooms/{room}/renew", (HttpContext context, string room) =>
        {
            if (!IsOperator(context)) return Results.Unauthorized();
            context.Response.Headers.CacheControl = "no-store";
            try { return Results.Ok(rooms.Renew(room, () => tickets.Issue(DateTimeOffset.UtcNow))); }
            catch (RelayAdmissionException ex) { return Results.StatusCode(ex.StatusCode); }
        }).RequireRateLimiting("entry");
        app.Map("/v1/tunnel", async (HttpContext context) =>
        {
            RelayTicket? ticket = tickets.Validate(Bearer(context), DateTimeOffset.UtcNow);
            if (ticket is null || !rooms.IsAllowed(ticket)) { context.Response.StatusCode = StatusCodes.Status401Unauthorized; return; }
            if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = StatusCodes.Status400BadRequest; return; }
            TimeSpan remaining = DateTimeOffset.FromUnixTimeSeconds(ticket.Expires) - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero) { context.Response.StatusCode = StatusCodes.Status401Unauthorized; return; }
            try
            {
                using RelayRooms.Reservation reservation = rooms.Reserve(ticket);
                using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync();
                using var session = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
                remaining = DateTimeOffset.FromUnixTimeSeconds(ticket.Expires) - DateTimeOffset.UtcNow;
                session.CancelAfter(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
                await rooms.ForwardReservedAsync(reservation, socket, session.Token);
            }
            catch (RelayAdmissionException ex) when (!context.Response.HasStarted)
            {
                context.Response.StatusCode = ex.StatusCode;
                if (ex.StatusCode == 429) context.Response.Headers.RetryAfter = "60";
            }
            catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or IOException)
            {
                // Device identities, tickets and tunnel contents never enter server logs.
                app.Logger.LogDebug("Relay connection ended: {Kind}", ex.GetType().Name);
            }
        }).RequireRateLimiting("entry");
        return app;
    }

    private static string Bearer(HttpContext context)
    {
        string authorization = context.Request.Headers.Authorization.ToString();
        return authorization.StartsWith("Bearer ", StringComparison.Ordinal) && authorization.Length <= 2048
            ? authorization[7..] : "";
    }
}
