using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using HelL;
using HelL.Core.Launcher;
using HelL.Services;

internal static class VideoLifecycleCheck
{
    internal static async Task Run(MainWindow window, string folder, string seed)
    {
        Directory.CreateDirectory(folder); Motion.Enabled = false; window.Activate();
        var papers = WallpaperEngine.Read(WallpaperEngine.DiscoverConfigs().First());
        string source = papers.First(paper => paper.Kind == "video").File;
        string still = papers.First(paper => File.Exists(paper.Preview)).Preview;
        await AppServices.Client.CallAsync("save_settings", new { data = new Dictionary<string, object> { ["ui_wallpaper_mode"] = "local", ["ui_background"] = source, ["ui_wallpaper_video_optimize"] = true } });
        await Wallpaper.ReloadAsync();
        await MaterialControlsPreview.Until(() => Wallpaper.VideoStatus.StartsWith("正在生成节能缓存"), "native encoding starts", 20);
        Wallpaper.SetSource(still); await Task.Delay(2000);
        Check(Wallpaper.CurrentSource == still && Wallpaper.PlaybackSource == "" && Wallpaper.VideoStatus == "", "cancelled video cannot replace the new still wallpaper");
        string cache = Path.Combine(Utils.ROOT, "cache", "wallpaper-video");
        Check(Directory.GetFiles(cache, "*.partial").Length == 0, "cancelling native transcode removes partial output");
        foreach (string file in Directory.GetFiles(seed).Where(file => file.EndsWith(".mp4") || file.EndsWith(".size"))) File.Copy(file, Path.Combine(cache, Path.GetFileName(file)), true);
        await Wallpaper.ReloadAsync(); await MaterialControlsPreview.Until(() => Wallpaper.VideoReady && Wallpaper.PlaybackSource != source, "previously generated cache opens immediately");
        string cached = Wallpaper.PlaybackSource; var cacheTime = File.GetCreationTimeUtc(cached);
        await AppServices.Client.CallAsync("save_settings", new { data = new { ui_wallpaper_video_optimize = false } });
        await Wallpaper.ReloadAsync(); await MaterialControlsPreview.Until(() => Wallpaper.PlaybackSource == source && Wallpaper.VideoReady, "disabling optimization restores original playback");
        await AppServices.Client.CallAsync("save_settings", new { data = new { ui_wallpaper_video_optimize = true } });
        await Wallpaper.ReloadAsync(); await MaterialControlsPreview.Until(() => Wallpaper.PlaybackSource == cached && Wallpaper.VideoReady, "reenabling optimization reuses the same cache");
        Check(File.GetCreationTimeUtc(cached) == cacheTime, "warm playback does not reencode the file");
        Wallpaper.SetSource(still); await Task.Delay(700);
        File.WriteAllBytes(cached, new byte[2048]); File.WriteAllText(cached + ".size", "2048");
        await Wallpaper.ReloadAsync();
        await MaterialControlsPreview.Until(() => Wallpaper.PlaybackSource == source && Wallpaper.VideoReady, "undecodable cached file recovers the original video", 20);
        Check(!File.Exists(cached + ".size"), "bad cache is invalidated for the next attempt");
        Wallpaper.SetSource(still); await AppServices.Client.CallAsync("save_settings", new { data = new { ui_wallpaper_mode = "material" } });
        await Wallpaper.ReloadAsync(); Check(!Wallpaper.Active && Wallpaper.PlaybackSource.Length == 0, "material mode releases wallpaper playback");
        Console.WriteLine("[VIDEO LIFECYCLE] passed native cancellation, cache reuse, opt-out and damaged-cache recovery");
    }
    private static void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); Console.WriteLine("[PASS] " + message); }
}
