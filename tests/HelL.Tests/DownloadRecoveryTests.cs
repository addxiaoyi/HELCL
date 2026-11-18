using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HelL.Core.Launcher;
using HelL.Core;
using Xunit;

namespace HelL.Tests;

public sealed class DownloadRecoveryTests
{
    private static readonly byte[] Content = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("Minecraft verified asset. ", 200)));
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    [Fact]
    public void CancelledCachedDownloadDoesNotReportSuccess()
    {
        using var home = new TestHome();
        string path = Path.Combine(home.Dir, "asset.bin"); File.WriteAllBytes(path, Content);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.Throws<DownloadError>(() => new DownloadManager(token: cancel.Token).Download("http://127.0.0.1:1/asset", path, sha256: Hash(Content), expand: false));
        Assert.Equal(Content, File.ReadAllBytes(path));
    }

    [Fact]
    public void SizeAloneCannotIdentifyAnOldPartialFile()
    {
        using var home = new TestHome(); Config.CONFIG.Set("download_segments", 1L);
        string path = Path.Combine(home.Dir, "asset.bin"); File.WriteAllBytes(path + ".part", Enumerable.Repeat((byte)'X', 64).ToArray());
        using var http = new DownloadHttpFixture(request => ReplyFor(request, Content));
        new DownloadManager().Download(http.Url, path, size: Content.Length, expand: false);
        Assert.Equal(Content, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VerifiedPrefixCanResumeOrRestartWhenRangeIsIgnored(bool ignoreRange)
    {
        using var home = new TestHome(); Config.CONFIG.Set("download_segments", 1L);
        string path = Path.Combine(home.Dir, "resource.zip"); File.WriteAllBytes(path + ".part", Content.Take(64).ToArray());
        using var http = new DownloadHttpFixture(request => ReplyFor(ignoreRange ? new Dictionary<string, string>() : request, Content));
        new DownloadManager().Download(http.Url, path, sha256: Hash(Content), size: Content.Length, expand: false);
        Assert.Equal("bytes=64-", http.Requests.First()["Range"]);
        Assert.Equal(Content, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".part"));
    }

    [Fact]
    public void MirrorFailureKeepsTheVerifiedResumeOffset()
    {
        using var home = new TestHome(); Config.CONFIG.Set("download_segments", 1L);
        string path = Path.Combine(home.Dir, "asset.bin"); File.WriteAllBytes(path + ".part", Content.Take(64).ToArray());
        using var failed = new DownloadHttpFixture(_ => new DownloadHttpFixture.Reply(503, Array.Empty<byte>()));
        using var mirror = new DownloadHttpFixture(request => ReplyFor(request, Content));
        new DownloadManager().Download(failed.Url, path, sha256: Hash(Content), size: Content.Length, urls: new[] { mirror.Url }, expand: false);
        Assert.Equal("bytes=64-", mirror.Requests.Single().GetValueOrDefault("Range"));
        Assert.Equal(Content, File.ReadAllBytes(path));
    }

    [Fact]
    public void BadChecksumNeverReplacesTheInstalledFile()
    {
        using var home = new TestHome(); Config.CONFIG.Set("download_segments", 1L);
        string path = Path.Combine(home.Dir, "asset.bin"); byte[] old = Encoding.UTF8.GetBytes("previous working asset"); File.WriteAllBytes(path, old);
        using var http = new DownloadHttpFixture(_ => new DownloadHttpFixture.Reply(200, new byte[Content.Length]));
        Assert.Throws<DownloadError>(() => new DownloadManager().Download(http.Url, path, sha256: Hash(Content), size: Content.Length, force: true, expand: false));
        Assert.Equal(old, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".part"));
    }

    [Fact]
    public void CurseForgeMetadataProtectsInstalledContentAndKeepsCancellation()
    {
        using var home = new TestHome(); Config.CONFIG.Set("download_segments", 1L);
        string path = Path.Combine(home.Dir, "pack.zip"); File.WriteAllBytes(path, Content);
        var metadata = PyDict.Of("fileLength", Content.Length, "hashes", Py.L(PyDict.Of("algo", 1L, "value", new string('a', 40))));
        using var http = new DownloadHttpFixture(_ => new DownloadHttpFixture.Reply(200, new byte[Content.Length]));
        Assert.Throws<DownloadError>(() => CatalogDownload.CurseForge(new DownloadManager(), new object[] { http.Url }, path, metadata));
        Assert.Equal(Content, File.ReadAllBytes(path));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        int before = http.Requests.Count;
        Assert.Throws<DownloadError>(() => CatalogDownload.CurseForge(new DownloadManager(token: cancel.Token), new object[] { http.Url }, path, metadata));
        Assert.Equal(before, http.Requests.Count); Assert.Equal(Content, File.ReadAllBytes(path));
    }

    [Fact]
    public void InterruptedTransferResumesWithANewManager()
    {
        using var home = new TestHome(); Config.CONFIG.Set("download_segments", 1L);
        string path = Path.Combine(home.Dir, "asset.bin");
        using var cancel = new CancellationTokenSource();
        using var http = new DownloadHttpFixture(request => ReplyFor(request, Content));
        var first = new DownloadManager(token: cancel.Token, on_progress: (_, done, _) => { if (done > 0) cancel.Cancel(); });
        Assert.Throws<DownloadError>(() => first.Download(http.Url, path, sha256: Hash(Content), size: Content.Length, expand: false));
        Assert.False(File.Exists(path)); Assert.True(File.Exists(path + ".part"));
        new DownloadManager().Download(http.Url, path, sha256: Hash(Content), size: Content.Length, expand: false);
        Assert.Equal(Content, File.ReadAllBytes(path));
    }

    [Fact]
    public void ModrinthNestedHashesRejectSameSizedCorruptionAndKeepInstalledFile()
    {
        using var home = new TestHome(); Config.CONFIG.Set("download_segments", 1L);
        string path = Path.Combine(home.Dir, "pack.zip"); File.WriteAllBytes(path, Content);
        var metadata = PyDict.Of("size", Content.Length, "hashes", PyDict.Of("sha512", new string('a', 128)));
        using var http = new DownloadHttpFixture(_ => new DownloadHttpFixture.Reply(200, new byte[Content.Length]));
        Assert.Throws<DownloadError>(() => CatalogDownload.Modrinth(new DownloadManager(), new object[] { http.Url }, path, metadata));
        Assert.Equal(Content, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".part"));
        Assert.Throws<DownloadError>(() => CatalogDownload.Modrinth(new DownloadManager(), new object[] { http.Url }, path, PyDict.Of("size", Content.Length)));
    }

    private static DownloadHttpFixture.Reply ReplyFor(IDictionary<string, string> request, byte[] content)
    {
        if (!request.TryGetValue("Range", out string range)) return new DownloadHttpFixture.Reply(200, content);
        int offset = int.Parse(range.Substring(6).TrimEnd('-'));
        if (offset >= content.Length) return new DownloadHttpFixture.Reply(416, Array.Empty<byte>());
        return new DownloadHttpFixture.Reply(206, content.Skip(offset).ToArray(), "bytes " + offset + "-" + (content.Length - 1) + "/" + content.Length);
    }
}

