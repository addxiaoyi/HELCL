using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HelL.Core.Launcher;

public sealed class WallpaperCacheUsage
{
    public int Videos { get; internal set; }
    public int Partials { get; internal set; }
    public long Bytes { get; internal set; }
    public long InUseBytes { get; internal set; }
    public long ReclaimableBytes => Bytes - InUseBytes;
}

public sealed class WallpaperCacheCleanup
{
    public long FreedBytes { get; internal set; }
    public int RemovedFiles { get; internal set; }
    public int SkippedFiles { get; internal set; }
    public string[] Errors { get; internal set; } = Array.Empty<string>();
}

public sealed partial class WallpaperVideoCache
{
    private sealed class CacheEntry
    {
        public string Path { get; init; } = "";
        public string Target { get; init; } = "";
        public long Bytes { get; init; }
        public bool Partial { get; init; }
        public DateTime LastUsedUtc { get; init; }
    }

    public WallpaperVideoCache(string folder, WallpaperCachePolicy? policy = null) : this(folder, (_, _, _, _) => throw new InvalidOperationException("未配置视频编码器。"), policy) { }

    public WallpaperCacheUsage Inspect()
    {
        var usage = new WallpaperCacheUsage();
        if (!SafeDirectory()) return usage;
        foreach (var entry in Inventory())
        {
            if (entry.Partial) usage.Partials++; else usage.Videos++;
            usage.Bytes += entry.Bytes;
            using var lease = TryLease(entry.Target);
            if (lease == null) usage.InUseBytes += entry.Bytes;
        }
        return usage;
    }

    public Task<WallpaperCacheCleanup> ClearUnused(CancellationToken cancellation = default) => Prune(true, cancellation);
    public Task<WallpaperCacheCleanup> EnforcePolicy(CancellationToken cancellation = default) => Prune(false, cancellation);

    private async Task<WallpaperCacheCleanup> Prune(bool all, CancellationToken cancellation)
    {
        var cleanup = new WallpaperCacheCleanup();
        if (!SafeDirectory()) return cleanup;
        if (!await Gate.WaitAsync(0, cancellation).ConfigureAwait(false)) throw new InvalidOperationException("视频正在生成缓存，请稍后重试。");
        try
        {
            using var cacheLease = new FileStream(Path.Combine(_folder, ".cache.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var errors = new List<string>();
            var entries = Inventory().OrderBy(entry => entry.LastUsedUtc).ToList();
            long bytes = entries.Sum(entry => entry.Bytes);
            foreach (var entry in entries)
            {
                cancellation.ThrowIfCancellationRequested();
                if (!all && !entry.Partial && bytes <= _policy.CapacityBytes && !_policy.Expired(entry.LastUsedUtc, DateTime.UtcNow)) continue;
                using var lease = TryLease(entry.Target);
                if (lease == null) { cleanup.SkippedFiles++; continue; }
                try
                {
                    var current = new FileInfo(entry.Path);
                    if (!current.Exists || (current.Attributes & FileAttributes.ReparsePoint) != 0) { cleanup.SkippedFiles++; continue; }
                    long deletedBytes = current.Length;
                    current.Delete(); cleanup.FreedBytes += deletedBytes; cleanup.RemovedFiles++; bytes -= entry.Bytes;
                    if (!entry.Partial && File.Exists(entry.Path + ".size") && (File.GetAttributes(entry.Path + ".size") & FileAttributes.ReparsePoint) == 0)
                        File.Delete(entry.Path + ".size");
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                { System.Diagnostics.Trace.TraceWarning("Wallpaper cache clear: " + ex); errors.Add(Path.GetFileName(entry.Path) + ": " + ex.Message); }
            }
            cleanup.Errors = errors.ToArray();
            return cleanup;
        }
        catch (IOException ex) { throw new IOException("视频缓存正在被另一个启动器使用，请稍后重试。", ex); }
        finally { Gate.Release(); }
    }

    public IDisposable HoldPlayback(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (!SafeDirectory() || !Path.GetDirectoryName(fullPath)!.Equals(_folder, StringComparison.OrdinalIgnoreCase) || !CacheKey(Path.GetFileNameWithoutExtension(fullPath)) || !fullPath.EndsWith(".mp4", StringComparison.Ordinal))
            throw new ArgumentException("播放缓存路径无效。", nameof(path));
        if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0) throw new IOException("播放缓存不能是链接。");
        return new FileStream(fullPath + ".lock", FileMode.OpenOrCreate, FileAccess.Read, FileShare.Read);
    }

    private bool SafeDirectory()
    {
        if (!Directory.Exists(_folder)) return false;
        if ((File.GetAttributes(_folder) & FileAttributes.ReparsePoint) != 0) throw new IOException("视频缓存目录不能是链接。");
        return true;
    }

    private IEnumerable<CacheEntry> Inventory()
    {
        foreach (var file in new DirectoryInfo(_folder).EnumerateFiles())
        {
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0) continue;
            if (file.Extension == ".mp4" && CacheKey(Path.GetFileNameWithoutExtension(file.Name)))
            { yield return new CacheEntry { Path = file.FullName, Target = file.FullName, Bytes = file.Length, LastUsedUtc = file.LastWriteTimeUtc }; continue; }
            var parts = file.Name.Split('.');
            if (parts.Length == 4 && CacheKey(parts[0]) && parts[1] == "mp4" && parts[3] == "partial" && Guid.TryParseExact(parts[2], "N", out _))
                yield return new CacheEntry { Path = file.FullName, Target = Path.Combine(_folder, parts[0] + ".mp4"), Bytes = file.Length, Partial = true, LastUsedUtc = file.LastWriteTimeUtc };
        }
    }

    private static FileStream? TryLease(string target)
    {
        try { return new FileStream(target + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException ex) { System.Diagnostics.Trace.TraceInformation("Wallpaper cache retained: " + ex.Message); return null; }
    }
}
