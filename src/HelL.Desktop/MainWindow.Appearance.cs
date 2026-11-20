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
        if (!persist) return;
        try
        {
            Config.CONFIG.Set("portable_theme", theme);
            Config.CONFIG.Save();
        }
        catch (Exception error)
        {
            SetStatus("外观偏好保存失败：" + error.Message);
        }
    }

    internal void SelectGlassOpacity(string glass, bool persist = true)
    {
        if (glass is not ("ultra" or "standard" or "deep" or "solid")) glass = "standard";
        CurrentGlass = glass;
        ApplyGlassBackdrop();
        SettingsView?.SyncGlassSelection(glass);
        if (!persist) return;
        try
        {
            Config.CONFIG.Set("portable_glass", glass);
            Config.CONFIG.Save();
        }
        catch (Exception error)
        {
            SetStatus("通透度保存失败：" + error.Message);
        }
    }

    private void ApplyGlassBackdrop()
    {
        if (WindowShell == null) return;
        bool isLight = Application.Current?.ActualThemeVariant == ThemeVariant.Light || CurrentTheme == "light";
        string hex = (isLight, CurrentGlass) switch
        {
            (false, "ultra") => "#6B0C0E13",
            (false, "standard") => "#940D0F14",
            (false, "deep") => "#BD0E1015",
            (false, "solid") => "#F6111318",
            (true, "ultra") => "#80EEF1F6",
            (true, "standard") => "#ADEEF1F6",
            (true, "deep") => "#D6EFF2F7",
            _ => "#F8F2F4F8"
        };
        bool hasBlur = ActualTransparencyLevel == WindowTransparencyLevel.AcrylicBlur
            || ActualTransparencyLevel == WindowTransparencyLevel.Blur || ActualTransparencyLevel == WindowTransparencyLevel.Mica;
        Color tint = Color.Parse(hex);
        if (!hasBlur || CurrentGlass == "solid") tint = Color.FromRgb(tint.R, tint.G, tint.B);
        WindowShell.Background = new SolidColorBrush(tint);
    }
}
