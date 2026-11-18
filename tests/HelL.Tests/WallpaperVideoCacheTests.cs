using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HelL.Core.Launcher;
using Xunit;

namespace HelL.Tests;

public sealed class WallpaperVideoCacheTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "hell-video-cache-" + Guid.NewGuid().ToString("N"));
    private readonly IProgress<double> _progress = new Progress<double>();
    public WallpaperVideoCacheTests() => Directory.CreateDirectory(_folder);
    private string Source() { string file = Path.Combine(_folder, "original.mp4"); File.WriteAllText(file, "original video fixture"); return file; }
    private WallpaperVideoCache Cache(Func<string, string, IProgress<double>, CancellationToken, Task<string>> encode) => new(Path.Combine(_folder, "cache"), encode);
    private static Task<string> Encode(string input, string output, IProgress<double> progress, CancellationToken cancellation) { File.WriteAllBytes(output, new byte[2048]); return Task.FromResult(output); }

    [Fact]
    public async Task CacheReuseDoesNotAlterOriginalAndSourceChangesInvalidateIt()
    {
        string source = Source(); int calls = 0;
        var cache = Cache((input, output, progress, token) => { calls++; return Encode(input, output, progress, token); });
        string first = await cache.Prepare(source, _progress, CancellationToken.None);
        Assert.Equal(first, await cache.Prepare(source, _progress, CancellationToken.None)); Assert.Equal(1, calls);
        Assert.Equal("original video fixture", File.ReadAllText(source));
        File.AppendAllText(source, "changed");
        Assert.NotEqual(first, await cache.Prepare(source, _progress, CancellationToken.None)); Assert.Equal(2, calls);
    }

    [Fact]
    public async Task InterruptedEncodingLeavesNoPlayableOrPartialCache()
    {
        string source = Source(); var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = Cache(async (_, output, progress, token) => { File.WriteAllBytes(output, new byte[2000]); started.SetResult(true); await Task.Delay(-1, token); return output; });
        using var cancellation = new CancellationTokenSource(); var pending = cache.Prepare(source, _progress, cancellation.Token);
        await started.Task; cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Empty(Directory.GetFiles(Path.Combine(_folder, "cache"), "*.partial")); Assert.False(File.Exists(cache.CachePath(source)));
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task TruncatedCacheIsRegenerated()
    {
        string source = Source(); int calls = 0;
        var cache = Cache((input, output, progress, token) => { calls++; return Encode(input, output, progress, token); });
        string path = await cache.Prepare(source, _progress, CancellationToken.None); File.WriteAllText(path, "broken");
        Assert.Equal(path, await cache.Prepare(source, _progress, CancellationToken.None)); Assert.Equal(2, calls); Assert.Equal(2048, new FileInfo(path).Length);
    }

    [Fact]
    public async Task SourceMutationDuringEncodeIsNeverPublished()
    {
        string source = Source(); var cache = Cache((input, output, progress, token) => { File.AppendAllText(input, "changed"); return Encode(input, output, progress, token); });
        await Assert.ThrowsAsync<IOException>(() => cache.Prepare(source, _progress, CancellationToken.None));
        Assert.Empty(Directory.GetFiles(Path.Combine(_folder, "cache"), "*.mp4")); Assert.Empty(Directory.GetFiles(Path.Combine(_folder, "cache"), "*.partial"));
    }

    [Fact]
    public async Task OversizedOutputIsRejectedAndDoesNotDeleteUnrelatedFiles()
    {
        string source = Source(); Directory.CreateDirectory(Path.Combine(_folder, "cache")); string unrelated = Path.Combine(_folder, "cache", "my-video.mp4"); File.WriteAllText(unrelated, "keep");
        var cache = Cache((_, output, progress, token) => { using (var stream = File.Create(output)) stream.SetLength(WallpaperVideoCache.MaxVideoBytes + 1); return Task.FromResult(output); });
        await Assert.ThrowsAsync<IOException>(() => cache.Prepare(source, _progress, CancellationToken.None));
        Assert.Equal("keep", File.ReadAllText(unrelated)); Assert.True(File.Exists(source)); Assert.Empty(Directory.GetFiles(Path.Combine(_folder, "cache"), "*.partial"));
    }

    [Fact]
    public async Task AlreadySuitableVideoDoesNotCreatePlayableCache()
    {
        string source = Source(); var cache = Cache((input, _, progress, token) => Task.FromResult(input));
        Assert.Equal(source, await cache.Prepare(source, _progress, CancellationToken.None)); Assert.False(File.Exists(cache.CachePath(source)));
    }

    [Fact]
    public async Task CacheBudgetEvictsOnlyManagedEntries()
    {
        string source = Source(); var cache = Cache(Encode); string folder = Path.Combine(_folder, "cache"); Directory.CreateDirectory(folder);
        string old = Path.Combine(folder, new string('a', 64) + ".mp4");
        using (var stream = File.Create(old)) stream.SetLength(WallpaperVideoCache.MaxVideoBytes + 2048);
        File.WriteAllText(old + ".size", (WallpaperVideoCache.MaxVideoBytes + 2048).ToString());
        string unrelated = Path.Combine(folder, "family-video.mp4"); File.WriteAllText(unrelated, "keep");
        string prepared = await cache.Prepare(source, _progress, CancellationToken.None);
        Assert.False(File.Exists(old)); Assert.False(File.Exists(old + ".size")); Assert.True(File.Exists(prepared)); Assert.Equal("keep", File.ReadAllText(unrelated));
    }

    [Fact]
    public async Task ActiveEntryCannotBeEvictedToMakeRoom()
    {
        string source = Source(); var cache = Cache(Encode); string folder = Path.Combine(_folder, "cache"); Directory.CreateDirectory(folder);
        string old = Path.Combine(folder, new string('a', 64) + ".mp4");
        using (var stream = File.Create(old)) stream.SetLength(WallpaperVideoCache.MaxVideoBytes + 2048);
        using var lease = new FileStream(old + ".lock", FileMode.Create, FileAccess.Write, FileShare.None);
        await Assert.ThrowsAsync<IOException>(() => cache.Prepare(source, _progress, CancellationToken.None));
        Assert.True(File.Exists(old)); Assert.False(File.Exists(cache.CachePath(source)));
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public async Task CrashResidueIsRemovedWithoutTouchingActiveOrUnrelatedPartials()
    {
        string source = Source(); var cache = Cache(Encode); string folder = Path.Combine(_folder, "cache"); Directory.CreateDirectory(folder);
        string abandoned = cache.CachePath(source) + "." + Guid.NewGuid().ToString("N") + ".partial";
        string unrelated = Path.Combine(folder, "recording.partial");
        string activeKey = new string('b', 64) + ".mp4";
        string active = Path.Combine(folder, activeKey + "." + Guid.NewGuid().ToString("N") + ".partial");
        File.WriteAllText(abandoned, "old"); File.WriteAllText(unrelated, "keep"); File.WriteAllText(active, "busy");
        using var lease = new FileStream(Path.Combine(folder, activeKey + ".lock"), FileMode.Create, FileAccess.Write, FileShare.None);
        await cache.Prepare(source, _progress, CancellationToken.None);
        Assert.False(File.Exists(abandoned)); Assert.Equal("keep", File.ReadAllText(unrelated)); Assert.Equal("busy", File.ReadAllText(active));
    }
}