internal sealed class DownloadHttpFixture : IDisposable
{
    internal sealed record Reply(int Status, byte[] Body, string Range = null);
    private readonly TcpListener _listener = new TcpListener(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _cancel = new CancellationTokenSource();
    private readonly Task _loop;
    private readonly Func<IDictionary<string, string>, Reply> _respond;
    public ConcurrentQueue<Dictionary<string, string>> Requests { get; } = new();
    public string Url { get; }
    public DownloadHttpFixture(Func<IDictionary<string, string>, Reply> respond)
    {
        _respond = respond; _listener.Start(); Url = "http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port + "/asset";
        _loop = Serve();
    }
    private async Task Serve()
    {
        var peers = new List<Task>();
        try { while (!_cancel.IsCancellationRequested) peers.Add(Respond(await _listener.AcceptTcpClientAsync(_cancel.Token))); }
        catch (OperationCanceledException) when (_cancel.IsCancellationRequested) { }
        await Task.WhenAll(peers);
    }
    private async Task Respond(TcpClient peer)
    {
        using (peer)
        {
            var stream = peer.GetStream(); using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
            await reader.ReadLineAsync(_cancel.Token);
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            while (await reader.ReadLineAsync(_cancel.Token) is string line && line.Length > 0)
            { int colon = line.IndexOf(':'); headers[line.Substring(0, colon)] = line.Substring(colon + 1).Trim(); }
            Requests.Enqueue(headers); var reply = _respond(headers);
            string response = "HTTP/1.1 " + reply.Status + " Fixture\r\nConnection: close\r\nContent-Length: " + reply.Body.Length + "\r\n" + (reply.Range == null ? "" : "Content-Range: " + reply.Range + "\r\n") + "\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(response), _cancel.Token); await stream.WriteAsync(reply.Body, _cancel.Token);
        }
    }
    public void Dispose() { _cancel.Cancel(); _listener.Stop(); _loop.GetAwaiter().GetResult(); _cancel.Dispose(); }
}
