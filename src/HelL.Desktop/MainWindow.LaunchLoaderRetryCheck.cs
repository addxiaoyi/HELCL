using Avalonia.Controls;
using Avalonia.Interactivity;
using HelL.Desktop.Models;
using HelL.Desktop.Services;
using HelL.Portable;

namespace HelL.Desktop;

public sealed partial class MainWindow
{
    private async Task CheckLaunchLoaderRetriesAsync()
    {
        ShowPage("launch");
        var originalVersions = RemoteVersions.ItemsSource;
        int originalVersionIndex = RemoteVersions.SelectedIndex;
        int originalLoaderIndex = LoaderPicker.SelectedIndex;
        var taskList = TasksView.FindControl<ListBox>("TaskList")!;
        var retryButton = TasksView.FindControl<Button>("RetryButton")!;
        var requests = new List<(string Version, string Loader)>();
        var originalConfirmation = DesktopDialogs.Confirmation;
        DesktopDialogs.Confirmation = (_, _) => Task.FromResult(true);
        try
        {
            RemoteVersions.ItemsSource = new[] { "1.21.1", "1.20.1" };
            RemoteVersions.SelectedIndex = 0;
            LoaderPicker.SelectedIndex = 1;
            string loader = SelectedLoader;
            LaunchLoaderVersionsFixture = (version, kind, _) =>
            {
                requests.Add((version, kind));
                return Task.FromException<IReadOnlyList<LoaderVersion>>(new IOException("Controlled loader fetch failure."));
            };
            RefreshLoaderButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForLoaderTask();
            var firstFailure = TasksView.History.First();
            await RetryLoaderTask(firstFailure);
            var secondFailure = TasksView.History.First();
            if (secondFailure.Title != firstFailure.Title || secondFailure.State != DesktopTaskState.Failed
                || !requests.SequenceEqual(new[] { ("1.21.1", loader), ("1.21.1", loader) }))
                throw new IOException("Retrying the loader lookup lost the original task or queried a different selection.");

            RemoteVersions.SelectedIndex = 1;
            await RetryLoaderTask(secondFailure);
            if (requests.Count != 2 || TasksView.History.First().State != DesktopTaskState.Failed || LoaderVersionPicker.ItemCount != 0)
                throw new IOException("A stale loader retry queried the newly selected Minecraft version.");

            ShowPage("launch");
            var pending = new TaskCompletionSource<IReadOnlyList<LoaderVersion>>(TaskCreationOptions.RunContinuationsAsynchronously);
            LaunchLoaderVersionsFixture = (version, kind, _) => { requests.Add((version, kind)); return pending.Task; };
            RefreshLoaderButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (requests.Count != 3 || requests[^1] != ("1.20.1", loader))
                throw new IOException("The controlled loader query did not start with the selected version.");
            RemoteVersions.SelectedIndex = 0;
            pending.SetResult([new LoaderVersion("stale", "stale", true)]);
            await WaitForLoaderTask();
            if (LoaderVersionPicker.ItemCount != 0 || InstallButton.IsEnabled)
                throw new IOException("A late loader reply enabled installation using an incompatible build.");

            LaunchLoaderVersionsFixture = (_, _, _) => Task.FromResult<IReadOnlyList<LoaderVersion>>(
                [new LoaderVersion("current-build", "current-build", true)]);
            ShowPage("launch");
            RefreshLoaderButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForLoaderTask();
            if (TasksView.History.First().State != DesktopTaskState.Completed || !InstallButton.IsEnabled
                || TasksView.History.First().Message != LoaderNotice.Text
                || !LoaderNotice.Text!.Contains("找到 1 个", StringComparison.Ordinal))
                throw new IOException("A recovered loader query did not report success or enable the current build.");
        }
        finally
        {
            LaunchLoaderVersionsFixture = null;
            DesktopDialogs.Confirmation = originalConfirmation;
            RemoteVersions.ItemsSource = originalVersions;
            RemoteVersions.SelectedIndex = originalVersionIndex;
            LoaderPicker.SelectedIndex = originalLoaderIndex;
            ShowPage("launch");
        }

        async Task RetryLoaderTask(DesktopTask failed)
        {
            if (failed.State != DesktopTaskState.Failed) throw new IOException("The loader fixture did not fail as expected.");
            ShowPage("tasks");
            taskList.SelectedItem = failed;
            if (!retryButton.IsVisible) throw new IOException("The failed loader task cannot be retried again.");
            retryButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForLoaderTask();
            if (ReferenceEquals(TasksView.History.First(), failed)) throw new IOException("The loader retry did not create a task.");
        }

        async Task WaitForLoaderTask()
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(3);
            while (IsWorking && DateTime.UtcNow < deadline) await Task.Delay(20);
            if (IsWorking) throw new TimeoutException("The controlled loader query did not finish.");
        }
    }
}
