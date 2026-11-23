using System.Net;
using System.Net.Sockets;
using HelL.Core.Launcher;
using HelL.Tests;
using Xunit;

namespace HelL.Portable.Tests;

public sealed class CurseForgeTransportTests : IDisposable
{
    private readonly TestHome home = new();
    private readonly string? previousReplay = Environment.GetEnvironmentVariable("TIPCL_HTTP_REPLAY");
    private readonly HttpListener listener = new();
    private readonly string address;

    public CurseForgeTransportTests()
    {
        Config.CONFIG.Set("use_system_proxy", false);
        using var port = new TcpListener(IPAddress.Loopback, 0);
        port.Start();
        address = $"http://127.0.0.1:{((IPEndPoint)port.LocalEndpoint).Port}/";
        port.Stop();
        listener.Prefixes.Add(address);
        listener.Start();
        Environment.SetEnvironmentVariable("TIPCL_HTTP_REPLAY", address);
    }

    [Theory]
    [InlineData("https://mod.mcimirror.top/curseforge/v1/mods")]
    [InlineData("https://untrusted.invalid/api.curseforge.com/v1/mods")]
    [InlineData("http://api.curseforge.com/v1/mods")]
    public async Task ExplicitHeadersCannotSendTheKeyToMirrorsOrUntrustedUrls(string url)
    {
        var request = Task.Run(() =>
        {
            using var response = Net.Request(url, new Net.RequestOptions { Headers = Mods.CfApiHeaders("fixture-only-cf-key") });
            Assert.Equal(200L, response.StatusCode);
        });
        var received = await listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(10));
        string? key = received.Request.Headers["x-api-key"];
        received.Response.Close();
        await request.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Null(key);
    }

    [Fact]
    public async Task CredentialedApiRedirectCannotForwardTheKey()
    {
        var request = Task.Run(() => Assert.Throws<NetError>(() =>
        {
            using var response = Net.Request("https://api.curseforge.com/v1/mods", new Net.RequestOptions
                { Headers = Mods.CfApiHeaders("fixture-only-cf-key") });
        }));
        var first = await listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(10));
        string? key = first.Request.Headers["x-api-key"];
        first.Response.StatusCode = 302;
        first.Response.RedirectLocation = address + "redirected";
        first.Response.Close();
        var redirected = listener.GetContextAsync();
        var completed = await Task.WhenAny(request, redirected).WaitAsync(TimeSpan.FromSeconds(10));
        if (completed == redirected)
        {
            var leaked = await redirected;
            leaked.Response.Close();
        }
        try
        {
            await request.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("fixture-only-cf-key", key);
            Assert.Same(request, completed);
        }
        finally
        {
            listener.Stop();
            try { await redirected; }
            catch (HttpListenerException) { }
        }
    }

    public void Dispose()
    {
        listener.Close();
        Environment.SetEnvironmentVariable("TIPCL_HTTP_REPLAY", previousReplay);
        home.Dispose();
    }
}
