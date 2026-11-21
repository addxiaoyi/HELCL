using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
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
        var originalBuilds = InstallerView.LoaderBuildsFixture;
        var originalConfirmation = DesktopDialogs.Confirmation;
        var taskList = TasksView.FindControl<ListBox>("TaskList")!;
        var retryButton = TasksView.FindControl<Button>("RetryButton")!;
        var minecraft = versions.Items.OfType<VersionInstallerView.RemoteVersionRow>().Take(2).ToArray();
        if (minecraft.Length != 2) throw new IOException("Installer loader smoke needs two Minecraft versions.");
        var requests = new List<(string Version, string Loader)>();
        var first = new TaskCompletionSource<IReadOnlyList<LoaderVersion>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var latest = new TaskCompletionSource<IReadOnlyList<LoaderVersion>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var hidden = new TaskCompletionSource<IReadOnlyList<LoaderVersion>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reentered = new TaskCompletionSource<IReadOnlyList<LoaderVersion>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var unrelated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        const string fabric = "fabric";
        const string forge = "forge";
        var fabricOption = loaders.Items.OfType<ComboBoxItem>().Single(option => option.Tag as string == fabric);
        var forgeOption = loaders.Items.OfType<ComboBoxItem>().Single(option => option.Tag as string == forge);
        bool selectedOldBuild = false;
        builds.SelectionChanged += TrackBuild;
        DesktopDialogs.Confirmation = (_, _) => Task.FromResult(true);
        try
        {
            InstallerView.LoaderBuildsFixture = (version, kind, _) =>
            {
                requests.Add((version, kind));
                return requests.Count switch
                {
                    1 => first.Task,
                    2 => latest.Task,
                    3 => hidden.Task,
                    4 => reentered.Task,
                    _ => Task.FromException<IReadOnlyList<LoaderVersion>>(new IOException("Controlled installer lookup failure."))
                };
            };
            versions.SelectedItem = minecraft[0];
            loaders.SelectedItem = fabricOption;
            await WaitForAsync(() => requests.Count > 0, "The original installer lookup did not start.");
            if (!IsWorking || !requests.SequenceEqual(new[] { (minecraft[0].Id, fabric) }))
                throw new IOException("The installer lookup did not start for the original selection.");
            versions.SelectedItem = minecraft[1];
            loaders.SelectedItem = forgeOption;
            versions.SelectedItem = minecraft[0];
            loaders.SelectedItem = fabricOption;
            first.SetResult([new LoaderVersion("old-build", "old-build", true)]);
            await WaitForAsync(() => requests.Count >= 2, "The installer did not look up the latest selection automatically.");
            if (!requests.SequenceEqual(new[] { (minecraft[0].Id, fabric), (minecraft[0].Id, fabric) }))
                throw new IOException("The installer queried an intermediate selection.");
            if (selectedOldBuild || builds.ItemCount != 0 || install.IsEnabled)
                throw new IOException("A stale installer lookup enabled installation after the selection changed.");
            latest.SetResult([new LoaderVersion("current-build", "current-build", true)]);
            await WaitForAsync(() => !IsWorking, "The latest installer lookup did not finish.");
            if (builds.SelectedItem is not LoaderVersion { Id: "current-build" } || !install.IsEnabled)
                throw new IOException("The latest installer lookup did not enable installation.");

            versions.SelectedItem = minecraft[1];
            await WaitForAsync(() => requests.Count >= 3, "The installer lookup did not start before leaving its page.");
            ShowPage("tasks");
            versions.SelectedItem = minecraft[0];
            loaders.SelectedItem = forgeOption;
            hidden.SetResult([new LoaderVersion("old-build", "old-build", true)]);
            await WaitForAsync(() => !IsWorking, "The hidden installer lookup did not finish.");
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            if (requests.Count != 3 || requests[^1] != (minecraft[1].Id, fabric)
                || selectedOldBuild || builds.ItemCount != 0 || install.IsEnabled)
                throw new IOException("A hidden installer page started pending work or accepted an obsolete build.");

            ShowPage("install");
            ShowPage("install");
            InstallerView.ResumeLoaderLookup();
            await WaitForAsync(() => requests.Count >= 4, "Reentering the installer did not resume its pending lookup.");
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            if (requests.Count != 4 || requests[^1] != (minecraft[0].Id, forge))
                throw new IOException("Reentering the installer did not query the latest selection exactly once.");
            reentered.SetResult([new LoaderVersion("reentered-build", "reentered-build", true)]);
            await WaitForAsync(() => !IsWorking, "The reentered installer lookup did not finish.");
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            if (requests.Count != 4 || builds.SelectedItem is not LoaderVersion { Id: "reentered-build" } || !install.IsEnabled)
                throw new IOException("The reentered installer lookup did not settle with exactly one current build.");

            var busy = RunTask(_ => unrelated.Task);
            ShowPage("tasks");
            versions.SelectedItem = minecraft[1];
            loaders.SelectedItem = forgeOption;
            ShowPage("install");
            ShowPage("install");
            if (requests.Count != 4 || builds.ItemCount != 0 || install.IsEnabled)
                throw new IOException("An installer selection reused builds or started while another task was running.");
            unrelated.SetResult();
            await busy.WaitAsync(TimeSpan.FromSeconds(3));
            await WaitForAsync(() => requests.Count >= 5 && !IsWorking, "The unrelated task did not release the queued installer lookup.");
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            var failed = TasksView.History.First();
            if (failed.State != DesktopTaskState.Failed || requests.Count != 5 || requests[^1] != (minecraft[1].Id, forge))
                throw new IOException("The queued installer lookup did not query the latest selection exactly once and report its failure.");

            loaders.SelectedIndex = 0;
            ShowPage("tasks");
            taskList.SelectedItem = failed;
            int beforeRetry = requests.Count;
            retryButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForAsync(() => !IsWorking, "The old installer retry did not finish.");
            if (requests.Count != beforeRetry || ReferenceEquals(TasksView.History.First(), failed)
                || TasksView.History.First().State != DesktopTaskState.Failed)
                throw new IOException("The old installer lookup could be retried after changing the loader.");
        }
        finally
        {
            builds.SelectionChanged -= TrackBuild;
            InstallerView.LoaderBuildsFixture = (_, _, _) => Task.FromResult<IReadOnlyList<LoaderVersion>>([]);
            first.TrySetResult([]);
            latest.TrySetResult([]);
            hidden.TrySetResult([]);
            reentered.TrySetResult([]);
            unrelated.TrySetResult();
            loaders.SelectedIndex = originalLoader;
            versions.SelectedItem = originalVersion;
            try
            {
                await WaitForAsync(() => !IsWorking, "The controlled installer requests did not settle during cleanup.");
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            }
            finally
            {
                ShowPage("launch");
                InstallerView.LoaderBuildsFixture = originalBuilds;
                DesktopDialogs.Confirmation = originalConfirmation;
            }
        }

        void TrackBuild(object? sender, SelectionChangedEventArgs args)
        {
            if (builds.SelectedItem is LoaderVersion { Id: "old-build" }) selectedOldBuild = true;
        }

        static async Task WaitForAsync(Func<bool> condition, string timeout)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(3);
            while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(20);
            if (!condition()) throw new TimeoutException(timeout);
        }
    }
}
