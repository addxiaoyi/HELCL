using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using HelL.Core;
using HelL.Core.Launcher;

namespace HelL.Desktop;

public sealed partial class MainWindow
{
    private async Task CheckSettingsDraftAsync()
    {
        object sourceBefore = Config.CONFIG.Get("download_source", "auto");
        object threadsBefore = Config.CONFIG.Get("download_threads", 8L);
        string pageBefore = currentPage;
        var source = SettingsView.FindControl<ComboBox>("MirrorCombo")!;
        var threads = SettingsView.FindControl<TextBox>("ThreadsBox")!;
        var save = SettingsView.GetLogicalDescendants().OfType<Button>().Single(button => button.Content?.ToString() == "保存");
        var failures = new List<string>();
        var originalCommit = SettingsView.CommitNetworkFixture;
        try
        {
            Config.CONFIG.Commit(new PyDict { ["download_source"] = "auto", ["download_threads"] = 8L });
            ShowPage("settings");
            foreach (bool failTask in new[] { false, true })
            {
                var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Task background = RunTask(_ => gate.Task, "SettingsDraftFixture");
                source.SelectedIndex = 2;
                threads.Text = "7";
                if (failTask) gate.SetException(new IOException("Controlled background failure."));
                else gate.SetResult();
                await background;
                Check(source.SelectedIndex == 2 && threads.Text == "7", "Finishing background work replaced the unsaved network settings.");
                ShowPage("tasks");
                ShowPage("settings");
                Check(source.SelectedIndex == 2 && threads.Text == "7", "Returning to settings lost the network draft.");
            }

            source.SelectedIndex = 1;
            threads.Text = "0";
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await RunTask(_ => Task.CompletedTask, "SettingsDraftFixture");
            Check(source.SelectedIndex == 1 && threads.Text == "0", "Refreshing settings discarded an invalid input before it could be corrected.");
            Check(Py.S(Config.CONFIG.Get("download_source")) == "auto" && Py.Int(Config.CONFIG.Get("download_threads")) == 8,
                "Invalid network input changed the saved configuration.");

            threads.Text = "12";
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(Py.S(Config.CONFIG.Get("download_source")) == "bmclapi" && Py.Int(Config.CONFIG.Get("download_threads")) == 12,
                "Saving the corrected network draft did not persist both fields.");
            SettingsView.Reload();
            Check(source.SelectedIndex == 1 && threads.Text == "12", "Reloading after save did not display the saved network settings.");

            source.SelectedIndex = 2;
            threads.Text = "17";
            SettingsView.CommitNetworkFixture = _ => throw new IOException("Controlled save failure.");
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await RunTask(_ => Task.CompletedTask, "SettingsDraftFixture");
            Check(source.SelectedIndex == 2 && threads.Text == "17"
                && SettingsView.FindControl<TextBlock>("NetworkNotice")!.Text!.Contains("保存失败", StringComparison.Ordinal),
                "A failed save lost its draft or its retry explanation after task completion.");
            Check(Py.S(Config.CONFIG.Get("download_source")) == "bmclapi" && Py.Int(Config.CONFIG.Get("download_threads")) == 12,
                "A failed save changed the in-memory configuration.");
            SettingsView.CommitNetworkFixture = originalCommit;
            var discard = SettingsView.FindControl<Button>("DiscardNetworkButton")!;
            Check(discard.IsVisible, "An unsaved draft has no explicit discard action.");
            discard.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(source.SelectedIndex == 1 && threads.Text == "12" && !discard.IsVisible,
                "Discarding the draft did not restore the last saved network settings.");

            File.WriteAllText(Path.Combine(Program.DataRoot, "desktop-settings-draft.json"), JsonSerializer.Serialize(new
            { ok = failures.Count == 0, failures, backgroundSuccessAndFailure = true, navigation = true, invalidInput = true,
                explicitSave = true, saveFailurePreservesDraft = true, explicitDiscard = true }));
            if (failures.Count > 0) throw new IOException(string.Join("; ", failures));
        }
        finally
        {
            SettingsView.CommitNetworkFixture = originalCommit;
            Config.CONFIG.Commit(new PyDict { ["download_source"] = sourceBefore, ["download_threads"] = threadsBefore });
            source.SelectedItem = source.Items.OfType<ComboBoxItem>().Single(item => item.Tag as string == Py.S(sourceBefore));
            threads.Text = Py.Int(threadsBefore).ToString();
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ShowPage(pageBefore);
        }

        void Check(bool passed, string failure) { if (!passed) failures.Add(failure); }
    }
}
