using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HelL.Core.Launcher;

namespace HelL.Services;

public static partial class Wallpaper
{
    private static bool _optimizeVideo = true;
    private static bool _videoPolicyChanged;
    private static CancellationTokenSource? _videoPreparation;
    private static IDisposable? _videoPlaybackLease;
    public static string VideoStatus { get; private set; } = "";
    public static string PlaybackSource => _video?.Source?.LocalPath ?? "";
    public static WallpaperCachePolicy CachePolicy { get; private set; } = WallpaperCachePolicy.Default;
    public static void SetCachePolicy(WallpaperCachePolicy policy) { CachePolicy = policy ?? throw new ArgumentNullException(nameof(policy)); StatusChanged?.Invoke(); }
    private static readonly Lazy<MethodInfo> VideoEncoder = new(() =>
    {
        using var stream = typeof(Wallpaper).Assembly.GetManifestResourceStream("HelL.WallpaperCapture.dll") ?? throw new IOException("视频优化组件缺失，请重新安装启动器。");
        using var bytes = new MemoryStream(); stream.CopyTo(bytes);
        return Assembly.Load(bytes.ToArray()).GetType("HelL.WallpaperRuntime.VideoEncoder", true)!.GetMethod("Encode")!;
    });

    private static async Task PrepareVideo(string source)
    {
        if (!_optimizeVideo) { StartVideo(source); return; }
        var cancellation = new CancellationTokenSource(); _videoPreparation = cancellation;
        try
        {
            var cache = new WallpaperVideoCache(Path.Combine(Utils.ROOT, "cache", "wallpaper-video"),
                (input, output, report, token) => (Task<string>)VideoEncoder.Value.Invoke(null, new object[] { input, output, report, token })!, CachePolicy);
            string? cached = cache.Cached(source);
            if (cached != null) { StartVideo(cached, fallback: source); VideoStatus = "正在使用节能缓存 · 1080p / 30 帧以内"; StatusChanged?.Invoke(); return; }
            StartVideo(source);
            VideoStatus = "正在准备节能播放…"; StatusChanged?.Invoke();
            var progress = new Progress<double>(percent =>
            {
                if (_videoPreparation != cancellation || cancellation.IsCancellationRequested) return;
                string status = "正在生成节能缓存 · " + percent.ToString("0") + "%";
                if (VideoStatus != status) { VideoStatus = status; StatusChanged?.Invoke(); }
            });
            string path = await Task.Run(() => cache.Prepare(source, progress, cancellation.Token));
            if (_videoPreparation != cancellation || cancellation.IsCancellationRequested || _source != source) return;
            if (path != source)
            {
                var position = _video?.Position ?? TimeSpan.Zero;
                StopVideo(); StartVideo(path, position, fallback: source);
                VideoStatus = "正在使用节能缓存 · 1080p / 30 帧以内";
            }
            else VideoStatus = "原视频适合直接播放";
            StatusChanged?.Invoke();
        }
        catch (OperationCanceledException) { if (_videoPreparation == cancellation) { VideoStatus = "已取消缓存，继续播放原视频"; StatusChanged?.Invoke(); } }
        catch (Exception ex)
        {
            Smoke.Note("wallpaper-video-cache", ex.ToString());
            if (_videoPreparation == cancellation) { if (_video == null) StartVideo(source); VideoStatus = "节能缓存暂不可用，继续播放原视频"; StatusChanged?.Invoke(); }
        }
        finally { if (_videoPreparation == cancellation) _videoPreparation = null; cancellation.Dispose(); }
    }

    private static void CancelVideoPreparation()
    {
        var cancellation = _videoPreparation; _videoPreparation = null; cancellation?.Cancel(); VideoStatus = "";
    }

    private static void InvalidateVideoCache(string path)
    {
        string folder = Path.Combine(Utils.ROOT, "cache", "wallpaper-video");
        if (!string.Equals(Path.GetDirectoryName(path), folder, StringComparison.OrdinalIgnoreCase)) return;
        try { if (File.Exists(path + ".size")) File.Delete(path + ".size"); }
        catch (IOException ex) { Smoke.Note("wallpaper-video-cache-invalidate", ex.ToString()); }
    }
}
