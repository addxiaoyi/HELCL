using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using HelL.Core;
using HelL.Core.Launcher;

namespace HelL.Mobile;

public sealed partial class MobileHome
{
    private bool appearanceReady;

    private void InitializeAppearance()
    {
        string theme = Py.S(Config.CONFIG.Get("mobile_theme", "dark"));
        if (theme is not ("dark" or "light" or "system")) theme = "dark";
        ThemePicker.SelectedIndex = theme switch { "light" => 1, "system" => 2, _ => 0 };
        ApplyAppearance(theme);
        appearanceReady = true;
    }

    private void ChangeAppearance(object? sender, SelectionChangedEventArgs e)
    {
        if (!appearanceReady || ThemePicker.SelectedItem is not ComboBoxItem { Tag: string theme }) return;
        ApplyAppearance(theme);
        try
        {
            Config.CONFIG.Set("mobile_theme", theme);
            Config.CONFIG.Save();
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceError("MobileHome.ChangeAppearance: " + error);
            Status.Text = "外观未能保存，请检查存储空间。";
        }
    }

    private static void ApplyAppearance(string theme)
    {
        if (Application.Current == null) return;
        Application.Current.RequestedThemeVariant = theme switch
        {
            "light" => ThemeVariant.Light,
            "system" => ThemeVariant.Default,
            _ => ThemeVariant.Dark
        };
    }
}
