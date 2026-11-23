using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using HelL.Core;
using HelL.Core.Launcher;

namespace HelL.Desktop;

public sealed partial class MainWindow
{
    internal string CurrentTheme { get; private set; } = "dark";
    internal string CurrentGlass { get; private set; } = "standard";

    private void InitializeAppearance()
    {
        string theme = Py.Str(Config.CONFIG.Get("portable_theme", "dark"));
        if (theme is not ("dark" or "light" or "system")) theme = "dark";
        string glass = Py.Str(Config.CONFIG.Get("portable_glass", "standard"));
        if (glass is not ("ultra" or "standard" or "deep" or "solid")) glass = "standard";
        CurrentGlass = glass;
        SelectTheme(theme, persist: false);
    }

    internal void SelectTheme(string theme, bool persist = true)
    {
        if (theme is not ("dark" or "light" or "system")) theme = "dark";
        if (persist && !CommitAppearance("portable_theme", theme)) return;
        CurrentTheme = theme;
        if (Application.Current != null)
        {
            Application.Current.RequestedThemeVariant = theme switch
            {
                "light" => ThemeVariant.Light,
                "system" => ThemeVariant.Default,
                _ => ThemeVariant.Dark
            };
        }
        ApplyGlassBackdrop();
        SettingsView?.SyncThemeSelection(theme);
        SettingsView?.SyncGlassSelection(CurrentGlass);
    }

    internal void SelectGlassOpacity(string glass, bool persist = true)
    {
        if (glass is not ("ultra" or "standard" or "deep" or "solid")) glass = "standard";
        if (persist && !CommitAppearance("portable_glass", glass)) return;
        CurrentGlass = glass;
        ApplyGlassBackdrop();
        SettingsView?.SyncGlassSelection(glass);
    }

    private bool CommitAppearance(string key, string value)
    {
        try
        {
            Config.CONFIG.Commit(PyDict.Of(key, value));
            SettingsView?.SetAppearanceNotice("外观已保存，下次启动会继续使用。", false);
            return true;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            System.Diagnostics.Trace.TraceWarning("MainWindow.CommitAppearance: " + error.GetType().Name);
            const string message = "外观保存失败，已保留原设置。请检查数据目录后重试。";
            SettingsView?.SetAppearanceNotice(message, true);
            SetStatus(message);
            return false;
        }
    }

    private void ApplyGlassBackdrop()
    {
        if (WindowShell == null) return;
        bool isLight = Application.Current?.ActualThemeVariant == ThemeVariant.Light || CurrentTheme == "light";
        string hex = (isLight, CurrentGlass) switch
        {
            (false, "ultra") => "#3D0C0E13",
            (false, "standard") => "#660D0F14",
            (false, "deep") => "#990E1015",
            (false, "solid") => "#FF111318",
            (true, "ultra") => "#4DEEF1F6",
            (true, "standard") => "#73EEF1F6",
            (true, "deep") => "#B3EFF2F7",
            _ => "#FFF2F4F8"
        };
        Color tint = Color.Parse(hex);
        if (ActualTransparencyLevel == WindowTransparencyLevel.None || CurrentGlass == "solid")
            tint = Color.FromRgb(tint.R, tint.G, tint.B);
        WindowShell.Background = new SolidColorBrush(tint);
    }
}
