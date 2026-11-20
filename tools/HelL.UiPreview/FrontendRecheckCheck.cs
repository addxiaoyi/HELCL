using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HelL;
using HelL.Models;
using HelL.Pages;
using HelL.Services;

internal static class FrontendRecheckCheck
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object Field(object owner, string name) => owner.GetType().GetField(name, Hidden)!.GetValue(owner)!;
    private static object? Call(object owner, string name, params object[] arguments) => owner.GetType().GetMethods(Hidden | BindingFlags.DeclaredOnly)
        .Single(method => method.Name == name && method.GetParameters().Length == arguments.Length).Invoke(owner, arguments);

    internal static IReadOnlyList<FrameworkElement> Cards(InstancePage library) => ((IDictionary)Field(library, "_cards")).Values.Cast<object>()
        .Select(row => (FrameworkElement)row.GetType().GetProperty("Root")!.GetValue(row)!).ToList();

    internal static async Task Run(MainWindow window, string folder)
    {
        Directory.CreateDirectory(folder);
        Motion.Enabled = false;
        window.Width = 1180; window.Height = 740;
        await WpfIconCheck.Run(window, Path.Combine(folder, "icons"));
        await WpfDetailCheck.Run(window, Path.Combine(folder, "details"));
        var failures = new List<string>();
        void Check(bool condition, string label)
        {
            if (!condition) failures.Add(label);
            Console.WriteLine((condition ? "[PASS] " : "[FAIL] ") + label);
        }

        window.Navigate("settings", true);
        await Program.WaitForPage(window, "settings");
        var settings = (SettingsPage)window.GetPage("settings")!;
        var width = (TextBox)Field(settings, "_w");
        width.Text = width.Text == "1024" ? "1025" : "1024";
        string draftWidth = width.Text;
        await settings.RefreshAsync();
        Check(width.Text == draftWidth, "background settings refresh preserves the edited draft");
        Check(((ICollection<string>)Field(settings, "_changedSettings")).Count > 0, "background refresh retains unsaved-change state");
        window.Navigate("launch", true); window.Navigate("settings", true);
        Check(width.Text == draftWidth, "leaving and returning preserves the settings draft");
        Program.SaveSnapshot(window, Path.Combine(folder, "settings-draft-preserved.png"));

        object key = Field(settings, "_cfKey");
        key.GetType().GetProperty("Text")!.SetValue(key, "fixture-not-a-real-api-key");
        Task save = (Task)Call(settings, "SaveAsync")!;
        Check(!settings.IsEnabled, "settings input is locked while a commit is pending");
        Task duplicate = (Task)Call(settings, "SaveAsync")!;
        Check(duplicate.IsCompleted, "a repeated save does not start a second pending commit");
        string saveError = "";
        try { await save; await duplicate; }
        catch (Exception error) { saveError = error.Message; }
        Check(saveError.Length == 0, "the real settings RPC accepts the complete appearance snapshot: " + saveError);
        Check(settings.IsEnabled, "settings input is restored after the commit settles");
        Check(((ICollection<string>)Field(settings, "_changedSettings")).Count == 0, "acknowledged save clears only the submitted draft");
        Check(string.IsNullOrEmpty((string?)key.GetType().GetProperty("Text")!.GetValue(key)),
            "acknowledged secret input is cleared without creating another draft");
        string configPath = Path.Combine(Environment.GetEnvironmentVariable("TIPCL_HOME")!, "config.json");
        using (var saved = JsonDocument.Parse(File.ReadAllText(configPath)))
            Check(saved.RootElement.TryGetProperty("ui_wpf_theme", out var theme) && theme.TryGetProperty("Skin", out _),
                "appearance and general settings persist through one RPC in canonical form");
        await SettingsThemeRecheck.Run(window, Path.Combine(folder, "themes"));

        width.Text = width.Text == "1026" ? "1027" : "1026";
        string rejectedDraft = width.Text;
        string blocker = Path.Combine(folder, "blocked-game-directory");
        File.WriteAllText(blocker, "owned test fixture");
        var gameDir = (TextBox)Field(settings, "_gameDir");
        gameDir.Text = Path.GetFullPath(blocker);
        string originalConfig = File.ReadAllText(configPath);
        bool rejected = false;
        try { await (Task)Call(settings, "SaveAsync")!; }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected && settings.IsEnabled && width.Text == rejectedDraft
            && ((ICollection<string>)Field(settings, "_changedSettings")).Count > 0,
            "a failed commit restores input and retains the complete draft");
        Check(File.ReadAllText(configPath) == originalConfig, "a failed directory validation does not partially commit other settings");

        window.Navigate("instance", true);
        await Program.WaitForPage(window, "instance");
        var library = (InstancePage)window.GetPage("instance")!;
        var instances = Enumerable.Range(0, 2000).Select(index => new InstanceInfo { Name = "virtual-fixture-" + index.ToString("D4") }).ToList();
        library.GetType().GetField("_rows", Hidden)!.SetValue(library, instances);
        Call(library, "Render");
        await Settle(window);
        Check(((IDictionary)Field(library, "_cards")).Count < 100, "two thousand cover records realize only a bounded viewport");
        object surface = Field(library, "_list");
        MethodInfo? scrollTo = surface.GetType().GetMethod("ScrollToIndex", BindingFlags.Instance | BindingFlags.Public);
        if (scrollTo is not null) scrollTo.Invoke(surface, new object[] { 1999 });
        else
        {
            var more = (Button)Field(library, "_loadMore");
            for (int index = 0; index < 2; index++) more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        await Settle(window);
        var cards = (IDictionary)Field(library, "_cards");
        Check(cards.Count < 100, "scrolling through the complete library does not accumulate card controls");
        Check(cards.Contains("virtual-fixture-1999"), "the last instance remains reachable without loading prior pages");
        foreach (int index in new[] { 0, 500, 1000, 1500, 1999 })
        {
            if (scrollTo is null) break;
            scrollTo.Invoke(surface, new object[] { index });
            await Settle(window);
            Check(((IDictionary)Field(library, "_cards")).Count is > 0 and < 100, "cover viewport stays bounded at record " + index);
        }
        Program.SaveSnapshot(window, Path.Combine(folder, "virtual-covers-end.png"));
        Call(library, "SetLibraryView", false);
        await Settle(window);
        surface = Field(library, "_list");
        scrollTo = surface.GetType().GetMethod("ScrollToIndex", BindingFlags.Instance | BindingFlags.Public);
        scrollTo?.Invoke(surface, new object[] { 1999 });
        await Settle(window);
        cards = (IDictionary)Field(library, "_cards");
        Check(cards.Count < 100 && cards.Contains("virtual-fixture-1999"), "the compact list also virtualizes and reaches the final record");
        Call(library, "Select", "virtual-fixture-1999");
        Call(library, "MoveSelection", -1999);
        await Settle(window);
        Check((string?)Field(library, "_selected") == "virtual-fixture-0000", "keyboard selection crosses unrealized records");
        Check(((IDictionary)Field(library, "_cards")).Contains("virtual-fixture-0000"), "keyboard selection brings its target into the viewport");
        Call(library, "MarkVisible");
        Check(((ICollection<string>)Field(library, "_marked")).Count == instances.Count, "select all applies to every filtered record including unrealized instances");
        window.Width = 800;
        await Settle(window);
        Call(library, "SetLibraryView", true);
        await Settle(window);
        Check(((IDictionary)Field(library, "_cards")).Count is > 0 and < 100, "narrow cover layout keeps bounded realization");
        Check(Cards(library).All(card => card.ActualWidth <= ((FrameworkElement)surface).ActualWidth + 1), "narrow cover cards stay inside the viewport");
        Program.SaveSnapshot(window, Path.Combine(folder, "virtual-list-keyboard-start.png"));

        File.WriteAllText(Path.Combine(folder, "frontend-recheck.json"), JsonSerializer.Serialize(new
        {
            ok = failures.Count == 0, failures, records = instances.Count,
            realized = ((IDictionary)Field(library, "_cards")).Count,
            settingsDraft = width.Text == rejectedDraft, nativeScreenReaderVerified = false
        }));
        if (failures.Count > 0) throw new IOException(string.Join("; ", failures));
    }

    private static async Task Settle(MainWindow window)
    {
        window.UpdateLayout();
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        await Task.Delay(80);
        window.UpdateLayout();
    }
}
