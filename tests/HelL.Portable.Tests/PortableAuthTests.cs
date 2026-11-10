using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using HelL.Core;
using HelL.Core.Launcher;
using HelL.Portable;
using Xunit;

namespace HelL.Portable.Tests;

public sealed class PortableAuthTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "hell-auth-test-" + Guid.NewGuid().ToString("N"));
    private readonly string? previous = Environment.GetEnvironmentVariable("TIPCL_HTTP_REPLAY");
    private readonly HttpListener listener = new();
    private readonly Task server;
    private readonly AccountVaultTests.MemoryStore keys = new();
    private readonly AccountVault vault;
    private int calls;
    private bool pending;
    private bool transient;
    private bool rejectRefresh;
    private bool failEntitlement;

    public PortableAuthTests()
    {
        Directory.CreateDirectory(root);
        _ = new PortableLauncher(root);
        Config.CONFIG.Set("use_system_proxy", false);
        var port = new TcpListener(IPAddress.Loopback, 0);
        port.Start();
        int number = ((IPEndPoint)port.LocalEndpoint).Port;
        port.Stop();
        string url = $"http://127.0.0.1:{number}/";
        listener.Prefixes.Add(url);
        listener.Start();
        Environment.SetEnvironmentVariable("TIPCL_HTTP_REPLAY", url);
        vault = new AccountVault(root, keys);
        server = Serve();
    }

    private async Task Serve()
    {
        while (listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync(); }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException) { break; }
            string path = new Uri(context.Request.Headers["X-HelL-Replay-Url"]!).AbsolutePath;
            Interlocked.Increment(ref calls);
            string body = new StreamReader(context.Request.InputStream).ReadToEnd();
            object reply;
            int status = 200;
            if (path == "/oauth20_token.srf" && rejectRefresh && body.Contains("grant_type=refresh_token"))
            { status = 400; reply = new { error = "invalid_grant" }; }
            else if (transient) { status = 503; reply = new { error = "temporary" }; }
            else if (path == "/oauth20_token.srf" && pending) { status = 400; reply = new { error = "authorization_pending" }; }
            else reply = path switch
            {
                "/oauth20_connect.srf" => new { device_code = "fixture-device", user_code = "TEST-CODE", verification_uri = "https://www.microsoft.com/link", expires_in = 90, interval = 1 },
                "/oauth20_token.srf" => new { access_token = "fixture-ms", refresh_token = "fixture-rotated-refresh" },
                "/user/authenticate" => new { Token = "fixture-xbl" },
                "/xsts/authorize" => new { Token = "fixture-xsts", DisplayClaims = new { xui = new[] { new { uhs = "1234" } } } },
                "/authentication/login_with_xbox" => new { access_token = "fixture-minecraft", expires_in = 120 },
                "/entitlements/mcstore" => new { items = failEntitlement ? Array.Empty<object>() : new object[] { new { name = "game_minecraft" } } },
                "/minecraft/profile" => new { name = "TestPlayer", id = "8667ba71b85a4004af54457a9734eed7" },
                _ => throw new InvalidOperationException("Unexpected fixture route: " + path)
            };
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(reply);
            context.Response.StatusCode = status;
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        }
    }

    [Fact]
    public void LoginRestoreRefreshAndLogoutCompleteWithoutPlaintext()
    {
        var session = new PortableAccount(vault);
        session.Login(true, code => Assert.Equal("TEST-CODE", code.Code), _ => { }, default);
        Assert.True(session.IsSignedIn);
        Assert.DoesNotContain("fixture", File.ReadAllText(Path.Combine(root, "portable-account.json")));
        var restored = new PortableAccount(vault);
        restored.Restore();
        Assert.Equal("TestPlayer", restored.DisplayName);
        Assert.Equal("fixture-minecraft", restored.ForLaunch(default, true)["token"]);
        Assert.Single(keys.Keys);
        restored.Logout();
        Assert.Empty(keys.Keys);
        Assert.Null(vault.Load());
    }

    [Fact]
    public void InvalidRefreshPreservesSavedAccountAndRequestsRelogin()
    {
        var session = new PortableAccount(vault);
        session.Login(true, _ => { }, _ => { }, default);
        string previousFile = File.ReadAllText(Path.Combine(root, "portable-account.json"));
        rejectRefresh = true;
        var error = Assert.Throws<AuthError>(() => session.ForLaunch(default, true));
        Assert.Contains("重新登录", error.Message);
        Assert.Equal(previousFile, File.ReadAllText(Path.Combine(root, "portable-account.json")));
    }

    [Fact]
    public void LockedKeyringLeavesAccountInMemoryAndDoesNotWriteTokens()
    {
        keys.FailWrites = true;
        var session = new PortableAccount(vault);
        session.Login(true, _ => { }, _ => { }, default);
        Assert.True(session.IsSignedIn);
        Assert.Contains("内存", session.Notice);
        Assert.False(File.Exists(Path.Combine(root, "portable-account.json")));
        Assert.Equal("fixture-minecraft", session.ForLaunch(default)["token"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancellationInterruptsPendingAuthorizationAndRetryDelay(bool retry)
    {
        pending = !retry;
        transient = retry;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var session = new PortableAccount(vault);
        var watch = Stopwatch.StartNew();
        Assert.ThrowsAny<OperationCanceledException>(() => session.Login(true, _ => { }, _ => { }, cancellation.Token));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2));
        Assert.False(session.IsSignedIn);
        Assert.Empty(keys.Keys);
    }

    [Fact]
    public void MissingGameOwnershipNeverCreatesSavedAccount()
    {
        failEntitlement = true;
        var session = new PortableAccount(vault);
        Assert.Throws<AuthError>(() => session.Login(true, _ => { }, _ => { }, default));
        Assert.False(session.IsSignedIn);
        Assert.Null(vault.Load());
    }

    [Fact]
    public void LostOwnershipBlocksLaunchAfterRefresh()
    {
        var session = new PortableAccount(vault);
        session.Login(true, _ => { }, _ => { }, default);
        failEntitlement = true;
        Assert.Throws<AuthError>(() => session.ForLaunch(default, true));
    }

    [Theory]
    [InlineData("http://www.microsoft.com/link")]
    [InlineData("https://microsoft.com.evil.example/link")]
    [InlineData("https://evil.example@www.microsoft.com/link")]
    [InlineData("file:///tmp/login")]
    public void AuthorizationPageRejectsUntrustedDestinations(string address) =>
        Assert.Throws<InvalidOperationException>(() => PortableAccount.ValidateDeviceUri(address));

    public void Dispose()
    {
        listener.Close();
        server.GetAwaiter().GetResult();
        Environment.SetEnvironmentVariable("TIPCL_HTTP_REPLAY", previous);
        Directory.Delete(root, recursive: true);
    }
}
