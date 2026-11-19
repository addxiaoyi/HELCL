using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HelL;
using HelL.Services;

internal static class NativeShellChecks
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    internal static async Task Run(MainWindow window)
    {
        var failures = new List<string>();
        int passed = 0;
        IntPtr handle = new WindowInteropHelper(window).Handle;
        double minWidth = window.MinWidth, minHeight = window.MinHeight;
        IntPtr limits = Marshal.AllocHGlobal(40);
        try
        {
            window.MinWidth = 940;
            window.MinHeight = 600;
            Marshal.Copy(new int[10], 0, limits, 10);
            SendMessage(handle, 0x0024, IntPtr.Zero, limits);
            DpiScale dpi = VisualTreeHelper.GetDpi(window);
            Check(Marshal.ReadInt32(limits, 24) == Math.Ceiling(window.MinWidth * dpi.DpiScaleX) &&
                  Marshal.ReadInt32(limits, 28) == Math.Ceiling(window.MinHeight * dpi.DpiScaleY),
                "Native resize limits follow WPF dimensions at the current display DPI");
        }
        finally
        {
            window.MinWidth = minWidth;
            window.MinHeight = minHeight;
            Marshal.FreeHGlobal(limits);
        }
        Wallpaper.SetSource("");
        foreach (bool dark in new[] { false, true })
        {
            App.ApplyTheme(dark);
            if (DwmGetWindowAttribute(handle, 20, out int systemDark, sizeof(int)) == 0)
                Check(systemDark == (dark ? 1 : 0), (dark ? "Dark" : "Light") + " palette change updates the DWM theme without a separate refresh");
            Backdrop.Refresh(window);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            string theme = dark ? "Dark" : "Light";
            int status = DwmGetWindowAttribute(handle, 38, out int backdrop, sizeof(int));
            Console.WriteLine($"[NATIVE] {theme}: backdrop HRESULT=0x{status:X8}, value={backdrop}");
            if (status == 0 && backdrop == 2)
            {
                Check(AlphaAt(window, 300, 12) == 0, theme + " title bar exposes the system backdrop");
                Check(AlphaAt(window, 100, 600) < 24, theme + " sidebar exposes the system backdrop");
                byte surfaceAlpha = AlphaAt(window, 1000, 600);
                Check(surfaceAlpha == 0, theme + " full transparency leaves the native material completely exposed");
            }
            else Console.WriteLine("[SKIP] Native material transparency requires an available Mica backdrop");
            if (DwmGetWindowAttribute(handle, 33, out int corner, sizeof(int)) == 0)
                Check(corner == 2, theme + " window requests the supported DWM rounded corners");
            SideFrost.SetEnabled(false);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check(AlphaAt(window, 100, 600) == 0, theme + " disabling blur retains the selected full transparency");
            SideFrost.SetEnabled(true);
            bool systemEffects = Backdrop.EffectsEnabled;
            var applyEffects = typeof(Backdrop).GetMethod("Apply", BindingFlags.Static | BindingFlags.NonPublic,
                null, new[] { typeof(Window), typeof(bool) }, null)!;
            applyEffects.Invoke(null, new object[] { window, false });
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check(!Backdrop.NativeAvailable && AlphaAt(window, 300, 12) == 255 && AlphaAt(window, 100, 600) == 255,
                theme + " disabled system effects use solid chrome and sidebar");
            Check(new[] { "B.WindowBase", "B.PageWash", "B.Popup", "B.ControlFill", "B.Glass", "B.Card" }
                .All(key => Ui.Res(key) is SolidColorBrush brush && brush.Opacity == 1 && brush.Color.A == 255),
                theme + " disabled system effects make reading and popup surfaces opaque");
            Check(SideFrost.Enabled, theme + " temporary system fallback preserves the user's frost preference");
            string? screenshots = Environment.GetEnvironmentVariable("HELL_UI_CHECK_SCREENSHOTS");
            if (!string.IsNullOrEmpty(screenshots)) Program.SaveSnapshot(window, Path.Combine(screenshots, "solid-system-" + theme.ToLowerInvariant() + ".png"));
            SendMessage(handle, 0x001A, IntPtr.Zero, IntPtr.Zero);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check(Backdrop.EffectsEnabled == systemEffects,
                theme + " a system settings notification restores the actual effects preference");
            Console.WriteLine($"[SYSTEM_EFFECTS] {theme}: {systemEffects}; disabled state above was simulated inside the app");
        }
        var caption = (Panel)window.FindName("CaptionHost");
        var maximize = (Button)window.FindName("MaxBtn");
        Point center = maximize.PointToScreen(new Point(maximize.ActualWidth / 2, maximize.ActualHeight / 2));
        var position = new IntPtr(unchecked((int)(((uint)(ushort)(int)center.Y << 16) | (ushort)(int)center.X)));
        Check(SendMessage(handle, 0x0084, IntPtr.Zero, position).ToInt32() == 9,
            "Windows recognizes the custom maximize button for Snap layouts");
        if (!PostMessage(handle, 0x00A1, new IntPtr(9), position) || !PostMessage(handle, 0x00A2, new IntPtr(9), position))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check(window.WindowState == WindowState.Maximized, "Non-client maximize click still maximizes the window");
        Point restoredCenter = maximize.PointToScreen(new Point(maximize.ActualWidth / 2, maximize.ActualHeight / 2));
        var restorePosition = new IntPtr(unchecked((int)(((uint)(ushort)(int)restoredCenter.Y << 16) | (ushort)(int)restoredCenter.X)));
        if (!PostMessage(handle, 0x00A1, new IntPtr(9), restorePosition) || !PostMessage(handle, 0x00A2, new IntPtr(9), restorePosition))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check(window.WindowState == WindowState.Normal && window.ActualWidth == 1180 && window.ActualHeight == 740,
            "Non-client restore click returns to the previous window dimensions");
        foreach (WindowState expected in new[] { WindowState.Maximized, WindowState.Normal })
        {
            Point buttonCenter = maximize.PointToScreen(new Point(maximize.ActualWidth / 2, maximize.ActualHeight / 2));
            Point clientCenter = ClientPoint(buttonCenter);
            if (!PostMessage(handle, 0x00A1, new IntPtr(9), Pack(buttonCenter)) ||
                !PostMessage(handle, 0x0202, IntPtr.Zero, Pack(clientCenter)))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check(window.WindowState == expected, "Captured client mouse release changes window state to " + expected);
        }
        window.WindowState = WindowState.Normal;
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        IntPtr outside = new IntPtr(unchecked((int)(((uint)(ushort)(int)center.Y << 16) | (ushort)((int)center.X - 80))));
        if (!PostMessage(handle, 0x00A1, new IntPtr(9), position) || !PostMessage(handle, 0x00A2, new IntPtr(1), outside))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check(window.WindowState == WindowState.Normal, "Releasing outside the maximize button cancels its click");
        if (!PostMessage(handle, 0x00A1, new IntPtr(9), position) || !PostMessage(handle, 0x001F, IntPtr.Zero, IntPtr.Zero) ||
            !PostMessage(handle, 0x00A2, new IntPtr(9), position))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check(window.WindowState == WindowState.Normal, "Cancelling a caption press does not maximize on release");
        string order = string.Join(",", caption.Children.OfType<Button>().Select(button => button.Name));
        Check(order == "ThemeBtn,MinBtn,MaxBtn,CloseBtn", "Window actions stay in a contiguous minimize/maximize/close group");
        Check(((Button)window.FindName("MaxBtn")).ActualHeight == 48, "Window actions use the full title bar hit area");
        window.WindowState = WindowState.Maximized;
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check(System.Windows.Automation.AutomationProperties.GetName((Button)window.FindName("MaxBtn")) == "还原",
            "Maximized window exposes the Restore action");
        window.WindowState = WindowState.Normal;
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check(window.ActualWidth == 1180 && window.ActualHeight == 740, "Restoring the window preserves its previous size");
        string wallpaper = Path.Combine(Environment.GetEnvironmentVariable("TIPCL_HOME")!, "native-wallpaper.png");
        SaveWallpaper(wallpaper);
        Wallpaper.SetSource(wallpaper);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check(AlphaAt(window, 100, 600) == 255 && AlphaAt(window, 300, 12) == 255,
            "Custom wallpaper keeps title bar and sidebar reading surfaces composited");
        Wallpaper.SetSource("");
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        if (Backdrop.NativeAvailable) Check(AlphaAt(window, 300, 12) == 0,
            "Removing a wallpaper exposes the system material again");
        if (failures.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
        Console.WriteLine($"[NATIVE PASS] {passed} checks");

        void Check(bool condition, string message)
        {
            Console.WriteLine((condition ? "[PASS] " : "[FAIL] ") + message);
            if (condition) passed++;
            else failures.Add(message);
        }

        Point ClientPoint(Point screen)
        {
            Point logical = window.PointFromScreen(screen);
            return HwndSource.FromHwnd(handle).CompositionTarget.TransformToDevice.Transform(logical);
        }
    }

    private static IntPtr Pack(Point point) => new IntPtr(unchecked((int)(((uint)(ushort)(int)point.Y << 16) | (ushort)(int)point.X)));

    private static byte AlphaAt(Window window, int x, int y)
    {
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return pixel[3];
    }

    private static void SaveWallpaper(string path)
    {
        var visual = new DrawingVisual();
        using (DrawingContext drawing = visual.RenderOpen())
            drawing.DrawRectangle(Brushes.Magenta, null, new Rect(0, 0, 8, 8));
        var bitmap = new RenderTargetBitmap(8, 8, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }
}
