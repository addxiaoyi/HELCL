using Avalonia.Controls;
using HelL.Desktop.Views;

namespace HelL.Desktop;

public sealed partial class MainWindow
{
    private async Task CheckDesktopRecheckSafetyAsync()
    {
        bool retryPreserved = await CheckAiRetryDraftAsync();
        bool logBound = await CheckLateLogFailureAsync();
        File.WriteAllText(Path.Combine(Program.DataRoot, "desktop-recheck.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            ok = retryPreserved && logBound,
            selectedReleaseDocument = true, failedRetryDraft = retryPreserved, lateLogFailure = logBound,
            nativeScreenReaderVerified = false
        }));
        if (!retryPreserved || !logBound)
            throw new IOException($"Desktop recheck failed (retryDraft={retryPreserved}, staleLog={logBound}).");
    }

    private async Task<bool> CheckAiRetryDraftAsync()
    {
        ShowPage("ai");
        var prompt = AiView.FindControl<TextBox>("PromptInput")!;
        var retry = AiView.FindControl<Button>("RetryPromptButton")!;
        int sends = 0;
        try
        {
            AiView.PrefillPrompt("controlled-failed-question", log: false);
            AiView.SendFixture = (_, _) => { sends++; return Task.FromException(new IOException("controlled-send-failure")); };
            await AiView.SendCurrentPromptAsync();
            bool failureRecoverable = retry.IsVisible && prompt.Text == "controlled-failed-question";
            prompt.Text = "new-unsent-draft";
            AiView.SendFixture = (_, _) => { sends++; return Task.CompletedTask; };
            await AiView.RetryFailedPromptAsync();
            bool preserved = prompt.Text == "new-unsent-draft" && sends == 1;
            prompt.Text = "new-successful-question";
            await AiView.SendCurrentPromptAsync();
            bool retryCleared = sends == 2 && !retry.IsVisible;
            prompt.Text = "another-unsent-draft";
            await AiView.RetryFailedPromptAsync();
            return failureRecoverable && preserved && retryCleared && sends == 2 && prompt.Text == "another-unsent-draft";
        }
        finally { AiView.SendFixture = null; }
    }

    private async Task<bool> CheckLateLogFailureAsync()
    {
        ShowPage("saves");
        var picker = SavesView.FindControl<ComboBox>("LogFilePicker")!;
        var viewer = SavesView.FindControl<SelectableTextBlock>("LogViewer")!;
        var diagnose = SavesView.FindControl<Button>("DiagnoseLogButton")!;
        var first = new SavesManagerView.LogEntryItem("A", "missing-fixture-a.log");
        var second = new SavesManagerView.LogEntryItem("B", "missing-fixture-b.log");
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            picker.ItemsSource = new[] { first, second };
            picker.SelectedItem = first;
            SavesView.LogReadFixture = path => path == first.FullPath ? pending.Task : Task.FromResult("current-b-lines");
            Task stale = SavesView.ReadSelectedLogAsync(first);
            picker.SelectedItem = second;
            await SavesView.ReadSelectedLogAsync(second);
            pending.SetException(new IOException("old-private-path-must-not-appear"));
            await stale;
            bool oldSelectionRejected = viewer.Text == "current-b-lines" && diagnose.IsEnabled;

            var olderRead = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            SavesView.LogReadFixture = _ => olderRead.Task;
            Task sameFile = SavesView.ReadSelectedLogAsync(second);
            SavesView.LogReadFixture = _ => Task.FromResult("newer-same-file-lines");
            await SavesView.ReadSelectedLogAsync(second);
            olderRead.SetException(new IOException("older-same-file-error"));
            await sameFile;
            bool olderReadRejected = viewer.Text == "newer-same-file-lines" && diagnose.IsEnabled;

            var reloadedInstance = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            SavesView.LogReadFixture = _ => reloadedInstance.Task;
            Task previousReload = SavesView.ReadSelectedLogAsync(second);
            SavesView.Reload();
            string? currentText = viewer.Text;
            bool currentEnabled = diagnose.IsEnabled;
            reloadedInstance.SetException(new IOException("previous-reload-path-must-not-appear"));
            await previousReload;
            return oldSelectionRejected && olderReadRejected && currentText == viewer.Text && currentEnabled == diagnose.IsEnabled;
        }
        finally { SavesView.LogReadFixture = null; }
    }
}
