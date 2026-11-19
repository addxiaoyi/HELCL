using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HelL;
using HelL.Core.Launcher;
using HelL.Services;

internal static class MaterialPerformanceCheck
{
    private sealed class Sample
    {
        public string Phase { get; set; } = "";
        public bool Active { get; set; }
        public double CpuCorePercent { get; set; }
        public double CpuTotalPercent { get; set; }
        public double PrivateMiB { get; set; }
        public double WorkingMiB { get; set; }
        public double? GpuBusiestPercent { get; set; }
        public double VideoSeconds { get; set; }
    }

    internal static async Task Run(MainWindow window, string folder, string mode)
    {
        Directory.CreateDirectory(folder); Motion.Enabled = false; window.Title = "HelL · 材质性能采样 " + mode;
        string config = WallpaperEngine.DiscoverConfigs().First();
        var paper = WallpaperEngine.Read(config).First(item => item.Kind == "video" && File.Exists(item.File) && File.Exists(item.Preview));
        string originalConfig = File.ReadAllText(config);
        string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HelL", "cache", "wallpaper-video");
        if (mode == "video")
        {
            string destination = Path.Combine(Utils.ROOT, "cache", "wallpaper-video"); Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(cache).Where(file => file.EndsWith(".mp4") || file.EndsWith(".size")))
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        }
        await AppServices.Client.CallAsync("save_settings", new { data = new Dictionary<string, object> {
            ["ui_wallpaper_mode"] = mode == "desktop" ? "transparent" : "local",
            ["ui_background"] = mode == "video" ? paper.File : paper.Preview, ["ui_background_folder"] = "",
            ["ui_background_dim"] = 0, ["ui_background_blur"] = 0,
            ["ui_glass_transparency"] = mode == "desktop" ? 100 : 82, ["ui_glass_cards"] = mode == "desktop" ? 100 : 68,
            ["ui_glass_controls"] = mode == "desktop" ? 100 : 42, ["ui_glass_popups"] = 10,
            ["ui_glass_frost"] = mode == "desktop" ? 0 : 18, ["ui_wallpaper_video_optimize"] = true } });
        window.Navigate("settings", instant: true); await Program.WaitForPage(window, "settings");
        await Wallpaper.ReloadAsync(); await MaterialControlsPreview.Layout(window);
        if (mode == "desktop" && !Backdrop.ClearAvailable) throw new InvalidOperationException("Desktop layered window was not created before the HWND.");
        if (mode == "video") await MaterialControlsPreview.Until(() => Wallpaper.VideoReady && Wallpaper.PlaybackSource != paper.File, "optimized wallpaper ready", 120);
        var video = typeof(Wallpaper).GetField("_video", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null) as MediaElement;
        using var process = Process.GetCurrentProcess();
        var counters = await Task.Run(() =>
        {
            try { return new PerformanceCounterCategory("GPU Engine").GetInstanceNames().Where(name => name.StartsWith("pid_" + process.Id + "_", StringComparison.OrdinalIgnoreCase))
                .Select(name => new PerformanceCounter("GPU Engine", "Utilization Percentage", name, true)).ToArray(); }
            catch (Exception error) { Console.WriteLine("[GPU UNAVAILABLE] " + error.Message); return Array.Empty<PerformanceCounter>(); }
        });
        var samples = new List<Sample>();
        var helper = new Window { Title = "HelL · 失焦采样", Width = 280, Height = 100, ShowInTaskbar = false,
            Content = new TextBlock { Text = "正在采样启动器失焦占用", Margin = new Thickness(20) } };
        async Task Measure(string phase, bool active, int count)
        {
            foreach (var counter in counters) counter.NextValue();
            TimeSpan cpu = process.TotalProcessorTime; var clock = Stopwatch.StartNew();
            double initialVideo = video?.Position.TotalSeconds ?? 0;
            for (int i = 0; i < count; i++)
            {
                await Task.Delay(1000); process.Refresh();
                double elapsed = clock.Elapsed.TotalMilliseconds; TimeSpan current = process.TotalProcessorTime;
                double? gpu = counters.Length == 0 ? null : await Task.Run(() => (double)counters.Max(counter => counter.NextValue()));
                double percent = (current - cpu).TotalMilliseconds / elapsed * 100;
                samples.Add(new Sample { Phase = phase, Active = window.IsActive, CpuCorePercent = percent, CpuTotalPercent = percent / Environment.ProcessorCount,
                    PrivateMiB = process.PrivateMemorySize64 / 1048576d, WorkingMiB = process.WorkingSet64 / 1048576d,
                    GpuBusiestPercent = gpu, VideoSeconds = video?.Position.TotalSeconds ?? 0 });
                cpu = current; clock.Restart();
                File.WriteAllText(Path.Combine(folder, "samples.json"), JsonSerializer.Serialize(samples, new JsonSerializerOptions { WriteIndented = true }));
                if (window.IsActive != active) throw new InvalidOperationException("Focus changed during " + phase + "; discard this run.");
            }
            if (mode == "video" && (active ? Math.Abs(video!.Position.TotalSeconds - initialVideo) < 2 : Math.Abs(video!.Position.TotalSeconds - initialVideo) > .2))
                throw new InvalidOperationException("Video activity did not match the measured phase.");
            var phaseSamples = samples.Where(item => item.Phase == phase).ToArray();
            Console.WriteLine($"[PERF] {mode}/{phase}: CPU core={phaseSamples.Average(item => item.CpuCorePercent):F2}%; GPU={phaseSamples.Average(item => item.GpuBusiestPercent):F2}%; private={phaseSamples.Average(item => item.PrivateMiB):F1}MiB");
        }
        try
        {
            window.Activate(); await MaterialControlsPreview.Until(() => window.IsActive, "foreground established");
            await Task.Delay(4000); await Measure("focused", true, 12);
            helper.Show(); helper.Activate(); await MaterialControlsPreview.Until(() => !window.IsActive, "actual focus loss");
            await Task.Delay(1200); await Measure("unfocused", false, 8);
            if (File.ReadAllText(config) != originalConfig) throw new InvalidOperationException("Wallpaper Engine configuration changed.");
            File.WriteAllText(Path.Combine(folder, "completed.json"), JsonSerializer.Serialize(new {
                mode, pid = process.Id, processors = Environment.ProcessorCount, dpi = VisualTreeHelper.GetDpi(window).PixelsPerInchX,
                width = window.ActualWidth, height = window.ActualHeight, wallpaper = paper.Title,
                source = Wallpaper.CurrentSource, playback = Wallpaper.PlaybackSource, allowsTransparency = window.AllowsTransparency,
                scope = "launcher process only; excludes shared DWM and Wallpaper Engine costs; GPU busiest engine, not summed" }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { foreach (var counter in counters) counter.Dispose(); helper.Close(); Wallpaper.Shutdown(); }
    }
}
