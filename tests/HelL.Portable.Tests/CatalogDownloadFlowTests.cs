using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HelL.Core;
using HelL.Core.Launcher;
using HelL.Portable;
using Xunit;

namespace HelL.Portable.Tests;

public sealed class CatalogDownloadFlowTests : IDisposable
{
    private readonly string root = Path.Combine(Environment.GetEnvironmentVariable("RUNNER_TEMP") ?? Path.GetTempPath(), "hell-catalog-" + Guid.NewGuid().ToString("N"));
    private static readonly byte[] Archive = Encoding.UTF8.GetBytes("isolated resource download fixture");
    private readonly string? previousReplay = Environment.GetEnvironmentVariable("TIPCL_HTTP_REPLAY");
    private readonly HttpListener listener = new();
    private readonly CancellationTokenSource stop = new();
    private readonly Task server;
    private readonly PortableLauncher launcher;
    private bool corruptArchive;

    public CatalogDownloadFlowTests()
    {
        launcher = new PortableLauncher(root);
        launcher.CreateInstance("下载目标");
        var game = new Instance("下载目标");
        string version = Path.Combine(game.VersionsDir(), "1.21.1");
        Directory.CreateDirectory(version);
        File.WriteAllText(Path.Combine(version, "1.21.1.json"), "{\"id\":\"1.21.1\"}");
        File.WriteAllText(Path.Combine(version, "1.21.1.jar"), "not an executable fixture");
        using var port = new TcpListener(IPAddress.Loopback, 0);
        port.Start();
        string address = $"http://127.0.0.1:{((IPEndPoint)port.LocalEndpoint).Port}/";
        port.Stop();
        listener.Prefixes.Add(address);
        listener.Start();
        Environment.SetEnvironmentVariable("TIPCL_HTTP_REPLAY", address);
        Config.CONFIG.Set("use_system_proxy", false);
        server = Serve();
    }

