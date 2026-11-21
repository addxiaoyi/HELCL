using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using HelL.Core.Launcher;
using HelL.Desktop.Models;
using HelL.Desktop.Services;

namespace HelL.Desktop;

public sealed partial class MainWindow
{
    private async Task CheckInstallCancellationAsync()
    {
        string instance = SelectedName ?? throw new IOException("Installation cancellation needs an isolated instance.");
        var originalPlan = InstallationPlanFixture;
        var originalInstall = InstallFixture;
        var originalConfirmation = DesktopDialogs.Confirmation;
        string originalPage = currentPage;
        bool wasClosing = closing;
        var failures = new List<string>();
        int installs = 0;
        try
        {
            ShowPage("launch");
            InstallFixture = (_, _, _, _, _) => { installs++; return Task.FromResult("unexpected-install"); };
            Closing += KeepSmokeOpen;
            foreach (var (duringPlanning, closeWindow) in new[] { (true, false), (false, false), (true, true), (false, true) })
            {
                var heldPlan = new TaskCompletionSource<InstallationPlan>(TaskCreationOptions.RunContinuationsAsynchronously);
                var heldApproval = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                InstallationPlanFixture = (_, _, _, _) =>
                {
                    if (duringPlanning) entered.TrySetResult();
                    return duringPlanning ? heldPlan.Task : Task.FromResult(new InstallationPlan());
                };
                int prompts = 0;
                DesktopDialogs.Confirmation = (_, _) => { prompts++; entered.TrySetResult(); return heldApproval.Task; };
                Task pending = InstallWithConfirmationAsync(instance, "1.21.1", "none", null, () => true);
                var installing = activeTask ?? throw new IOException("The install cancellation fixture did not start.");
                try
                {
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
                    if (closeWindow) Close();
                    else CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    if (await Task.WhenAny(pending, Task.Delay(2000)) != pending)
                        failures.Add($"{(duringPlanning ? "Plan" : "Confirmation")} {(closeWindow ? "window close" : "cancellation")} did not release the task gate promptly.");
                }
                finally
                {
                    heldPlan.TrySetResult(new InstallationPlan());
                    heldApproval.TrySetResult(true);
                    await pending.WaitAsync(TimeSpan.FromSeconds(3));
                    closing = wasClosing;
                }
                if (installing.State != DesktopTaskState.Cancelled || installs != 0 || (duringPlanning && prompts != 0))
                    failures.Add("Cancelling an installation continued to confirmation or installation.");
            }
            Closing -= KeepSmokeOpen;
            DesktopDialogs.Confirmation = null;
            foreach (bool closeOwner in new[] { false, true })
            {
                using var stop = new CancellationTokenSource();
                var owner = new Window { Title = "Isolated confirmation cancellation", Width = 360, Height = 220 };
                owner.Closing += (_, _) => stop.Cancel();
                Window? modal = null;
                try
                {
                    owner.Show(this);
                    Task<bool> confirming = DesktopDialogs.Confirm(owner, "确认安装取消回归", "No files will be installed.", cancellation: stop.Token);
                    modal = owner.OwnedWindows.Single();
                    if (!modal.IsVisible) throw new IOException("The native confirmation fixture was not shown.");
                    if (closeOwner) owner.Close();
                    else stop.Cancel();
                    bool approved = false;
                    try { approved = await confirming.WaitAsync(TimeSpan.FromSeconds(3)); }
                    catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
                    if (approved || modal.IsVisible)
                        failures.Add("Cancelling the native confirmation left its modal visible or returned approval.");
                }
                finally
                {
                    stop.Cancel();
                    if (modal?.IsVisible == true) modal.Close(false);
                    owner.Close();
                }
            }
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            File.WriteAllText(Path.Combine(Program.DataRoot, "desktop-install-cancellation.json"), JsonSerializer.Serialize(new
            {
                ok = failures.Count == 0, failures, planningCancellation = true, confirmationCancellation = true,
                windowCloseDuringPlanning = true, windowCloseDuringConfirmation = true,
                nativeModalCancellation = true, nativeOwnerClose = true, installs
            }));
            if (failures.Count > 0) throw new IOException(string.Join("; ", failures));
        }
        finally
        {
            InstallationPlanFixture = originalPlan;
            InstallFixture = originalInstall;
            DesktopDialogs.Confirmation = originalConfirmation;
            Closing -= KeepSmokeOpen;
            closing = wasClosing;
            ShowPage(originalPage);
        }

        static void KeepSmokeOpen(object? sender, WindowClosingEventArgs args) => args.Cancel = true;
    }
}
