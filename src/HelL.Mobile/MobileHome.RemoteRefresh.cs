using HelL.Mobile.Remote;

namespace HelL.Mobile;

public sealed partial class MobileHome
{
    private static readonly TimeSpan RemoteRefreshInterval = TimeSpan.FromSeconds(5);
    private CancellationTokenSource? remotePolling;
    private Task remotePollingTask = Task.CompletedTask;
    private bool remotePageActive;

    private void SetRemotePageActive(bool active)
    {
        remotePageActive = active && !disposed;
        if (!remotePageActive)
        {
            PauseRemotePolling();
            remote?.Cancel();
            remote?.StopUpdates();
            remoteOfferInput.Clear();
            remoteInviteInput.Clear();
            remoteVerified.IsChecked = remoteRevokeConfirmed.IsChecked = false;
            foreach (Avalonia.Controls.CheckBox confirmation in remoteGame.Children.OfType<Avalonia.Controls.CheckBox>())
                confirmation.IsChecked = false;
            return;
        }
        if (!MobileRuntime.RemotePreview) StartRemotePolling(RemoteRefreshInterval);
    }

    private void PauseRemotePolling()
    {
        remotePolling?.Cancel();
        remotePolling = null;
    }

    private void StartRemotePolling(TimeSpan interval)
    {
        if (remotePolling is not null || !remotePageActive || remote is null || remoteAutoRefresh.IsChecked != true) return;
        var cancellation = new CancellationTokenSource();
        remotePolling = cancellation;
        remotePollingTask = PollRemoteAsync(remote, interval, cancellation);
    }

    private void ChangeRemoteAutoRefresh(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (remoteAutoRefresh.IsChecked == true)
        {
            if (!MobileRuntime.RemotePreview) StartRemotePolling(RemoteRefreshInterval);
        }
        else PauseRemotePolling();
    }

    private async Task PollRemoteAsync(MobileRemoteController owner, TimeSpan interval, CancellationTokenSource cancellation)
    {
        try
        {
            int failures = 0;
            while (!cancellation.IsCancellationRequested)
            {
                int multiplier = 1 << Math.Min(failures, 3);
                await Task.Delay(TimeSpan.FromTicks(interval.Ticks * multiplier), cancellation.Token);
                if (disposed || !remotePageActive || owner.Busy || owner.Invitation is null
                    || remoteOfferInput.IsFocused || remoteInviteInput.IsFocused
                    || remoteVerified.IsChecked == true || remoteRevokeConfirmed.IsChecked == true) continue;
                await RefreshRemoteStateAsync(owner, cancellation.Token);
                failures = owner.Error.Length == 0 ? 0 : Math.Min(failures + 1, 3);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            if (!disposed && remotePageActive)
            {
                remoteError.Text = "自动刷新已暂停，请重新读取电脑状态。";
                remoteError.IsVisible = true;
            }
            System.Diagnostics.Trace.TraceWarning("MobileHome remote refresh stopped: " + error.GetType().Name);
        }
        finally
        {
            if (ReferenceEquals(remotePolling, cancellation)) remotePolling = null;
            cancellation.Dispose();
        }
    }

    private async Task RefreshRemoteStateAsync(MobileRemoteController owner, CancellationToken cancellation)
    {
        string? hostId = owner.SelectedHostId;
        if (owner.Invitation is not { } current || current.Expires <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return;
        await owner.RefreshAsync(cancellation);
        if (cancellation.IsCancellationRequested || !remotePageActive || owner.Busy || owner.Error.Length != 0
            || owner.SelectedHostId != hostId || owner.Invitation is not { } invite
            || invite.Expires <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return;
        await owner.ReadGameAsync(cancellation);
    }
}