    [Fact]
    public void SearchSelectVersionDownloadAndRetryUsesTheSelectedInstance()
    {
        var catalog = new PortableCatalog();
        var target = catalog.Target("下载目标", "1.21.1");
        var project = Assert.Single(catalog.Search("resourcepack", "fixture", "modrinth", target, default));
        Assert.Equal("fixture", project.Slug);
        var release = Assert.Single(catalog.Releases(project, target, default));
        Assert.Equal("release-test", release.Id);
        catalog.Download(project, release, "下载目标", "1.21.1", new Progress<InstallProgress>(), default);
        string path = Path.Combine(launcher.InstanceFolder("下载目标"), "resourcepacks", "fixture.zip");
        Assert.Equal(Archive, File.ReadAllBytes(path));
        catalog.Download(project, release, "下载目标", "1.21.1", new Progress<InstallProgress>(), default);
        Assert.Equal(Archive, File.ReadAllBytes(path));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!));
    }

    [Fact]
    public void ForeignReleaseAndCancelledDownloadLeaveNoResourceFile()
    {
        string resources = Path.Combine(launcher.InstanceFolder("下载目标"), "resourcepacks");
        // Instance creation seeds empty standard folders; absence here detects any later write.
        Assert.Empty(Directory.EnumerateFileSystemEntries(resources));
        Directory.Delete(resources);
        var catalog = new PortableCatalog();
        var project = Assert.Single(catalog.Search("resourcepack", "fixture", "modrinth", null, default));
        var release = Assert.Single(catalog.Releases(project, null, default));
        Assert.Throws<InvalidOperationException>(() => catalog.Download(project, release with { Id = "other-project-file" }, "下载目标", "1.21.1", new Progress<InstallProgress>(), default));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => catalog.Download(project, release, "下载目标", "1.21.1", new Progress<InstallProgress>(), cancelled.Token));
        Assert.False(Directory.Exists(resources));
    }

    private async Task Serve()
    {
        while (!stop.IsCancellationRequested)
        {
            HttpListenerContext request;
            try { request = await listener.GetContextAsync().WaitAsync(stop.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
            catch (HttpListenerException) when (stop.IsCancellationRequested) { return; }
            try
            {
                var upstream = new Uri(request.Request.Headers["X-HelL-Replay-Url"] ?? throw new InvalidOperationException("Replay origin missing."));
                byte[] body;
                if (upstream.AbsolutePath.EndsWith("/search"))
                    body = JsonSerializer.SerializeToUtf8Bytes(new { hits = new[] { new { project_id = "fixture", slug = "fixture", title = "Fixture pack", description = "验收资源包", author = "Fixture", downloads = 10, icon_url = "" } } });
                else if (upstream.AbsolutePath.EndsWith("/version"))
                    body = VersionResponse();
                else if (upstream.AbsolutePath.EndsWith("/fixture.zip")) body = corruptArchive ? Encoding.UTF8.GetBytes("wrong bytes") : Archive;
                else throw new InvalidOperationException("Unexpected catalog replay route: " + upstream.AbsolutePath);
                request.Response.ContentLength64 = body.Length;
                await request.Response.OutputStream.WriteAsync(body, stop.Token).ConfigureAwait(false);
            }
            finally { request.Response.Close(); }
        }
    }

    public void Dispose()
    {
        stop.Cancel();
        listener.Close();
        try { Assert.True(server.Wait(TimeSpan.FromSeconds(5)), "Catalog replay did not stop."); }
        finally
        {
            stop.Dispose();
            Environment.SetEnvironmentVariable("TIPCL_HTTP_REPLAY", previousReplay);
            Config.Reset();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static byte[] VersionResponse()
    {
        var file = new
        {
            primary = true, filename = "fixture.zip", url = "https://cdn.modrinth.com/data/fixture/fixture.zip", size = Archive.Length,
            hashes = new { sha512 = Convert.ToHexString(SHA512.HashData(Archive)).ToLowerInvariant() }
        };
        return JsonSerializer.SerializeToUtf8Bytes(new[]
        {
            new { id = "release-test", name = "Fixture 1", version_number = "1", game_versions = new[] { "1.21.1" },
                loaders = new[] { "minecraft" }, date_published = "2026-11-11T00:00:00Z", files = new[] { file } }
        });
    }

    [Fact]
    public void CorruptArchiveDoesNotBecomeAnInstalledResource()
    {
        var catalog = new PortableCatalog();
        var project = Assert.Single(catalog.Search("resourcepack", "fixture", "modrinth", null, default));
        var release = Assert.Single(catalog.Releases(project, null, default));
        corruptArchive = true;
        Assert.Throws<DownloadError>(() => catalog.Download(project, release, "下载目标", "1.21.1", new Progress<InstallProgress>(), default));
        Assert.False(File.Exists(Path.Combine(launcher.InstanceFolder("下载目标"), "resourcepacks", "fixture.zip")));
    }

    [Fact]
    public void DatapackRequiresAnExistingWorldAndWritesInsideThatWorld()
    {
        var catalog = new PortableCatalog();
        var project = Assert.Single(catalog.Search("datapack", "fixture", "modrinth", null, default));
        var release = Assert.Single(catalog.Releases(project, null, default));
        Assert.Throws<ArgumentException>(() => catalog.Download(project, release, "下载目标", "1.21.1", new Progress<InstallProgress>(), default, "../escape"));
        string world = Path.Combine(launcher.InstanceFolder("下载目标"), "saves", "中文存档");
        Directory.CreateDirectory(world);
        File.WriteAllBytes(Path.Combine(world, "level.dat"), [1]);
        Assert.Equal(new[] { "中文存档" }, catalog.WorldsForTarget("下载目标", "1.21.1"));
        catalog.Download(project, release, "下载目标", "1.21.1", new Progress<InstallProgress>(), default, "中文存档");
        Assert.Equal(Archive, File.ReadAllBytes(Path.Combine(world, "datapacks", "fixture.zip")));
    }

    [Fact]
    public void SameNameWithDifferentContentIsPreserved()
    {
        var catalog = new PortableCatalog();
        var project = Assert.Single(catalog.Search("resourcepack", "fixture", "modrinth", null, default));
        var release = Assert.Single(catalog.Releases(project, null, default));
        string path = Path.Combine(launcher.InstanceFolder("下载目标"), "resourcepacks", "fixture.zip");
        File.WriteAllText(path, "user-edited pack");
        Assert.Throws<IOException>(() => catalog.Download(project, release, "下载目标", "1.21.1", new Progress<InstallProgress>(), default));
        Assert.Equal("user-edited pack", File.ReadAllText(path));
    }
}
