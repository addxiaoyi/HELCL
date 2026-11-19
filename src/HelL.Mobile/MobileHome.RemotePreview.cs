using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using HelL.Mobile.Remote;
using HelL.Remote;

namespace HelL.Mobile;

public sealed partial class MobileHome
{
    public async Task<bool> RunRemotePreviewAsync()
    {
        try
        {
            Directory.CreateDirectory(EvidenceRoot);
            await Task.Delay(500);
            MobileRuntime.BeginRemotePreview?.Invoke();
            ShowPage("remote");
            if (remote is null) throw new IOException("Remote UI did not initialize: " + remoteInitializationFailure);
            await Capture("remote-empty");
            var host = new RemotePairingStore(Path.Combine(root, "preview-host"), "家里的电脑 · 长名称配对验证", new MobileRemoteSecrets(session.Secrets));
            remoteOfferInput.Text = JsonSerializer.Serialize(host.Offer);
            StageRemoteOffer(this, new RoutedEventArgs());
            await Task.Delay(100);
            if (remote.PendingHost?.DeviceId != host.Offer.DeviceId || remote.Hosts.Length != 0) throw new IOException("The host offer was trusted before confirmation.");
            ConfirmRemotePairing(this, new RoutedEventArgs());
            if (remote.Hosts.Length != 0 || !remoteError.IsVisible) throw new IOException("Unconfirmed pairing did not show an error.");
            await Task.Delay(100);
            Point pin = remoteHostPin.TranslatePoint(default, RemoteLayout) ?? default;
            RemotePage.Offset = new Vector(0, Math.Max(0, pin.Y - 20));
            await Capture("remote-fingerprints");
            remoteVerified.IsChecked = true;
            ConfirmRemotePairing(this, new RoutedEventArgs());
            if (remote.Hosts.Length != 1) throw new IOException("Confirmed pairing was not retained.");
            var invite = new RemoteHostInvite(host.Offer, remote.OwnOffer.DeviceId, "https://relay.example/",
                new string('a', 48), "isolated-preview-client-ticket", DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds());
            remoteInviteInput.Text = JsonSerializer.Serialize(invite);
            ImportRemoteInvitation(this, new RoutedEventArgs());
            if (remote.Invitation is null || remoteInviteInput.Text?.Length > 0 || remote.CanCancel) throw new IOException("Private invitation or read-only state is incorrect.");
            RemotePage.Offset = Vector.Zero;
            await Capture("remote-paired");
            Task pending = remote.RefreshAsync();
            await Capture("remote-loading");
            remote.Cancel();
            await pending;
            if (remote.Tasks.Length != 0) throw new IOException("Cancelled refresh published late state.");
            await Capture("remote-cancelled");
            await remote.RefreshAsync();
            await Capture("remote-readonly-tasks");
            if (remote.Tasks.Length != 2 || remoteTasks.GetVisualDescendants().OfType<Button>().Any(button => button.IsEnabled && button.Tag is string id && remote.Tasks.Any(task => task.Id == id && task.State == "running") && button.Content?.ToString() != "查看任务详情与脱敏日志"))
                throw new IOException("Read-only task actions were enabled.");
            await remote.ReadTaskDetailsAsync("preview-install");
            if (remote.Detail?.RecentLogs.Length != 2) throw new IOException("Read-only task details were not available.");
            await Task.Delay(100);
            ScrollRemotePreview(remoteTaskDetail);
            await Capture("remote-task-details");
            await remote.ReadGameAsync();
            if (remote.Game?.Running != true || remote.CanControlGame) throw new IOException("Read-only game state or control permission is incorrect.");
            await Task.Delay(100);
            ScrollRemotePreview(remoteGame);
            await Capture("remote-game-readonly");
            remote.StageOffer(JsonSerializer.Serialize(host.Offer));
            remote.ConfirmPairing(true, true, true);
            await remote.StopGameAsync(remote.Game!.SessionId, true);
            if (remote.Game.State != "stopping") throw new IOException("Graceful stop was not reflected in game state.");
            await Capture("remote-game-stopping");
            await remote.CancelTaskAsync("preview-install");
            if (remote.Tasks.Single(task => task.Id == "preview-install").State != "failed") throw new IOException("Task cancellation did not reconcile its snapshot.");
            await Capture("remote-cancel-task");
            await remote.RefreshAsync();
            await Capture("remote-error");
            if (remote.Error.Length == 0 || remoteError.Text?.Contains("isolated-preview-private-token") == true)
                throw new IOException("Transport failure did not produce a safe retry state.");
            var anotherHost = new RemotePairingStore(Path.Combine(root, "preview-host-two"), "第二台电脑", new MobileRemoteSecrets(session.Secrets));
            remote.StageOffer(JsonSerializer.Serialize(anotherHost.Offer));
            remote.ConfirmPairing(true, false);
            remote.SelectHost(anotherHost.Offer.DeviceId);
            if (remote.Invitation is not null || remote.Game is not null || remote.Detail is not null) throw new IOException("Host selection retained another host's private state.");
            await Task.Delay(100);
            ScrollRemotePreview(remoteHosts);
            await Capture("remote-host-selection");
            remote.SelectHost(host.Offer.DeviceId);
            if (remote.Invitation?.Host.DeviceId != host.Offer.DeviceId) throw new IOException("The saved host invitation was not restored.");
            foreach (Control control in RemoteLayout.GetVisualDescendants().OfType<Control>().Where(control => control.IsEffectivelyVisible && control.Bounds.Width > 0))
            {
                Point point = control.TranslatePoint(default, RemotePage) ?? default;
                if (point.X < -2 || point.X + control.Bounds.Width > RemotePage.Bounds.Width + 2)
                    throw new IOException("A remote pairing control overflows the viewport: " + control.GetType().Name);
            }
            remote.Revoke(host.Offer.DeviceId);
            if (remote.Invitation is not null || remote.Tasks.Length != 0) throw new IOException("Revocation left a usable invitation.");
            await PreviewDiagnosticBundleAsync();
            File.WriteAllText(Path.Combine(EvidenceRoot, "mobile-remote-preview.json"), JsonSerializer.Serialize(new
            {
                ok = true, width = Bounds.Width, height = Bounds.Height, desktopHosted = true,
                fingerprintConfirmation = true, readOnly = true, invitationInputCleared = true, revoked = true,
                horizontalOverflow = false, loading = true, cancellation = true, snapshot = true, safeError = true,
                taskDetails = true, gameStatus = true, gracefulStop = true, hostSelection = true,
                mockedRemoteTransport = true, nativeAndroidVerified = false
            }));
            return true;
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(EvidenceRoot, "mobile-remote-preview-error.txt"), error.GetType().Name + ": " + error.Message);
            return false;
        }
        finally { DetachRemote(); }
    }

    private void ScrollRemotePreview(Control target)
    {
        Point position = target.TranslatePoint(default, RemoteLayout) ?? default;
        RemotePage.Offset = new Vector(0, Math.Max(0, position.Y - 16));
    }

    private async Task PreviewDiagnosticBundleAsync()
    {
        OpenDiagnostics(this, new RoutedEventArgs());
        for (int attempt = 0; attempt < 100 && (diagnosticBusy || diagnosticReport is null); attempt++) await Task.Delay(50);
        if (diagnosticReport is null || diagnosticBusy) throw new IOException("The diagnostic preview did not finish.");
        await Capture("diagnostic-fields");
        if (diagnosticFields.Children.OfType<CheckBox>().FirstOrDefault() is { } field) field.IsChecked = false;
        if (diagnosticReport.Fields.Count != fullDiagnosticReport!.Fields.Count - 1)
            throw new IOException("The diagnostic field selection was not applied.");
        await diagnosticReport.SavePackageAsync(Path.Combine(EvidenceRoot, "diagnostic-preview.zip"));
        if (diagnosticPreview.Text != diagnosticReport.ToJson()) throw new IOException("The diagnostic export does not match its preview.");
        Point preview = diagnosticPreview.TranslatePoint(default, diagnosticLayout) ?? default;
        diagnosticPage!.Offset = new Vector(0, Math.Max(0, preview.Y - 16));
        await Capture("diagnostic-preview");
        File.WriteAllText(Path.Combine(EvidenceRoot, "diagnostic-preview.json"), JsonSerializer.Serialize(new
        {
            ok = true, previewMatchesExport = true, fieldsRemoved = 1, uploaded = false,
            desktopHosted = true, nativeAndroidVerified = false
        }));
    }
}
