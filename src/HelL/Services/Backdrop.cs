using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace HelL.Services;

public static class Backdrop
{
    private const int DarkModeAttribute = 20;
    private const int MainWindowMaterial = 2;
    private const int AcrylicMaterial = 3;
    private const int NoMaterial = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    public static bool NativeAvailable { get; private set; }
    public static int Material { get; private set; } = NoMaterial;
    public static bool EffectsEnabled { get; private set; } = true;
    public static bool ClearDesktop { get; private set; }
    public static bool ClearAvailable { get; private set; }

    public static void SetClearDesktop(bool enabled)
    {
        if (ClearDesktop == enabled) return;
        ClearDesktop = enabled;
        Refresh(AppServices.Window);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void Apply(Window? window) => Apply(window, ReadSystemEffects());

    internal static void Apply(Window? window, bool effectsEnabled)
    {
        if (window == null) return;
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;

        EffectsEnabled = effectsEnabled;
        Material = NoMaterial;
        ClearAvailable = false;
        if (window.AllowsTransparency)
        {
            NativeAvailable = ClearAvailable = ClearDesktop && effectsEnabled;
            Glass.Refresh();
            return;
        }

        bool available = false;
        try
        {
            int dark = App.IsDark ? 1 : 0;
            int themeStatus = DwmSetWindowAttribute(handle, DarkModeAttribute, ref dark, sizeof(int));
            int preferred = Glass.IsMac ? AcrylicMaterial : MainWindowMaterial;
            available = effectsEnabled && themeStatus == 0 && WinVer.SystemBackdrop(handle, preferred);
            Material = available ? preferred : NoMaterial;
            if (!available && effectsEnabled && preferred != MainWindowMaterial)
            {
                available = WinVer.SystemBackdrop(handle, MainWindowMaterial);
                Material = available ? MainWindowMaterial : NoMaterial;
            }
            if (!available) WinVer.SystemBackdrop(handle, NoMaterial);
            if (themeStatus != 0) Trace.WriteLine($"[Backdrop] System theme is unavailable; using solid surfaces: 0x{themeStatus:X8}");
            var margins = available
                ? new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 }
                : new Margins();
            int frameStatus = DwmExtendFrameIntoClientArea(handle, ref margins);
            if (available && frameStatus != 0)
            {
                Trace.WriteLine($"[Backdrop] Extending the window frame failed: 0x{frameStatus:X8}");
                WinVer.SystemBackdrop(handle, NoMaterial);
                available = false;
                Material = NoMaterial;
            }
            WinVer.Corner(handle, round: window.WindowState != WindowState.Maximized);
        }
        catch (Exception error) when (error is DllNotFoundException || error is EntryPointNotFoundException)
        {
            Trace.WriteLine("[Backdrop] System material is unavailable: " + error.Message);
        }

        NativeAvailable = available;
        HwndSource? source = HwndSource.FromHwnd(handle);
        if (source?.CompositionTarget != null)
            source.CompositionTarget.BackgroundColor = available ? Colors.Transparent : Glass.Base(App.IsDark);
        // WPF must leave the chrome transparent; otherwise it hides DWM's material completely.
        Glass.Refresh();
    }

    public static void Refresh(Window? window) => Apply(window);

    private static bool ReadSystemEffects()
    {
        if (SystemParameters.HighContrast) return false;
        try
        {
            // Load the optional Windows API at runtime so older systems retain a solid fallback.
            Type? settingsType = Type.GetType("Windows.UI.ViewManagement.UISettings, Windows, ContentType=WindowsRuntime");
            PropertyInfo? effects = settingsType?.GetProperty("AdvancedEffectsEnabled");
            if (settingsType == null || effects == null) return false;
            return effects.GetValue(Activator.CreateInstance(settingsType)) is true;
        }
        catch (Exception error) when (error is TypeLoadException || error is TargetInvocationException ||
                                      error is COMException || error is NotSupportedException)
        {
            Trace.WriteLine("[Backdrop] Cannot read system transparency; using solid surfaces: " + error.Message);
            return false;
        }
    }

    public static void PmSetMoving(IntPtr hwnd, bool moving) => Wallpaper.SetWindowMoving(moving);
}
