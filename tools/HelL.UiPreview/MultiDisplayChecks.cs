using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using HelL;
using HelL.Services;

internal static class MultiDisplayChecks
{
    [StructLayout(LayoutKind.Sequential)] private struct Bounds { public int Left, Top, Right, Bottom; public int Width => Right - Left; public int Height => Bottom - Top; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Bounds Monitor, Work; public uint Flags; }
    private delegate bool MonitorCallback(IntPtr monitor, IntPtr dc, ref Bounds bounds, IntPtr state);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorCallback callback, IntPtr state);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Bounds bounds);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    internal static async Task Run(MainWindow window, string folder, bool live, bool typography = false)
    {
        Directory.CreateDirectory(folder); Motion.Enabled = false; window.Title = "HelL · 跨屏与贴靠验证";
        window.Navigate("settings", instant: true); await Program.WaitForPage(window, "settings"); Wallpaper.SetSource("");
        var monitors = new List<MonitorInfo>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr dc, ref Bounds bounds, IntPtr state) =>
        { var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() }; if (!GetMonitorInfo(monitor, ref info)) throw new Win32Exception(); monitors.Add(info); return true; }, IntPtr.Zero);
        var handle = new WindowInteropHelper(window).Handle; var dpis = new HashSet<uint>(); int index = 0;
        foreach (var monitor in monitors)
        {
            window.WindowState = WindowState.Normal;
            if (!SetWindowPos(handle, IntPtr.Zero, monitor.Work.Left + 28, monitor.Work.Top + 28, 1100, 700, 0x14)) throw new Win32Exception(Marshal.GetLastWin32Error());
            await MaterialControlsPreview.Layout(window);
            uint dpi = GetDpiForWindow(handle); dpis.Add(dpi);
            Console.WriteLine($"[DISPLAY] {index} work={monitor.Work.Left},{monitor.Work.Top},{monitor.Work.Width},{monitor.Work.Height} nativeDpi={dpi} WPF={VisualTreeHelper.GetDpi(window).PixelsPerInchX}");
            Check(Math.Abs(VisualTreeHelper.GetDpi(window).PixelsPerInchX - dpi) < 1, "moving to display " + index + " updates WPF DPI");
            if (typography)
            {
                await TypographyChecks.Run(window);
                window.Width = 1180; window.Height = 740;
                var sample = TypographyAudit.ShowControls();
                await MaterialControlsPreview.Layout(window);
                TypographyAudit.Capture(window, Path.Combine(folder, "controls-" + dpi + "dpi.png"));
                sample.Dismiss();
            }
            foreach (string skin in new[] { "win11", "macos" })
            {
                MaterialControlsPreview.Skin(window, skin); await MaterialControlsPreview.Layout(window);
                var max = (Button)window.FindName("MaxBtn"); var edge = max.PointToScreen(new Point(2, 2));
                Check(SendMessage(handle, 0x0084, IntPtr.Zero, Pack(edge)).ToInt32() == 9, "Snap hit target tracks the new DPI · " + skin);
                var before = window.RestoreBounds;
                window.WindowState = WindowState.Maximized; await MaterialControlsPreview.Layout(window);
                GetWindowRect(handle, out var maximized);
                Check(Math.Abs(maximized.Left - monitor.Work.Left) <= 1 && Math.Abs(maximized.Top - monitor.Work.Top) <= 1 && Math.Abs(maximized.Width - monitor.Work.Width) <= 1 && Math.Abs(maximized.Height - monitor.Work.Height) <= 1, "maximize fits this monitor's work area · " + skin);
                Program.SaveSnapshot(window, Path.Combine(folder, "display-" + index + "-" + skin + "-max.png"));
                window.WindowState = WindowState.Normal; await MaterialControlsPreview.Layout(window);
                Check(Math.Abs(window.ActualWidth - before.Width) < 1 && Math.Abs(window.ActualHeight - before.Height) < 1, "restore retains logical size after DPI change · " + skin);
                int half = monitor.Work.Width / 2;
                SetWindowPos(handle, IntPtr.Zero, monitor.Work.Left, monitor.Work.Top, half, monitor.Work.Height, 0x14); await MaterialControlsPreview.Layout(window);
                GetWindowRect(handle, out var halfBounds);
                Check(halfBounds.Width <= half + 1, "window minimum allows a real half-screen placement · " + skin);
                Check(window.ActualWidth >= window.MinWidth && max.IsVisible, "half-work-area layout preserves minimum width and window controls · " + skin);
                Program.SaveSnapshot(window, Path.Combine(folder, "display-" + index + "-" + skin + "-half.png"));
            }
            index++;
        }
        if (monitors.Count < 2 || dpis.Count < 2) Console.WriteLine("[DISPLAY LIMIT] Two distinct physical DPIs are not available.");
        else Console.WriteLine("[DISPLAY PASS] Verified native monitor moves across " + string.Join(",", dpis) + " DPI; half-work-area sizing is not a synthetic claim of dragging into Snap.");
        if (live)
        {
            var first = monitors[0]; window.WindowState = WindowState.Normal;
            SetWindowPos(handle, IntPtr.Zero, first.Work.Left + 200, first.Work.Top + 120, 1300, 900, 0x14); window.Activate();
        }
    }
    private static IntPtr Pack(Point point) => new(unchecked((int)(((uint)(ushort)(int)point.Y << 16) | (ushort)(int)point.X)));
    private static void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); Console.WriteLine("[PASS] " + message); }
}
