using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HelL.Core.Launcher;
using Xunit;

namespace HelL.Tests;

public sealed class WallpaperCacheManagementTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "hell-cache-management-" + Guid.NewGuid().ToString("N"));
    private readonly WallpaperVideoCache _cache;
    public WallpaperCacheManagementTests() { Directory.CreateDirectory(_folder); _cache = new WallpaperVideoCache(_folder); }
    private string Video(char key, int bytes)
    {
        string path = Path.Combine(_folder, new string(key, 64) + ".mp4"); File.WriteAllBytes(path, new byte[bytes]); File.WriteAllText(path + ".size", bytes.ToString()); return path;
    }
    [Fact]
    public async Task UsageAndCleanupPreservePlaybackAndUnrelatedFiles()
    {
        string active = Video('a', 4096), unused = Video('b', 2048), unrelated = Path.Combine(_folder, "original.mp4"); File.WriteAllText(unrelated, "keep");
        var playback = _cache.HoldPlayback(active);
        try
        {
            var usage = _cache.Inspect(); Assert.Equal(2, usage.Videos); Assert.Equal(6144, usage.Bytes); Assert.Equal(4096, usage.InUseBytes); Assert.Equal(2048, usage.ReclaimableBytes);
            var cleanup = await _cache.ClearUnused(); Assert.Equal(2048, cleanup.FreedBytes); Assert.Equal(1, cleanup.SkippedFiles); Assert.Empty(cleanup.Errors);
            Assert.True(File.Exists(active)); Assert.False(File.Exists(unused)); Assert.False(File.Exists(unused + ".size")); Assert.Equal("keep", File.ReadAllText(unrelated));
        }
        finally { playback.Dispose(); }
        Assert.Equal(4096, (await _cache.ClearUnused()).FreedBytes); Assert.Equal(0, _cache.Inspect().Bytes);
    }
    [Fact]
    public async Task OnlyManagedPartialFilesAreCountedAndCleaned()
    {
        string partial = Path.Combine(_folder, new string('a', 64) + ".mp4." + Guid.NewGuid().ToString("N") + ".partial"); File.WriteAllBytes(partial, new byte[512]);
        string unknown = Path.Combine(_folder, "download.partial"); File.WriteAllText(unknown, "keep");
        var usage = _cache.Inspect(); Assert.Equal(1, usage.Partials); Assert.Equal(512, usage.ReclaimableBytes);
        Assert.Equal(512, (await _cache.ClearUnused()).FreedBytes); Assert.False(File.Exists(partial)); Assert.True(File.Exists(unknown));
    }
    [Fact]
    public async Task CleanupCannotInterruptEncodingOrWaitIndefinitely()
    {
        string source = Path.Combine(_folder, "source.mp4"); File.WriteAllText(source, "original");
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var encoder = new WallpaperVideoCache(_folder, async (_, output, progress, token) => { File.WriteAllBytes(output, new byte[2048]); started.SetResult(true); await Task.Delay(-1, token); return output; });
        using var cancellation = new CancellationTokenSource(); var encoding = encoder.Prepare(source, new Progress<double>(), cancellation.Token); await started.Task;
        try { await Assert.ThrowsAsync<InvalidOperationException>(() => _cache.ClearUnused()); Assert.True(File.Exists(source)); }
        finally { cancellation.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => encoding); }
    }
    [Fact]
    public async Task CleanupReportsFailureAndContinuesWithOtherEntries()
    {
        string locked = Video('a', 4096), free = Video('b', 2048); File.SetAttributes(locked, FileAttributes.ReadOnly);
        try
        {
            var cleanup = await _cache.ClearUnused(); Assert.Equal(2048, cleanup.FreedBytes); Assert.Single(cleanup.Errors); Assert.True(File.Exists(locked)); Assert.False(File.Exists(free));
        }
        finally { File.SetAttributes(locked, FileAttributes.Normal); }
    }
    [Fact]
    public void PlaybackLeaseCannotProtectAnArbitraryFile()
    {
        string outside = Path.Combine(Path.GetTempPath(), new string('a', 64) + ".mp4");
        Assert.Throws<ArgumentException>(() => _cache.HoldPlayback(outside));
    }
    [Fact]
    public async Task AbsentCacheIsAnEmptySuccess()
    {
        var cache = new WallpaperVideoCache(Path.Combine(_folder, "absent")); Assert.Equal(0, cache.Inspect().Bytes); Assert.Equal(0, (await cache.ClearUnused()).FreedBytes);
        Assert.False(Directory.Exists(Path.Combine(_folder, "absent")));
    }
    public void Dispose() => Directory.Delete(_folder, true);
}
