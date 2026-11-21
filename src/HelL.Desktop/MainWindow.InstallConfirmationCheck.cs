using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using HelL.Core.Launcher;
using HelL.Desktop.Models;
using HelL.Desktop.Services;
using HelL.Desktop.Views;
using HelL.Portable;

namespace HelL.Desktop;

public sealed partial class MainWindow
{
    private async Task CheckInstallConfirmationAsync()
    {
        const string other = "install-confirmation-target";
        const string build = "confirmation-build-0.16.5";
        string originalInstance = SelectedName ?? throw new IOException("The install confirmation check needs an isolated instance.");
        var originalVersions = RemoteVersions.ItemsSource;
        int originalVersion = RemoteVersions.SelectedIndex;
        int originalLoader = LoaderPicker.SelectedIndex;
        var originalBuilds = LoaderVersionPicker.ItemsSource;
        int originalBuild = LoaderVersionPicker.SelectedIndex;
        var originalConfirmation = DesktopDialogs.Confirmation;
        var originalPlan = InstallationPlanFixture;
        var originalInstall = InstallFixture;
        var originalLookup = InstallerView.LoaderBuildsFixture;
        var targets = InstallerView.FindControl<ComboBox>("InstancePicker")!;
        var versions = InstallerView.FindControl<ListBox>("VersionList")!;
        var loaders = InstallerView.FindControl<ComboBox>("LoaderCombo")!;
        var builds = InstallerView.FindControl<ComboBox>("LoaderBuildCombo")!;
        var start = InstallerView.FindControl<Button>("StartInstallButton")!;
        var originalInstallerVersion = versions.SelectedItem;
        int originalInstallerLoader = loaders.SelectedIndex;
        string? originalTarget = (targets.SelectedItem as InstanceEntry)?.Name;
        var minecraft = versions.Items.OfType<VersionInstallerView.RemoteVersionRow>().Take(2).ToArray();
        if (minecraft.Length != 2) throw new IOException("The install confirmation check needs two Minecraft versions.");
        if (launcher.InstancesList().Any(instance => instance.Name == other))
            throw new IOException("The isolated install confirmation target already exists.");
        launcher.CreateInstance(other);
        var plans = new List<(string Instance, string Version, string Loader)>();
        var installs = new List<(string Instance, string Version, string Loader, string? Build)>();
        var prompts = new List<string>();
        var pendingPlan = new TaskCompletionSource<InstallationPlan>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool planningHeldGate = true;
        try
        {
            InstallerView.LoaderBuildsFixture = (_, _, _) => Task.FromResult<IReadOnlyList<LoaderVersion>>(
                [new LoaderVersion(build, build, true)]);
            InstallationPlanFixture = (name, version, loader, _) =>
            {
                RecordPlan(name, version, loader);
                return Task.FromException<InstallationPlan>(new IOException("Controlled install plan failure."));
            };
            InstallFixture = (name, version, loader, selectedBuild, _) =>
            {
                installs.Add((name, version, loader, selectedBuild));
                return Task.FromResult("controlled-installed-profile");
            };
            DesktopDialogs.Confirmation = (_, body) => { prompts.Add(body); return Task.FromResult(false); };

            PrepareQuickInstall();
            InstallButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForIdleAsync();
            if (!plans.SequenceEqual(new[] { (originalInstance, "1.21.1", "fabric") }) || !planningHeldGate)
                throw new IOException("Quick install did not capture its target and hold the task gate during planning.");
            RequirePrompt(originalInstance, "1.21.1", build);
            if (installs.Count != 0 || TasksView.History.First(task => task.Title == "安装 Minecraft").State != DesktopTaskState.Cancelled)
                throw new IOException("Declining the unavailable-size confirmation installed or recorded successful completion.");

            InstallationPlanFixture = (name, version, loader, _) =>
            {
                RecordPlan(name, version, loader);
                return Task.FromResult(new InstallationPlan());
            };
            prompts.Clear();
            DesktopDialogs.Confirmation = (_, body) => { prompts.Add(body); return Task.FromResult(true); };
            InstallButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForIdleAsync();
            RequirePrompt(originalInstance, "1.21.1", build);
            if (!installs.SequenceEqual(new[] { (originalInstance, "1.21.1", "fabric", (string?)build) }))
                throw new IOException("Approved quick install changed the captured instance, Minecraft version, loader or build.");

            ShowPage("install");
            loaders.SelectedIndex = 0;
            InstallerView.RefreshInstances(other);
            versions.SelectedItem = minecraft[0];
            loaders.SelectedItem = loaders.Items.OfType<ComboBoxItem>().Single(option => option.Tag as string == "fabric");
            await WaitForIdleAsync();
            if ((targets.SelectedItem as InstanceEntry)?.Name != other || SelectedName != originalInstance)
                throw new IOException("Completing a loader lookup replaced the dedicated install target with the sidebar target.");
            await RunTask(_ => Task.CompletedTask);
            if ((targets.SelectedItem as InstanceEntry)?.Name != other)
                throw new IOException("Completing an unrelated task changed the dedicated install target.");
            if (builds.SelectedItem is not LoaderVersion { Id: build } || !start.IsEnabled)
                throw new IOException("The dedicated confirmation fixture did not prepare the selected loader build.");

            prompts.Clear();
            start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForIdleAsync();
            RequirePrompt(other, minecraft[0].Id, build);
            if (installs.Count != 2 || installs[^1] != (other, minecraft[0].Id, "fabric", build))
                throw new IOException("Approved dedicated install changed its independently selected target or build.");

            prompts.Clear();
            int beforePlans = plans.Count;
            InstallationPlanFixture = (name, version, loader, token) =>
            {
                RecordPlan(name, version, loader);
                return pendingPlan.Task.WaitAsync(TimeSpan.FromSeconds(3), token);
            };
            start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!IsWorking || plans.Count != beforePlans + 1 || !planningHeldGate)
                throw new IOException("Dedicated install planning did not acquire the global task gate.");
            targets.SelectedItem = targets.Items.OfType<InstanceEntry>().Single(instance => instance.Name == originalInstance);
            versions.SelectedItem = minecraft[1];
            pendingPlan.SetResult(new InstallationPlan());
            await WaitForIdleAsync();
            if (prompts.Count != 0 || installs.Count != 2)
                throw new IOException("A stale dedicated install plan prompted or installed after its target and Minecraft version changed.");

            InstallationPlanFixture = (name, version, loader, _) =>
            {
                RecordPlan(name, version, loader);
                return Task.FromResult(new InstallationPlan());
            };
            PrepareQuickInstall();
            DesktopDialogs.Confirmation = (_, body) =>
            {
                prompts.Add(body);
                RemoteVersions.SelectedIndex = 1;
                return Task.FromResult(true);
            };
            InstallButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForIdleAsync();
            RequirePrompt(originalInstance, "1.21.1", build);
            if (installs.Count != 2)
                throw new IOException("Approving an outdated confirmation installed after the Minecraft selection changed.");

            File.WriteAllText(Path.Combine(Program.DataRoot, "desktop-install-confirmation-smoke.json"), JsonSerializer.Serialize(new
            {
                ok = true, unavailableSizeSinglePrompt = true, declinedInstallPrevented = true,
                quickInstallExactArguments = true, dedicatedInstallExactArguments = true,
                dedicatedTargetSurvivesTaskCompletion = true, planningHoldsTaskGate = planningHeldGate,
                stalePlanRejected = true, staleApprovalRejected = true
            }));
        }
        finally
        {
            pendingPlan.TrySetResult(new InstallationPlan());
            try
            {
                await WaitForIdleAsync();
                ShowPage("launch");
                loaders.SelectedIndex = 0;
                versions.SelectedItem = originalInstallerVersion;
                RemoteVersions.ItemsSource = originalVersions;
                RemoteVersions.SelectedIndex = originalVersion;
                LoaderPicker.SelectedIndex = originalLoader;
                LoaderVersionPicker.ItemsSource = originalBuilds;
                LoaderVersionPicker.SelectedIndex = originalBuild;
                ReloadInstances(originalInstance);
                InstallerView.RefreshInstances(originalTarget);
                loaders.SelectedIndex = originalInstallerLoader;
            }
            finally
            {
                InstallationPlanFixture = originalPlan;
                InstallFixture = originalInstall;
                InstallerView.LoaderBuildsFixture = originalLookup;
                DesktopDialogs.Confirmation = originalConfirmation;
            }
        }

