using Avalonia.Controls;
using Avalonia.Interactivity;
using HelL.Desktop.Models;
using HelL.Desktop.Services;

namespace HelL.Desktop;

public sealed partial class MainWindow
{
    private async Task CheckLibraryRefreshRetryAsync()
    {
        const string corruptMetadata = "controlled invalid library metadata";
        string instance = SelectedName ?? throw new InvalidOperationException("The library refresh check needs an isolated instance.");
        string metadata = Path.Combine(launcher.InstanceFolder(instance), ".instance.json");
        byte[] original = File.ReadAllBytes(metadata);
        var originalConfirmation = DesktopDialogs.Confirmation;
        try
        {
            ShowPage("library");
            var refresh = LibraryView.FindControl<Button>("LibraryRefreshButton")!;
            var error = LibraryView.FindControl<TextBlock>("LibraryError")!;
            var previous = TasksView.History.FirstOrDefault();
            File.WriteAllText(metadata, corruptMetadata);
            refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForRefreshAsync();
            var failed = TasksView.History.First();
            if (ReferenceEquals(previous, failed) || failed.State != DesktopTaskState.Failed || !error.IsVisible
                || !failed.Message.Contains(instance, StringComparison.Ordinal) || File.ReadAllText(metadata) != corruptMetadata)
                throw new IOException("Refreshing with damaged metadata did not report failure and preserve the damaged file.");

            ShowPage("tasks");
            var taskList = TasksView.FindControl<ListBox>("TaskList")!;
            var retry = TasksView.FindControl<Button>("RetryButton")!;
            taskList.SelectedItem = failed;
            if (!retry.IsVisible) throw new IOException("The failed library refresh is not retryable.");
            File.WriteAllBytes(metadata, original);
            DesktopDialogs.Confirmation = (_, _) => Task.FromResult(true);
            retry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForRefreshAsync();
            var recovered = TasksView.History.First();
            if (ReferenceEquals(failed, recovered) || recovered.Title != failed.Title || recovered.State != DesktopTaskState.Completed
                || recovered.Message != "实例库已刷新。" || error.IsVisible || LibraryView.Selected?.Name != instance)
                throw new IOException("Retrying after restoring metadata did not recover the library or report success.");
        }
        finally
        {
            DesktopDialogs.Confirmation = originalConfirmation;
            File.WriteAllBytes(metadata, original);
            ShowPage("library");
        }

        async Task WaitForRefreshAsync()
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(3);
            while (IsWorking && DateTime.UtcNow < deadline) await Task.Delay(20);
            if (IsWorking) throw new TimeoutException("The controlled library refresh did not finish.");
        }
    }
}
