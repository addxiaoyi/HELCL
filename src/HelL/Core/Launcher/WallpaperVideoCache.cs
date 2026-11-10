using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HelL.Core.Launcher;

public sealed partial class WallpaperVideoCache
{
    public const long MaxBytes = 512L * 1024 * 1024;
    public const long MaxVideoBytes = 256L * 1024 * 1024;
    private readonly string _folder;
    private readonly Func<string, string, IProgress<double>, CancellationToken, Task<string>> _encode;
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly WallpaperCachePolicy _policy;

    public WallpaperVideoCache(string folder, Func<string, string, IProgress<double>, CancellationToken, Task<string>> encode, WallpaperCachePolicy? policy = null)
    {
        _folder = Path.GetFullPath(folder);
        _encode = encode ?? throw new ArgumentNullException(nameof(encode));
        _policy = policy ?? WallpaperCachePolicy.Default;
    }

    public string CachePath(string source)
    {
        var file = new FileInfo(source);
        if (!file.Exists) throw new FileNotFoundException("壁纸视频已移动或删除。", source);
        string identity = file.FullName.ToUpperInvariant() + "\n" + file.Length + "\n" + file.LastWriteTimeUtc.Ticks + "\nh264-1080p30-v1";
        using var sha = SHA256.Create();
        string key = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(identity))).Replace("-", "").ToLowerInvariant();
        return Path.Combine(_folder, key + ".mp4");
    }

    public string? Cached(string source)
    {
        string path = CachePath(source);
        if (!Valid(path)) return null;
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        return path;
    }

    public async Task<string> Prepare(string source, IProgress<double> progress, CancellationToken cancellation)
    {
        string target = CachePath(source);
        await Gate.WaitAsync(cancellation).ConfigureAwait(false);
        string partial = target + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            Directory.CreateDirectory(_folder);
            if ((File.GetAttributes(_folder) & FileAttributes.ReparsePoint) != 0) throw new IOException("视频缓存目录不能是链接。");
            using var cacheLease = new FileStream(Path.Combine(_folder, ".cache.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            ClearAbandoned();
            using var lease = new FileStream(target + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            if (Valid(target)) { File.SetLastWriteTimeUtc(target, DateTime.UtcNow); return target; }
            if (File.Exists(target))
            {
                if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0) throw new IOException("视频缓存文件不能是链接。");
                File.Delete(target);
            }
            if (File.Exists(target + ".size")) File.Delete(target + ".size");
            Trim(_policy.CapacityBytes - MaxVideoBytes, target);
            if (Entries().Sum(file => file.Length) > _policy.CapacityBytes - MaxVideoBytes) throw new IOException("视频缓存正在使用，暂时无法腾出空间。");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));
            var output = await _encode(source, partial, progress, timeout.Token).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            if (output == source) return source;
            if (output != partial || !File.Exists(partial)) throw new IOException("未收到完整的视频缓存。");
            var file = new FileInfo(partial);
            if (file.Length < 1024 || file.Length > MaxVideoBytes) throw new IOException("视频缓存超出大小限制。");
            if (target != CachePath(source)) throw new IOException("原视频已变化，取消本次缓存。");
            if (File.Exists(target)) File.Delete(target);
            File.Move(partial, target);
            File.WriteAllText(target + ".size", file.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Trim(_policy.CapacityBytes, target);
            return target;
        }
        finally
        {
            try { if (File.Exists(partial)) File.Delete(partial); }
            finally { Gate.Release(); }
        }
    }

    private static bool Valid(string path)
    {
        if (!File.Exists(path) || !File.Exists(path + ".size")) return false;
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return false;
        return long.TryParse(File.ReadAllText(path + ".size"), out long length) && length >= 1024 && length <= MaxVideoBytes && new FileInfo(path).Length == length;
    }

    private void ClearAbandoned()
    {
        foreach (var file in new DirectoryInfo(_folder).EnumerateFiles("*.partial"))
        {
            var parts = file.Name.Split('.');
            if (parts.Length != 4 || !CacheKey(parts[0]) || parts[1] != "mp4" || !Guid.TryParseExact(parts[2], "N", out _) || (file.Attributes & FileAttributes.ReparsePoint) != 0) continue;
            try
            {
                using (new FileStream(Path.Combine(_folder, parts[0] + ".mp4.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) file.Delete();
            }
            catch (IOException ex) { System.Diagnostics.Trace.TraceWarning("Video partial still in use: " + ex.Message); }
        }
    }

    private void Trim(long budget, string active)
    {
        var files = Entries().OrderBy(file => file.LastWriteTimeUtc).ToList();
        long bytes = files.Sum(file => file.Length);
        foreach (var file in files)
        {
            if (bytes <= budget && !_policy.Expired(file.LastWriteTimeUtc, DateTime.UtcNow)) continue;
            if (file.FullName.Equals(active, StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                long length = file.Length;
                // A second launcher may still be reading this cache entry.
                using (new FileStream(file.FullName + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                { file.Delete(); if (File.Exists(file.FullName + ".size")) File.Delete(file.FullName + ".size"); }
                bytes -= length;
            }
            catch (IOException ex) { System.Diagnostics.Trace.TraceWarning("Video cache cleanup: " + ex.Message); }
        }
    }

    private System.Collections.Generic.IEnumerable<FileInfo> Entries() => new DirectoryInfo(_folder).EnumerateFiles("*.mp4")
        .Where(file => CacheKey(Path.GetFileNameWithoutExtension(file.Name)) && (file.Attributes & FileAttributes.ReparsePoint) == 0);
    private static bool CacheKey(string name) => name.Length == 64 && name.All(character => character >= '0' && character <= '9' || character >= 'a' && character <= 'f');
}
