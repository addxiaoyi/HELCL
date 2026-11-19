using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using HelL;
using HelL.Core.Launcher;
using HelL.Services;

internal static class CapturePermissionCheck
{
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowDisplayAffinity(IntPtr window, out uint affinity);
    internal static async Task Run(MainWindow owner, string folder, bool picker = false)
    {
        Directory.CreateDirectory(folder);
        IntPtr ownerHandle = new WindowInteropHelper(owner).Handle;
        bool affinityRead = GetWindowDisplayAffinity(ownerHandle, out uint affinity);
        CaptureAbiCheck.Run(ownerHandle);
        Console.WriteLine("[CAPTURE SESSION] session=" + Process.GetCurrentProcess().SessionId + " remote=" + (GetSystemMetrics(0x1000) != 0) + " monitors=" + GetSystemMetrics(80) + " affinity=" + (affinityRead ? affinity.ToString() : "unavailable"));
        var desktopProcesses = Process.GetProcessesByName("wallpaper64").Select(process => process.Id).ToList();
        try { Console.WriteLine("[CAPTURE ACCESS] " + await EngineCapture.RequestAccess()); }
        catch (Exception ex) { Console.WriteLine("[CAPTURE ACCESS] " + ex); }
        await Capture(new WindowInteropHelper(owner).Handle, "launcher-self", folder);
        foreach (string config in WallpaperEngine.DiscoverConfigs())
        {
            var selected = WallpaperEngine.Read(config);
            foreach (var wallpaper in selected.Where(item => !item.Direct))
            {
                string project = Path.Combine(Path.GetDirectoryName(wallpaper.File)!, "project.json");
                using var window = await EngineWallpaperWindow.Open(config, File.Exists(project) ? project : wallpaper.File, picker);
                if (picker)
                {
                    try
                    {
                    owner.Activate(); await Task.Delay(500);
                    Console.WriteLine("[PICKER WAIT] " + wallpaper.Kind + " " + window.Title);
                    var pickTime = Stopwatch.StartNew();
                    using var capture = await EngineCapture.Pick(new WindowInteropHelper(owner).Handle, window.Title);
                    Console.WriteLine("[PICKER RETURN] " + pickTime.Elapsed.TotalSeconds.ToString("F2") + "s active=" + owner.IsActive);
                    if (capture == null) { Console.WriteLine("[PICKER CANCELLED] " + wallpaper.Kind); await VerifyClose(window); continue; }
                    var first = await capture.FirstFrame(); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(first));
                    using var file = File.Create(Path.Combine(folder, wallpaper.Kind + "-authorized.png")); encoder.Save(file);
                    Console.WriteLine("[PICKER PASS] " + wallpaper.Kind + " " + first.PixelWidth + "x" + first.PixelHeight);
                    window.Park(); int frames = 0; bool changed = false; string initial = Hash(first);
                    capture.Frame += image => { System.Threading.Interlocked.Increment(ref frames); if (Hash(image) != initial) changed = true; };
                    await Task.Delay(3000);
                    Console.WriteLine("[PICKER MOTION] " + wallpaper.Kind + " frames=" + frames + " changed=" + changed);
                    capture.Active = false; await Task.Delay(400); int paused = frames; await Task.Delay(800);
                    Console.WriteLine("[PICKER PAUSE] " + wallpaper.Kind + " frames=" + (frames - paused));
                    }
                    catch (UnauthorizedAccessException ex) { Console.WriteLine("[PICKER BLOCKED] " + wallpaper.Kind + " " + ex.Message); }
                }
                else await Capture(window.Handle, wallpaper.Kind, folder);
                await VerifyClose(window);
            }
            var after = WallpaperEngine.Read(config);
            if (!selected.All(item => after.Any(current => current.Monitor == item.Monitor && current.File == item.File))) throw new InvalidOperationException("Desktop wallpaper selection changed.");
            Console.WriteLine("[CAPTURE] Desktop selections preserved");
        }
        foreach (int id in desktopProcesses) using (var process = Process.GetProcessById(id)) if (process.HasExited) throw new InvalidOperationException("Existing desktop engine exited.");
        Console.WriteLine("[CAPTURE CLEANUP PASS] Existing desktop processes preserved");
    }
    private static async Task VerifyClose(EngineWallpaperWindow window)
    {
        IntPtr handle = window.Handle; window.Dispose();
        var end = DateTime.UtcNow.AddSeconds(5); while (IsWindow(handle) && DateTime.UtcNow < end) await Task.Delay(100);
        if (IsWindow(handle)) throw new InvalidOperationException("Dedicated wallpaper window was not closed.");
        Console.WriteLine("[CAPTURE CLEANUP PASS] Dedicated window closed");
    }
    private static string Hash(BitmapSource frame)
    {
        int stride = frame.PixelWidth * 4; var pixels = new byte[stride * frame.PixelHeight]; frame.CopyPixels(pixels, stride, 0);
        using var sha = SHA256.Create(); return Convert.ToBase64String(sha.ComputeHash(pixels));
    }
    private static async Task Capture(IntPtr handle, string name, string folder)
    {
        try
        {
            using var capture = new EngineCapture(handle);
            var first = await capture.FirstFrame();
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(first));
            using var file = File.Create(Path.Combine(folder, name + ".png")); encoder.Save(file);
            Console.WriteLine("[CAPTURE PASS] " + name + " " + first.PixelWidth + "x" + first.PixelHeight);
        }
        catch (Exception ex) { Console.WriteLine("[CAPTURE BLOCKED] " + name + " " + ex); }
    }
}
