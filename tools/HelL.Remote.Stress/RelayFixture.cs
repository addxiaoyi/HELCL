using System.Security.Cryptography;
using System.Text.Json;
using HelL.Relay;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace HelL.Remote.Stress;

internal sealed class RelayFixture(string root) : IAsyncDisposable
{
    private WebApplication? app;
    private readonly string signingKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    internal string OperatorToken { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    internal Uri Address { get; private set; } = null!;
    private string StatePath => Path.Combine(root, "relay", "rooms.json");
    internal bool Stopped => app is null;

    internal async Task StartAsync(CancellationToken cancellation)
    {
        if (app is not null) throw new InvalidOperationException("The fixture relay is already running.");
        WebApplication next = RelayApp.Build([], builder =>
        {
            builder.Configuration.Sources.Clear();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Relay:OperatorToken"] = OperatorToken, ["Relay:SigningKey"] = signingKey,
                ["Relay:StatePath"] = StatePath, ["Relay:MaxInvitationChannels"] = "1024",
                ["Relay:OperatorRequestsPerMinute"] = "100000", ["Relay:AnonymousRequestsPerMinute"] = "100000",
                ["urls"] = Address?.AbsoluteUri.TrimEnd('/') ?? "http://127.0.0.1:0"
            });
            builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(2));
            builder.Logging.ClearProviders();
        });
        try { await next.StartAsync(cancellation); }
        catch { await next.DisposeAsync(); throw; }
        app = next;
        Address = new Uri(next.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single() + "/");
    }

    internal async Task StopAsync(CancellationToken cancellation)
    {
        if (app is null) return;
        WebApplication previous = app;
        try { await previous.StopAsync(cancellation); }
        finally { await previous.DisposeAsync(); app = null; }
    }

    internal (int Rooms, int Feeds) Counts()
    {
        // The caller samples between acknowledged writes, never concurrently with a publish/renew.
        static int Count(string path) { using JsonDocument json = JsonDocument.Parse(File.ReadAllBytes(path)); return json.RootElement.GetArrayLength(); }
        return (Count(StatePath), Count(StatePath + ".invitations"));
    }

    public async ValueTask DisposeAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await StopAsync(timeout.Token);
    }
}
