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
        try
        {
            Config.CONFIG.Commit(PyDict.Of("mobile_theme", theme));
            ApplyAppearance(theme);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            System.Diagnostics.Trace.TraceError("MobileHome.ChangeAppearance: " + error.GetType().Name);
            appearanceReady = false;
            try
            {
                ThemePicker.SelectedIndex = Py.S(Config.CONFIG.Get("mobile_theme", "dark")) switch { "light" => 1, "system" => 2, _ => 0 };
            }
            finally { appearanceReady = true; }
            Status.Text = "外观未能保存，已保留原设置。请检查存储空间后重试。";
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
