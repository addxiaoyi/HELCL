using System;
using System.Linq;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using HelL.Core.Launcher;
using HelL.Core;
using Xunit;

namespace HelL.Tests;

public sealed class FileMirrorTests : IDisposable
{
    private readonly TestHome _home = new TestHome();
    public FileMirrorTests() { Source.ProbeHook = () => true; Source.InvalidateProbe(); }
    public void Dispose() { Source.ProbeHook = null; Source.InvalidateProbe(); _home.Dispose(); }

    [Theory]
    [InlineData("https://libraries.minecraft.net/com/google/code/gson/gson/2.10.1/gson-2.10.1.jar", "/libraries/com/google/code/gson/gson/2.10.1/gson-2.10.1.jar")]
    [InlineData("https://resources.download.minecraft.net/b6/b62ca8ec10d07e6bf5ac8dae0c8c1d2e6a1e3356", "/assets/objects/b6/b62ca8ec10d07e6bf5ac8dae0c8c1d2e6a1e3356")]
    [InlineData("https://maven.fabricmc.net/net/fabricmc/fabric-loader/0.16.0/fabric-loader-0.16.0.jar", "/fabric/maven/net/fabricmc/fabric-loader/0.16.0/fabric-loader-0.16.0.jar")]
    [InlineData("https://maven.quiltmc.org/repository/release/org/quiltmc/quilt-loader/0.28.0/quilt-loader-0.28.0.jar", "/quilt/maven/org/quiltmc/quilt-loader/0.28.0/quilt-loader-0.28.0.jar")]
    public void BothStaticMirrorsUseTheirDocumentedDirectories(string upstream, string path)
    {
        var candidates = Source.ExpandDownloadUrls(upstream);
        Assert.Contains("https://mc-mirror.com" + path, candidates);
        Assert.Contains("https://us-west-01.mc-mirror.com" + path, candidates);
        Assert.Contains(upstream, candidates);
        Assert.Equal(candidates.Count, candidates.Distinct().Count());
    }

    [Fact]
    public void ExplicitMirrorPreferenceIsRecognizedAndKeepsFallbacks()
    {
        Config.CONFIG.Set("download_source", "mc_mirror_west");
        Assert.Equal("mc_mirror_west", Source.DownloadMode());
        string upstream = "https://libraries.minecraft.net/test/asset.jar";
        Assert.Equal("https://us-west-01.mc-mirror.com/libraries/test/asset.jar", Source.ExpandDownloadUrls(upstream).First());
        Assert.Contains(upstream, Source.ExpandDownloadUrls(upstream));
    }

    [Fact]
    public void OfficialModeStaysOfficialAndThePartialCatalogNeverHidesVersions()
    {
        Config.CONFIG.Set("download_source", "official");
        string upstream = "https://libraries.minecraft.net/test/asset.jar";
        Assert.Equal(new[] { upstream }, Source.ExpandDownloadUrls(upstream));
        Assert.All(Source.VersionManifestUrls(), url => Assert.DoesNotContain("mc-mirror.com", url));
        Config.CONFIG.Set("download_source", "auto");
        Assert.All(Source.VersionManifestUrls(), url => Assert.DoesNotContain("mc-mirror.com", url));
    }

    [Fact]
    public void ObjectPathsComeFromVersionMetadataAndUnrelatedEndpointsStayUntouched()
    {
        string hash = new string('c', 40), client = "https://piston-data.mojang.com/v1/objects/" + hash + "/client.jar";
        FileMirrors.RegisterVersion(PyDict.Of("id", "1.21.1", "downloads", PyDict.Of("client", PyDict.Of("url", client, "sha1", hash)),
            "assetIndex", PyDict.Of("id", "17", "url", "https://piston-meta.mojang.com/v1/packages/" + hash + "/17.json", "sha1", hash),
            "logging", PyDict.Of("client", PyDict.Of("file", PyDict.Of("id", "client.xml", "url", "https://piston-data.mojang.com/v1/objects/" + hash + "/client.xml", "sha1", hash)))));
        Assert.Equal("/versions/1.21.1/client.jar", FileMirrors.RelativePath(client));
        Assert.Equal("/assets/indexes/17.json", FileMirrors.RelativePath("https://piston-meta.mojang.com/v1/packages/" + hash + "/17.json"));
        Assert.Equal("/logging/client.xml", FileMirrors.RelativePath("https://piston-data.mojang.com/v1/objects/" + hash + "/client.xml"));
        foreach (string url in new[] { "https://api.minecraftservices.com/minecraft/profile", "https://libraries.minecraft.net.evil.invalid/x.jar", "https://libraries.minecraft.net/x.jar?token=secret", "https://piston-meta.mojang.com/v1/packages/a/1.21.1.json", "https://maven.minecraftforge.net/net/minecraftforge/forge.jar" })
            Assert.Empty(FileMirrors.Candidates(url));
    }

