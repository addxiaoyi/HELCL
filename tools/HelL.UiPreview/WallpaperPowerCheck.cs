using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using HelL;
using HelL.Core.Launcher;
using HelL.Services;

internal static class WallpaperPowerCheck
{
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
    private sealed class Sample
    {
        public string Phase { get; set; } = "";
        public int Frost { get; set; }
        public double VideoSeconds { get; set; }
        public bool Active { get; set; }
        public bool Allowed { get; set; }
        public double CpuCorePercent { get; set; }
        public double PrivateMiB { get; set; }
        public double? BusiestGpuEnginePercent { get; set; }
    }
    private static readonly List<Sample> Samples = new();
    private static T Static<T>(string name) => (T)typeof(Wallpaper).GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
    private static void Check(bool condition, string message) => MaterialControlsPreview.Check(condition, message);

    internal static async Task Run(MainWindow window, string folder, bool optimized = false, string? seed = null)
    {
        Directory.CreateDirectory(folder); Motion.Enabled = false;
        if (seed != null)
        {
            string destination = Path.Combine(Utils.ROOT, "cache", "wallpaper-video"); Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(seed).Where(file => file.EndsWith(".mp4") || file.EndsWith(".size"))) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        }
        string config = WallpaperEngine.DiscoverConfigs().FirstOrDefault() ?? throw new IOException("Wallpaper Engine config is unavailable.");
        var paper = WallpaperEngine.Read(config).FirstOrDefault(item => item.Kind == "video" && File.Exists(item.File)) ?? throw new IOException("No actual video wallpaper is available.");
        string configHash = File.ReadAllText(config);
        var engineIds = Process.GetProcessesByName("wallpaper64").Select(process => process.Id).ToArray();
        Console.WriteLine("[VIDEO] " + paper.Title + "; " + paper.File);
        window.Navigate("settings", instant: true); await Program.WaitForPage(window, "settings");
        await AppServices.Client.CallAsync("save_settings", new { data = new Dictionary<string, object> { ["ui_wallpaper_mode"] = "engine", ["ui_wallpaper_engine_config"] = config, ["ui_wallpaper_monitor"] = paper.Monitor, ["ui_wallpaper_engine_live"] = false, ["ui_wallpaper_video_optimize"] = optimized } });
        window.Activate(); await MaterialControlsPreview.Until(() => window.IsActive, "test launcher is in the foreground");
        await Wallpaper.ReloadAsync(); await MaterialControlsPreview.Until(() => Wallpaper.VideoReady, "real Wallpaper Engine video opens");
        if (optimized)
        {
            await MaterialControlsPreview.Until(() => Wallpaper.VideoStatus.StartsWith("正在使用节能缓存") && Wallpaper.VideoReady, "optimized wallpaper cache is ready", 120);
            Check(Wallpaper.PlaybackSource != paper.File && Wallpaper.CurrentSource == paper.File, "cached playback preserves the original selection");
            File.WriteAllText(Path.Combine(folder, "cache-directory.txt"), Path.GetDirectoryName(Wallpaper.PlaybackSource)!);
            var encoded = Static<MediaElement>("_video"); Check(encoded.NaturalVideoWidth <= 1920 && encoded.NaturalVideoHeight <= 1080, "actual player decodes at most 1080p");
        }
        Console.WriteLine("[VIDEO CACHE] " + Wallpaper.VideoStatus + "; " + Wallpaper.PlaybackSource);
        var video = Static<MediaElement>("_video");
        var helper = new Window { Title = "HelL · 壁纸失焦验证", Width = 300, Height = 120, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterScreen, Content = new TextBlock { Text = "正在验证启动器失焦暂停", Margin = new Thickness(24), VerticalAlignment = VerticalAlignment.Center } };
        try
        {
            Glass.SetMaterial(100, 0); await Task.Delay(1500);
            await Measure(window, video, "focused-frost-0", folder);
            Glass.SetMaterial(100, 18); await Task.Delay(1000);
            await Measure(window, video, "focused-clear-glass", folder);
            Glass.SetMaterial(100, 30); await Task.Delay(1000);
            await Measure(window, video, "focused-default-frost", folder);
            Glass.SetMaterial(100, 60); await Task.Delay(1000);
            await Measure(window, video, "focused-frost-60", folder);
            helper.Show(); helper.Activate(); await MaterialControlsPreview.Until(() => !window.IsActive, "helper window causes actual focus loss");
            await Task.Delay(800); await Measure(window, video, "unfocused", folder, paused: true);
            Check(!Static<DispatcherTimer>("_sync").IsEnabled, "external wallpaper polling sleeps while unfocused");
            window.WindowState = WindowState.Minimized; await Task.Delay(500);
            Check(!Wallpaper.PlaybackAllowed, "minimized window cannot play");
            ShowWindow(new WindowInteropHelper(window).Handle, 4); await Task.Delay(800);
            Check(window.WindowState == WindowState.Normal && !window.IsActive && !Wallpaper.PlaybackAllowed, "restore without activation keeps video paused");
            await Measure(window, video, "restored-unfocused", folder, paused: true);
            video.RaiseEvent(new RoutedEventArgs(MediaElement.MediaEndedEvent)); await Task.Delay(700);
            Check(video.Position.TotalSeconds < .2 && !Wallpaper.PlaybackAllowed, "simulated end-of-loop callback cannot restart an unfocused video");
            window.Activate(); await MaterialControlsPreview.Until(() => window.IsActive && Wallpaper.PlaybackAllowed, "focus restores playback permission");
            await Task.Delay(1200); await Measure(window, video, "refocused", folder);
            var hwnd = new WindowInteropHelper(window).Handle;
            Backdrop.PmSetMoving(hwnd, true); await Task.Delay(700);
            double before = video.Position.TotalSeconds; await Task.Delay(800);
            Check(Math.Abs(video.Position.TotalSeconds - before) < .2 && !Wallpaper.PlaybackAllowed, "window-move pause stops the actual video clock");
            helper.Activate(); await MaterialControlsPreview.Until(() => !window.IsActive, "window loses focus during move");
            Backdrop.PmSetMoving(hwnd, false); await Task.Delay(500);
            Check(!Wallpaper.PlaybackAllowed && Math.Abs(video.Position.TotalSeconds - before) < .2, "ending a move cannot override focus loss");
            window.Activate(); await MaterialControlsPreview.Until(() => window.IsActive, "focus recovers after move");
            Check(Static<DispatcherTimer>("_sync").IsEnabled, "external polling resumes with the launcher");

            string slides = Path.Combine(folder, "slideshow"); Directory.CreateDirectory(slides);
            string image = WallpaperEngine.Read(config).First(item => File.Exists(item.Preview)).Preview;
            File.Copy(image, Path.Combine(slides, "wallpaper" + Path.GetExtension(image)), true);
            await AppServices.Client.CallAsync("save_settings", new { data = new Dictionary<string, object> { ["ui_wallpaper_mode"] = "local", ["ui_background_folder"] = slides } });
            await Wallpaper.ReloadAsync(); Check(Static<DispatcherTimer>("_rotate").IsEnabled, "slideshow timer runs while focused");
            helper.Activate(); await MaterialControlsPreview.Until(() => !window.IsActive, "slideshow loses focus");
            Check(!Static<DispatcherTimer>("_rotate").IsEnabled, "slideshow timer sleeps when unfocused");
            window.Activate(); await MaterialControlsPreview.Until(() => window.IsActive, "slideshow regains focus");
            Check(Static<DispatcherTimer>("_rotate").IsEnabled, "slideshow resumes on focus");
        }
        finally { helper.Close(); Wallpaper.Shutdown(); }
        Check(File.ReadAllText(config) == configHash, "original Wallpaper Engine config remains unchanged");
        foreach (int id in engineIds) using (var process = Process.GetProcessById(id)) Check(!process.HasExited, "existing desktop wallpaper process remains running");
        File.WriteAllText(Path.Combine(folder, "power-samples.json"), JsonSerializer.Serialize(Samples, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("[POWER] Real video clock, process CPU/private memory and busiest per-process GPU engine recorded; capture-session GPU is not covered.");
    }

    private static async Task Measure(MainWindow window, MediaElement video, string phase, string folder, bool paused = false)
    {
        using var process = Process.GetCurrentProcess();
        var counters = await Task.Run(() =>
        {
            var category = new PerformanceCounterCategory("GPU Engine");
            return category.GetInstanceNames().Where(name => name.StartsWith("pid_" + process.Id + "_", StringComparison.OrdinalIgnoreCase))
                .Select(name => new PerformanceCounter("GPU Engine", "Utilization Percentage", name, true)).ToArray();
        });
        if (counters.Length == 0) Console.WriteLine("[GPU UNAVAILABLE] No engine counters for this process.");
        double startPosition = video.Position.TotalSeconds; var clock = Stopwatch.StartNew(); TimeSpan cpu = process.TotalProcessorTime;
        try
        {
            await Task.Run(() => { foreach (var counter in counters) counter.NextValue(); });
            for (int index = 0; index < 6; index++)
            {
                await Task.Delay(1000);
                double? gpu = counters.Length == 0 ? null : await Task.Run(() => (double)counters.Max(counter => counter.NextValue()));
                process.Refresh(); TimeSpan currentCpu = process.TotalProcessorTime;
                var sample = new Sample { Phase = phase, Frost = Glass.Frost, VideoSeconds = video.Position.TotalSeconds, Active = window.IsActive, Allowed = Wallpaper.PlaybackAllowed,
                    CpuCorePercent = (currentCpu - cpu).TotalMilliseconds / clock.Elapsed.TotalMilliseconds * 100, PrivateMiB = process.PrivateMemorySize64 / 1048576d, BusiestGpuEnginePercent = gpu };
                Samples.Add(sample); cpu = currentCpu; clock.Restart();
                Check(paused ? !sample.Allowed && !sample.Active : sample.Allowed && sample.Active, phase + " preserves the measured activity state " + index);
            }
        }
        finally { foreach (var counter in counters) counter.Dispose(); }
        var samples = Samples.Where(sample => sample.Phase == phase).ToList();
        Check(paused ? Math.Abs(video.Position.TotalSeconds - startPosition) < .2 : samples.Select(sample => sample.VideoSeconds).Distinct().Count() > 4, phase + " video position " + (paused ? "stays fixed" : "advances"));
        File.WriteAllText(Path.Combine(folder, "power-samples.json"), JsonSerializer.Serialize(Samples, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"[POWER] {phase}: GPU busiest engine mean={samples.Average(sample => sample.BusiestGpuEnginePercent):F3}%, CPU one-core mean={samples.Average(sample => sample.CpuCorePercent):F3}%, private={samples.Last().PrivateMiB:F1} MiB");
    }
}