        void PrepareQuickInstall()
        {
            ReloadInstances(originalInstance);
            ShowPage("launch");
            RemoteVersions.ItemsSource = new[] { "1.21.1", "1.20.1" };
            RemoteVersions.SelectedIndex = 0;
            LoaderPicker.SelectedItem = LoaderPicker.Items.OfType<ComboBoxItem>().Single(option => option.Tag as string == "fabric");
            LoaderVersionPicker.ItemsSource = new[] { new LoaderVersion(build, build, true) };
            LoaderVersionPicker.SelectedIndex = 0;
            if (!InstallButton.IsEnabled) throw new IOException("The quick confirmation fixture did not enable installation.");
        }

        void RecordPlan(string name, string version, string loader)
        {
            plans.Add((name, version, loader));
            planningHeldGate &= IsWorking && !InstanceList.IsEnabled && !CreateButton.IsEnabled;
        }

        void RequirePrompt(string instance, string version, string selectedBuild)
        {
            if (prompts.Count != 1 || !new[] { instance, version, selectedBuild }.All(value => prompts[0].Contains(value, StringComparison.Ordinal)))
                throw new IOException("Install confirmation must show one prompt with the captured instance, Minecraft version and exact loader build.");
        }

        async Task WaitForIdleAsync()
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            do
            {
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                if (!IsWorking) return;
                await Task.Delay(20);
            } while (DateTime.UtcNow < deadline);
            throw new TimeoutException("The controlled install confirmation operation did not finish.");
        }
    }
}
