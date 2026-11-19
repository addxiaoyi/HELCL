using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HelL;
using HelL.Pages;
using HelL.Services;

internal static class SettingsThemeRecheck
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string FirstTheme = "recheck-theme-green";
    private const string SecondTheme = "recheck-theme-blue";

    internal static async Task Run(MainWindow window, string folder)
    {
        Directory.CreateDirectory(folder);
        var checks = new List<string>();
        string failure = "";
        void Check(bool condition, string label)
        {
            if (!condition) throw new IOException("Settings theme check failed: " + label);
            checks.Add(label);
            Console.WriteLine("[PASS] " + label);
        }

        try
        {
            window.Navigate("settings", true);
            await Program.WaitForPage(window, "settings");
            var settings = (SettingsPage)window.GetPage("settings")!;
            Check(Dirty(settings).Count == 0, "theme checks start from an acknowledged settings snapshot");
            JsonElement original = await AppServices.Client.CallAsync("get_settings");
            string originalColor = original.GetProperty("theme_color").GetString()!;
            string originalBackground = original.GetProperty("ui_bg_preset").GetString()!;

            await SaveAppearance("#2E9B6B", "eye");
            await AppServices.Client.CallAsync("save_theme", new { name = FirstTheme });
            await SaveAppearance("#2F72C4", "mist");
            await AppServices.Client.CallAsync("save_theme", new { name = SecondTheme });
            await SaveAppearance(originalColor, originalBackground);
            await settings.RefreshAsync();

            var themes = Field<ComboBox>(settings, "_themePack");
            Check(themes.Items.Contains(FirstTheme) && themes.Items.Contains(SecondTheme), "saved theme fixtures are listed through the real bridge");
            themes.SelectedItem = FirstTheme;
            Check(Dirty(settings).Count == 0, "selecting a saved theme does not create a settings draft");

            Task firstLoad = Load(settings);
            Check(!settings.IsEnabled, "theme application locks settings until the bridge request settles");
            await firstLoad;
            JsonElement first = await AppServices.Client.CallAsync("get_settings");
            Check(Field<string>(settings, "_color") == "#2E9B6B" && SelectedKey(Field<ComboBox>(settings, "_bg")) == "eye",
                "explicit theme loading refreshes the color and background controls");
            Check(first.GetProperty("theme_color").GetString() == "#2E9B6B" && first.GetProperty("ui_bg_preset").GetString() == "eye",
                "theme controls agree with the persisted backend snapshot");
            Check(settings.IsEnabled && Dirty(settings).Count == 0, "successful theme application restores editing with no stale draft");
            Program.SaveSnapshot(window, Path.Combine(folder, "settings-theme-loaded.png"));

            var width = Field<TextBox>(settings, "_w");
            string persistedWidth = width.Text;
            width.Text = persistedWidth == "1024" ? "1025" : "1024";
            string draftWidth = width.Text;
            themes.SelectedItem = SecondTheme;
            Task canceledLoad = Load(settings);
            await Answer("保留草稿");
            await canceledLoad;
            JsonElement canceled = await AppServices.Client.CallAsync("get_settings");
            Check(width.Text == draftWidth && Dirty(settings).Count > 0, "canceling theme replacement retains the edited draft and dirty state");
            Check(canceled.GetProperty("theme_color").GetString() == "#2E9B6B" && canceled.GetProperty("ui_bg_preset").GetString() == "eye",
                "canceling theme replacement leaves the backend theme unchanged");
            Check(Field<string>(settings, "_color") == "#2E9B6B" && settings.IsEnabled,
                "canceling theme replacement keeps the current preview editable");

            Task confirmedLoad = Load(settings);
            await Answer("加载并放弃草稿");
            await confirmedLoad;
            Check(width.Text == persistedWidth && Dirty(settings).Count == 0,
                "explicitly discarding a draft reloads its persisted field values");
            Check(Field<string>(settings, "_color") == "#2F72C4" && SelectedKey(Field<ComboBox>(settings, "_bg")) == "mist",
                "confirmed theme replacement applies the selected theme after draft cleanup");

            await AppServices.Client.CallAsync("delete_theme", new { name = FirstTheme });
            await AppServices.Client.CallAsync("delete_theme", new { name = SecondTheme });
            await SaveAppearance(originalColor, originalBackground);
            await settings.RefreshAsync();
            Check(settings.IsEnabled && Dirty(settings).Count == 0, "fixture cleanup leaves settings ready for subsequent checks");
        }
        catch (Exception error)
        {
            failure = error.ToString();
            throw;
        }
        finally
        {
            File.WriteAllText(Path.Combine(folder, "settings-theme-recheck.json"), JsonSerializer.Serialize(new
            {
                ok = failure.Length == 0, checks, failure
            }));
        }
    }

    private static async Task SaveAppearance(string color, string background)
    {
        await AppServices.Client.CallAsync("save_settings", new
        {
            data = new Dictionary<string, object> { ["theme_color"] = color, ["ui_bg_preset"] = background }
        });
    }

    private static T Field<T>(SettingsPage settings, string name) => (T)settings.GetType().GetField(name, Hidden)!.GetValue(settings)!;
    private static ICollection<string> Dirty(SettingsPage settings) => Field<ICollection<string>>(settings, "_changedSettings");
    private static Task Load(SettingsPage settings) => (Task)settings.GetType().GetMethod("LoadThemeAsync", Hidden)!.Invoke(settings, null)!;
    private static string SelectedKey(ComboBox combo) => (string)combo.SelectedItem.GetType().GetProperty("Key")!.GetValue(combo.SelectedItem)!;

    private static async Task Answer(string caption)
    {
        for (int attempt = 0; attempt < 150 && !Dlg.AnyOpen; attempt++) await Task.Delay(20);
        if (!Dlg.AnyOpen || Dlg.Host is null) throw new IOException("The theme draft confirmation did not open.");
        var button = CustomizationPreview.Walk(Dlg.Host).OfType<Button>().Single(control => control.Content?.ToString() == caption);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
}
