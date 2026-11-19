using System.Security.Cryptography;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using HelL.Core;
using HelL.Core.Launcher;
using HelL.Tests;
using Xunit;

namespace HelL.Portable.Tests;

public sealed class DownloadCompletionTests
{
    [Fact]
    public void CancellationFromTheLastFileProgressCannotCompleteTheBatch()
    {
        using var home = new TestHome();
        byte[] content = Encoding.UTF8.GetBytes("a previously verified download fixture");
        string path = Path.Combine(home.Dir, "installed.bin");
        File.WriteAllBytes(path, content);
        using var cancelled = new CancellationTokenSource();
        var tracker = new DownloadTracker();
        var manager = new DownloadManager(threads: 1L, tracker: tracker, token: cancelled.Token,
            on_progress: (_, _, _) => { if (tracker.Snapshot().files_done == 1) cancelled.Cancel(); });
        var task = new DownloadTask("http://127.0.0.1:1/not-requested", path,
            Convert.ToHexString(SHA1.HashData(content)).ToLowerInvariant(), content.Length);

        Assert.Throws<DownloadError>(() => manager.DownloadAll([task]));

        Assert.True(cancelled.IsCancellationRequested);
        Assert.Equal("fail", tracker.Snapshot().phase);
        Assert.False(tracker.Snapshot().active);
        Assert.Equal(content, File.ReadAllBytes(path));
    }

    [Fact]
    public void AnAlreadyCancelledEmptyBatchCannotReportSuccess()
    {
        using var home = new TestHome();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var tracker = new DownloadTracker();
        var manager = new DownloadManager(token: cancelled.Token, tracker: tracker);

        Assert.Throws<DownloadError>(() => manager.DownloadAll([]));

        Assert.NotEqual("done", tracker.Snapshot().phase);
        Assert.False(tracker.Snapshot().active);
    }

    [Fact]
    public void CancellationThrownByInitialProgressEndsTheBatch()
    {
        using var home = new TestHome();
        var tracker = new DownloadTracker();
        var manager = new DownloadManager(tracker: tracker, on_progress: (_, _, _) => throw new TaskCancelled());

        Assert.Throws<TaskCancelled>(() => manager.DownloadAll([]));

        Assert.Equal("fail", tracker.Snapshot().phase);
        Assert.False(tracker.Snapshot().active);
    }

    [Fact]
    public void AFailedFilePromotionMustKeepTheInstalledTarget()
    {
        using var home = new TestHome();
        string path = Path.Combine(home.Dir, "installed.bin");
        const string previous = "keep the previously installed file";
        File.WriteAllText(path, previous);

        Assert.ThrowsAny<Exception>(() => Utils.OsReplace(path + ".missing-part", path));

        Assert.Equal(previous, File.ReadAllText(path));
    }

    [Fact]
    public void LosingTheVerifiedDownloadBeforeCommitDoesNotDeleteTheInstalledFile()
    {
        using var home = new TestHome();
        Config.CONFIG.Set("download_segments", 1L);
        Config.CONFIG.Set("use_system_proxy", false);
        byte[] content = Encoding.UTF8.GetBytes("a complete and verified replacement download");
        string path = Path.Combine(home.Dir, "installed.bin");
        const string previous = "keep the working installation";
        File.WriteAllText(path, previous);
        using var http = new FileServer(content);
        var tracker = new DownloadTracker();
        int removed = 0;
        var manager = new DownloadManager(tracker: tracker, on_progress: (_, _, _) =>
        {
            if (tracker.Snapshot().phase != "verify" || !File.Exists(path + ".part")) return;
            File.Delete(path + ".part");
            removed++;
        });

        Assert.Throws<DownloadError>(() => manager.Download(http.Url, path,
            sha256: Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(),
            size: content.Length, force: true, expand: false));

        Assert.True(removed > 0);
        Assert.Equal(previous, File.ReadAllText(path));
        Assert.Equal("fail", tracker.Snapshot().phase);
    }

    [Fact]
    public void AnUncancelledVerifiedBatchStillFinishesSuccessfully()
    {
        using var home = new TestHome();
        byte[] content = Encoding.UTF8.GetBytes("an already verified cached file");
        string path = Path.Combine(home.Dir, "installed.bin");
        File.WriteAllBytes(path, content);
        var tracker = new DownloadTracker();
        var manager = new DownloadManager(tracker: tracker);
        var task = new DownloadTask("http://127.0.0.1:1/not-requested", path,
            Convert.ToHexString(SHA1.HashData(content)).ToLowerInvariant(), content.Length);

        Assert.True(manager.DownloadAll([task]));

        Assert.Equal("done", tracker.Snapshot().phase);
        Assert.False(tracker.Snapshot().active);
        Assert.Equal(content, File.ReadAllBytes(path));
    }

    [Fact]
    public void AReplacementAcrossFileSystemsPreservesBothFilesWhenItCannotCommit()
    {
        if (!OperatingSystem.IsLinux() || !Directory.Exists("/dev/shm") || !File.Exists("/usr/bin/stat")) return;
        using var home = new TestHome();
        string memory = Path.Combine("/dev/shm", "hell-download-commit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(memory);
        try
        {
            if (LinuxDevice(home.Dir) == LinuxDevice(memory)) return;
            string source = Path.Combine(memory, "new.part");
            string target = Path.Combine(home.Dir, "installed.bin");
            File.WriteAllText(source, "verified replacement");
            File.WriteAllText(target, "working installation");

            Assert.ThrowsAny<Exception>(() => Utils.OsReplace(source, target));

            Assert.Equal("working installation", File.ReadAllText(target));
            Assert.Equal("verified replacement", File.ReadAllText(source));
        }
        finally { Directory.Delete(memory, recursive: true); }
    }

    private static string LinuxDevice(string path)
    {
        var start = new ProcessStartInfo("/usr/bin/stat") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { "-c", "%d", "--", path }) start.ArgumentList.Add(argument);
        using var probe = Process.Start(start) ?? throw new InvalidOperationException("The filesystem probe did not start.");
        string device = probe.StandardOutput.ReadToEnd();
        probe.WaitForExit();
        Assert.Equal(0, probe.ExitCode);
        return device.Trim();
    }

    private sealed class FileServer : IDisposable
    {
        private readonly HttpListener listener = new();
        private readonly CancellationTokenSource stopped = new();
        private readonly Task server;
        public string Url { get; }

        public FileServer(byte[] content)
        {
            using var port = new TcpListener(IPAddress.Loopback, 0);
            port.Start();
            Url = $"http://127.0.0.1:{((IPEndPoint)port.LocalEndpoint).Port}/";
            port.Stop();
            listener.Prefixes.Add(Url);
            listener.Start();
            server = Serve(content);
        }

        private async Task Serve(byte[] content)
        {
            while (!stopped.IsCancellationRequested)
            {
                HttpListenerContext request;
                try { request = await listener.GetContextAsync().WaitAsync(stopped.Token); }
                catch (OperationCanceledException) when (stopped.IsCancellationRequested) { return; }
                catch (HttpListenerException) when (stopped.IsCancellationRequested) { return; }
                try
                {
                    request.Response.ContentLength64 = content.Length;
                    await request.Response.OutputStream.WriteAsync(content, stopped.Token);
                }
                finally { request.Response.Close(); }
            }
        }

        public void Dispose()
        {
            stopped.Cancel();
            listener.Close();
            try { server.GetAwaiter().GetResult(); }
            finally { stopped.Dispose(); }
        }
    }
}
