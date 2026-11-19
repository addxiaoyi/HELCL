using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using HelL.Core;
using HelL.Core.Launcher;
using HelL.Portable;
using HelL.Tests;
using Xunit;

namespace HelL.Portable.Tests;

public sealed class CatalogPagingFlowTests : IDisposable
{
    private readonly TestHome home = new();
    private readonly string? oldReplay = Environment.GetEnvironmentVariable("TIPCL_HTTP_REPLAY");
    private readonly HttpListener listener = new();
    private readonly CancellationTokenSource stopping = new();
    private readonly Task serving;
    private readonly ConcurrentQueue<Uri> requests = new();

    public CatalogPagingFlowTests()
    {
        using var port = new TcpListener(IPAddress.Loopback, 0);
        port.Start();
        string address = "http://127.0.0.1:" + ((IPEndPoint)port.LocalEndpoint).Port + "/";
        port.Stop();
        listener.Prefixes.Add(address);
        listener.Start();
        Config.CONFIG.Set("use_system_proxy", false);
        Environment.SetEnvironmentVariable("TIPCL_HTTP_REPLAY", address);
        serving = ServeAsync();
    }

    [Theory]
    [InlineData("modrinth", "offset")]
    [InlineData("curseforge", "index")]
    public void NextPageUsesTheUpstreamOffsetAndPreservesProjectIdentity(string provider, string parameter)
    {
        var catalog = new PortableCatalog();
        var first = catalog.Search("mod", "fixture", provider, null, default, "downloads", offset: 0, pageSize: 2);
        var second = catalog.Search("mod", "fixture", provider, null, default, "downloads", offset: 2, pageSize: 2);
        Assert.Equal(2, first.Count);
        Assert.Equal(2, second.Count);
        Assert.All(first.Concat(second), project => Assert.False(string.IsNullOrWhiteSpace(project.Id)));
        Assert.Empty(first.Select(project => project.Id.Length > 0 ? project.Id : project.Slug)
            .Intersect(second.Select(project => project.Id.Length > 0 ? project.Id : project.Slug)));
        Assert.Contains(requests, uri => Query(uri, parameter) == "2");
        Assert.Contains(requests, uri => Query(uri, provider == "modrinth" ? "limit" : "pageSize") == "2");
    }

    [Fact]
    public void DetailsKeepTrustedGalleryAndReadableBodyWhileBindingTheSelectedRelease()
    {
        var project = new CatalogProject("fixture-id", "fixture", "modrinth", "mod", "Fixture", "Fixture", "Author", "", "", "", 1);
        var release = new CatalogRelease("release-1", "Release", "fixture.jar", ["1.21.1"], ["fabric"], "2026-11-19", 0, new PyDict());
        CatalogProjectDetails detail = new PortableCatalog().Details(project, release, default);
        Assert.Equal(["https://cdn.modrinth.com/data/fixture/image.png"], detail.Gallery);
        Assert.Contains("Full introduction", detail.Body);
        Assert.Contains("Version note", detail.Changelog);
        Assert.Equal("https://modrinth.com/mod/fixture", detail.ProjectUrl);
    }

    private async Task ServeAsync()
    {
        while (!stopping.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync().WaitAsync(stopping.Token); }
            catch (Exception error) when (stopping.IsCancellationRequested && error is OperationCanceledException or HttpListenerException or ObjectDisposedException) { return; }
            try
            {
                Uri upstream = new(context.Request.Headers["X-HelL-Replay-Url"]!);
                requests.Enqueue(upstream);
                object body;
                int offset = int.TryParse(Query(upstream, upstream.AbsolutePath.EndsWith("/mods/search") ? "index" : "offset"), out int start) ? start : 0;
                if (upstream.AbsolutePath.EndsWith("/mods/search"))
                    body = new { data = Enumerable.Range(offset, 2).Select(index => new { id = 1000 + index, slug = "fixture-" + index,
                        name = "Fixture " + index, summary = "测试资源", downloadCount = 1, categories = Array.Empty<object>() }) };
                else if (upstream.AbsolutePath.EndsWith("/search"))
                    body = new { hits = Enumerable.Range(offset, 2).Select(index => new { project_id = "fixture-" + index,
                        slug = "fixture-" + index, title = "Fixture " + index, author = "Author", description = "测试资源", downloads = 1 }) };
                else if (upstream.AbsolutePath.Contains("/project/"))
                    body = new { id = "fixture-id", body = "# Full introduction\nReadable body", gallery = new[] {
                        new { url = "https://cdn.modrinth.com/data/fixture/image.png" }, new { url = "https://untrusted.example/private.png" } } };
                else body = new { id = "release-1", project_id = "fixture-id", changelog = "Version note" };
                byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(body);
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes, stopping.Token);
            }
            finally { context.Response.Close(); }
        }
    }

    private static string Query(Uri uri, string key) => uri.Query.TrimStart('?').Split('&').Select(part => part.Split('=', 2))
        .Where(part => part[0] == key).Select(part => Uri.UnescapeDataString(part.ElementAtOrDefault(1) ?? "")).FirstOrDefault() ?? "";

    public void Dispose()
    {
        stopping.Cancel(); listener.Close();
        try { Assert.True(serving.Wait(TimeSpan.FromSeconds(5)), "Catalog paging replay did not stop."); }
        finally { Environment.SetEnvironmentVariable("TIPCL_HTTP_REPLAY", oldReplay); stopping.Dispose(); home.Dispose(); }
    }
}
