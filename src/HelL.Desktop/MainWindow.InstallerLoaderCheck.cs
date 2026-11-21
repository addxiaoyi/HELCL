using Avalonia.Controls;
using Avalonia.Interactivity;
using HelL.Desktop.Models;
using HelL.Desktop.Services;
using HelL.Desktop.Views;
using HelL.Portable;

namespace HelL.Desktop;

public sealed partial class MainWindow
{
    private async Task CheckInstallerLoaderRetriesAsync()
    {
        ShowPage("install");
        var versions = InstallerView.FindControl<ListBox>("VersionList")!;
        var loaders = InstallerView.FindControl<ComboBox>("LoaderCombo")!;
        var builds = InstallerView.FindControl<ComboBox>("LoaderBuildCombo")!;
        var install = InstallerView.FindControl<Button>("StartInstallButton")!;
        var originalVersion = versions.SelectedItem;
        int originalLoader = loaders.SelectedIndex;
        var originalConfirmation = DesktopDialogs.Confirmation;
        var taskList = TasksView.FindControl<ListBox>("TaskList")!;
        var retryButton = TasksView.FindControl<Button>("RetryButton")!;
        var minecraft = versions.Items.OfType<VersionInstallerView.RemoteVersionRow>().Take(2).ToArray();
        if (minecraft.Length != 2) throw new IOException("Installer loader smoke needs two Minecraft versions.");
        var requests = new List<(string Version, string Loader)>();
        var first = new TaskCompletionSource<IReadOnlyList<LoaderVersion>>(TaskCreationOptions.RunContinuationsAsynchronously);
        const string loader = "fabric";
        DesktopDialogs.Confirmation = (_, _) => Task.FromResult(true);
        try
        {
            InstallerView.LoaderBuildsFixture = (version, kind, _) =>
            {
                requests.Add((version, kind));
                return first.Task;
            };
            versions.SelectedItem = minecraft[0];
            loaders.SelectedItem = loaders.Items.OfType<ComboBoxItem>().Single(option => option.Tag as string == loader);
            if (!IsWorking || !requests.SequenceEqual(new[] { (minecraft[0].Id, loader) }))
                throw new IOException("The installer lookup did not start for the original selection.");
            versions.SelectedItem = minecraft[1];
            first.SetResult([new LoaderVersion("old-build", "old-build", true)]);
            await WaitForInstallerTask();
            if (builds.ItemCount != 0 || install.IsEnabled)
                throw new IOException("A stale installer lookup enabled installation for the wrong Minecraft version.");

            InstallerView.LoaderBuildsFixture = (version, kind, _) =>
            {
                requests.Add((version, kind));
                return Task.FromException<IReadOnlyList<LoaderVersion>>(new IOException("Controlled installer lookup failure."));
            };
            await InstallerView.LoadLoaderBuildsAsync();
            var failed = TasksView.History.First();
            if (failed.State != DesktopTaskState.Failed || requests[^1] != (minecraft[1].Id, loader))
                throw new IOException("The installer refresh did not query the current selection and report its failure.");

            loaders.SelectedIndex = 0;
            ShowPage("tasks");
            taskList.SelectedItem = failed;
            int beforeRetry = requests.Count;
            retryButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForInstallerTask();
            if (requests.Count != beforeRetry || ReferenceEquals(TasksView.History.First(), failed)
                || TasksView.History.First().State != DesktopTaskState.Failed)
                throw new IOException("The old installer lookup could be retried after changing the loader.");
        }
        finally
        {
            loaders.SelectedIndex = originalLoader;
            versions.SelectedItem = originalVersion;
            InstallerView.LoaderBuildsFixture = null;
            DesktopDialogs.Confirmation = originalConfirmation;
            ShowPage("launch");
        }

        async Task WaitForInstallerTask()
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(3);
            while (IsWorking && DateTime.UtcNow < deadline) await Task.Delay(20);
            if (IsWorking) throw new TimeoutException("The controlled installer lookup did not finish.");
        }
    }
}