    [Fact]
    public void AutomaticRankingRespondsToFailureAndNetworkChanges()
    {
        var ranking = new MirrorRanking(); var now = DateTimeOffset.UtcNow;
        string[] urls = { "https://libraries.minecraft.net/a.jar", FileMirrors.Main + "/libraries/a.jar", FileMirrors.West + "/libraries/a.jar", Source.BMCLAPI + "/maven/a.jar" };
        ranking.Record("network-a", new MirrorSample("official", "all", 500, true), now);
        ranking.Record("network-a", new MirrorSample("mc_mirror", "all", 100, true), now);
        ranking.Record("network-a", new MirrorSample("mc_mirror_west", "all", 50, true), now);
        ranking.Record("network-a", new MirrorSample("bmclapi", "all", 5, false), now);
        Assert.Equal(urls[2], ranking.Order(urls, "libraries", "network-a", "auto", now).First());
        ranking.Record("network-a", new MirrorSample("mc_mirror_west", "libraries", 0, false), now, failed: true);
        Assert.Equal(urls[1], ranking.Order(urls, "libraries", "network-a", "auto", now).First());
        Assert.Equal(urls[2], ranking.Order(urls, "assets", "network-a", "auto", now).First());
        Assert.Equal(urls[0], ranking.Order(urls, "libraries", "network-a", "auto", now.AddMinutes(11)).First());
        Assert.Equal(urls[0], ranking.Order(urls, "libraries", "network-b", "auto", now).First());
        Assert.Equal(urls[2], ranking.Order(urls, "libraries", "network-b", "mc_mirror_west", now).First());
    }

    [Theory]
    [InlineData(404)]
    [InlineData(200)]
    public void MissingOrCorruptPreferredMirrorFallsBackAndVerifiesTheOriginalHash(int status)
    {
        string replay = Environment.GetEnvironmentVariable("TIPCL_HTTP_REPLAY");
        byte[] content = Encoding.UTF8.GetBytes("verified upstream file fixture");
        string upstream = "https://libraries.minecraft.net/fixture/artifact.jar";
        string path = Path.Combine(_home.Dir, "artifact.jar");
        using var server = new DownloadHttpFixture(headers =>
        {
            string requested = headers["X-HelL-Replay-Url"];
            return requested.StartsWith(FileMirrors.Main + "/", StringComparison.Ordinal)
                ? new DownloadHttpFixture.Reply(status, Encoding.UTF8.GetBytes("wrong content"))
                : new DownloadHttpFixture.Reply(200, content);
        });
        try
        {
            Environment.SetEnvironmentVariable("TIPCL_HTTP_REPLAY", server.Url);
            Config.CONFIG.Set("download_source", "mc_mirror"); Config.CONFIG.Set("download_segments", 1L);
            var tracker = new DownloadTracker();
            new DownloadManager(tracker: tracker).Download(upstream, path, sha256: Convert.ToHexString(SHA256.HashData(content)), size: content.Length);
            Assert.StartsWith(FileMirrors.Main, server.Requests.First()["X-HelL-Replay-Url"]);
            Assert.Contains(server.Requests, headers => headers["X-HelL-Replay-Url"] == upstream);
            Assert.Equal(content, File.ReadAllBytes(path));
            Assert.Equal(upstream, tracker.url);
            Assert.Equal("libraries.minecraft.net", tracker.Snapshot().host);
        }
        finally { Environment.SetEnvironmentVariable("TIPCL_HTTP_REPLAY", replay); Source.InvalidateProbe(); }
    }

    [Fact]
    public void FastHtmlResponsesCannotWinTheProbeAndResultsAreCached()
    {
        string replay = Environment.GetEnvironmentVariable("TIPCL_HTTP_REPLAY");
        using var server = new DownloadHttpFixture(_ => new DownloadHttpFixture.Reply(200, Encoding.UTF8.GetBytes("<html>temporary gateway page</html>")));
        try
        {
            Environment.SetEnvironmentVariable("TIPCL_HTTP_REPLAY", server.Url); Source.InvalidateProbe();
            var first = MirrorHealth.Refresh(true);
            Assert.Equal(4, server.Requests.Count);
            Assert.All(Py.ListOr(first.Get("samples")).OfType<PyDict>(), sample => Assert.False(Py.Truthy(sample.Get("available"))));
            Assert.Equal("", first.Get("preferred"));
            MirrorHealth.Refresh(); Assert.Equal(4, server.Requests.Count);
            Source.InvalidateProbe(); MirrorHealth.Refresh(); Assert.Equal(8, server.Requests.Count);
        }
        finally { Environment.SetEnvironmentVariable("TIPCL_HTTP_REPLAY", replay); Source.InvalidateProbe(); }
    }

    [Fact]
    public void SwitchingProxyModeKeepsTheProcessEnvironmentForTheNextNetwork()
    {
        string proxy = Environment.GetEnvironmentVariable("HTTP_PROXY"), bypass = Environment.GetEnvironmentVariable("NO_PROXY");
        try
        {
            Environment.SetEnvironmentVariable("HTTP_PROXY", "http://127.0.0.1:38123"); Environment.SetEnvironmentVariable("NO_PROXY", "localhost");
            Config.CONFIG.Set("use_system_proxy", false); Net.ApplyProxyPolicy();
            Assert.True(Net.ForceDirect());
            Config.CONFIG.Set("use_system_proxy", true); Net.ApplyProxyPolicy();
            Assert.False(Net.ForceDirect());
            Assert.Equal("http://127.0.0.1:38123", Environment.GetEnvironmentVariable("HTTP_PROXY"));
            Assert.Equal("localhost", Environment.GetEnvironmentVariable("NO_PROXY"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("HTTP_PROXY", proxy); Environment.SetEnvironmentVariable("NO_PROXY", bypass);
            Config.CONFIG.Set("use_system_proxy", true); Net.ApplyProxyPolicy(); Net.ResetClients();
        }
    }
}
