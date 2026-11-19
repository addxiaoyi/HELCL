using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using HelL.Mobile.Remote;
using HelL.Remote;

namespace HelL.Mobile;

public sealed partial class MobileHome
{
    private MobileRemoteController? remote;
    private string remoteInitializationFailure = "";
    private readonly TextBlock remoteNotice = RemoteText("", "caption");
    private readonly TextBlock remoteError = RemoteText("", "caption");
    private readonly TextBlock remoteUpdateNotice = RemoteText("", "caption");
    private readonly TextBlock remoteHostPin = RemoteText("尚未导入电脑公开配对信息。", "caption");
    private readonly StackPanel remoteHosts = new() { Spacing = 10 };
    private readonly StackPanel remoteTasks = new() { Spacing = 10 };
    private readonly StackPanel remoteSettings = new() { Spacing = 12 };
    private readonly Border remoteActions = new() { Classes = { "glass" } };
    private readonly StackPanel remoteUpdateControls = new() { Spacing = 8 };
    private readonly Expander remotePairing = new() { Header = "配对电脑与权限", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Expander remoteInvitation = new() { Header = "私密连接邀请", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox remoteOfferInput = RemoteInput("电脑公开配对信息", 4096, false);
    private readonly TextBox remoteInviteInput = RemoteInput("本机专属连接邀请", 8192, true);
    private readonly CheckBox remoteVerified = new() { Content = "已在两台设备上逐字核对双方指纹" };
    private readonly CheckBox remoteAllowCancel = new() { Content = "允许从此手机请求取消电脑任务" };
    private readonly CheckBox remoteRevokeConfirmed = new() { Content = "确认撤销所选电脑的本机信任" };
    private readonly CheckBox remoteAutoRefresh = new() { Content = "自动刷新任务与游戏状态", IsChecked = true };
    private readonly Button remoteRefresh = new() { Content = "读取电脑任务", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Button remoteCancel = new() { Content = "取消本次连接", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ProgressBar remoteProgress = new() { IsIndeterminate = true, Height = 3, IsVisible = false };

    private void InitializeRemote()
    {
        if (remote is not null) { PresentRemote(); RestoreRemoteConnection(); return; }
        try
        {
            remote = session.Remote;
            remote.Changed += RemoteChanged;
            RemoteLayout.Children.Clear();
            RemoteLayout.Children.Add(RemoteText("远程电脑", "title"));
            RemoteLayout.Children.Add(RemoteText("安全配对后查看电脑任务；电脑和手机都需要在线。", "caption"));
            RemoteLayout.Children.Add(remoteNotice);
            RemoteLayout.Children.Add(remoteError);
            RemoteLayout.Children.Add(remoteProgress);
            var actions = new StackPanel { Spacing = 8 };
            remoteRefresh.Classes.Add("primary");
            remoteRefresh.Click += RefreshRemote;
            remoteCancel.Classes.Add("secondary");
            remoteCancel.Click += CancelRemote;
            actions.Children.Add(remoteRefresh);
            actions.Children.Add(remoteCancel);
            remoteAutoRefresh.IsCheckedChanged += ChangeRemoteAutoRefresh;
            actions.Children.Add(remoteAutoRefresh);
            actions.Children.Add(remoteUpdateNotice);
            remoteUpdateControls.Children.Add(RemoteButton("恢复安全邀请更新 / 重试", ResumeRemoteUpdates));
            remoteUpdateControls.Children.Add(RemoteButton("暂停邀请自动更新", PauseRemoteUpdates));
            actions.Children.Add(remoteUpdateControls);
            actions.Children.Add(RemoteText("仅显示任务快照。取消请求需要两端明确授权，任务会继续运行直至电脑确认取消。", "caption"));
            actions.Children.Add(remoteTasks);
            actions.Children.Add(remoteGame);
            actions.Children.Add(remoteTaskDetail);
            remoteActions.Child = actions;
            RemoteLayout.Children.Add(remoteActions);
            RemoteLayout.Children.Add(remoteSettings);
            remoteSettings.Children.Add(RemoteText("已信任的电脑", "section"));
            remoteSettings.Children.Add(remoteHosts);
            remoteSettings.Children.Add(remoteRevokeConfirmed);
            var publicInfo = new StackPanel { Spacing = 10 };
            publicInfo.Children.Add(RemoteText("1 · 向电脑提供手机公开信息", "section"));
            publicInfo.Children.Add(RemoteText("此处只有设备名称、公钥指纹和设备 ID；电脑也必须核对后才能信任手机。", "caption"));
            publicInfo.Children.Add(RemoteText("手机 SHA-256 指纹", "field-label"));
            publicInfo.Children.Add(Pin(remote.OwnOffer.Fingerprint));
            publicInfo.Children.Add(new SelectableTextBlock { Text = remote.OfferJson, TextWrapping = TextWrapping.Wrap, FontSize = 12 });
            publicInfo.Children.Add(RemoteButton("复制手机公开配对信息", CopyRemoteOffer));
            remoteSettings.Children.Add(new Expander { Header = "手机公开配对信息", Content = publicInfo, HorizontalAlignment = HorizontalAlignment.Stretch });
            var pairing = new StackPanel { Spacing = 10 };
            pairing.Children.Add(RemoteText("2 · 导入电脑公开信息并核对", "section"));
            pairing.Children.Add(RemoteText("电脑公开配对信息 JSON", "field-label"));
            pairing.Children.Add(remoteOfferInput);
            pairing.Children.Add(RemoteButton("显示电脑指纹", StageRemoteOffer));
            pairing.Children.Add(remoteHostPin);
            pairing.Children.Add(remoteVerified);
            pairing.Children.Add(remoteAllowCancel);
            pairing.Children.Add(remoteAllowControlGame);
            pairing.Children.Add(RemoteText("默认只读。手机上的权限开关不会替代电脑端的授权；修改权限需重新核对指纹。", "caption"));
            pairing.Children.Add(RemoteButton("确认配对 / 保存权限", ConfirmRemotePairing));
            remotePairing.Content = pairing;
            remotePairing.IsExpanded = remote.Hosts.Length == 0;
            remoteSettings.Children.Add(remotePairing);
            var invitation = new StackPanel { Spacing = 10 };
            invitation.Children.Add(RemoteText("3 · 导入或更新连接邀请", "section"));
            invitation.Children.Add(RemoteText("邀请必须来自刚才核对的电脑，并只发给本机。它包含私密连接凭据，勿粘贴管理员令牌或 host 票据。", "caption"));
            invitation.Children.Add(RemoteText("本机专属连接邀请 JSON（输入隐藏）", "field-label"));
            invitation.Children.Add(remoteInviteInput);
            invitation.Children.Add(RemoteButton("导入 / 更新邀请", ImportRemoteInvitation));
            remoteInvitation.Content = invitation;
            remoteSettings.Children.Add(remoteInvitation);
            foreach (Control control in new Control[] { remoteVerified, remoteAllowCancel, remoteRevokeConfirmed, remoteRefresh, remoteCancel })
                AutomationProperties.SetName(control, control is CheckBox check ? check.Content?.ToString() : ((Button)control).Content?.ToString());
            PresentRemote();
            RestoreRemoteConnection();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            remoteInitializationFailure = error.GetType().Name;
            if (remote is not null) remote.Changed -= RemoteChanged;
            remote = null;
            RemoteLayout.Children.Clear();
            RemoteLayout.Children.Add(RemoteText("远程配对暂不可用。请检查系统密钥库是否已解锁，再重新打开此页。", "caption"));
        }
    }

    private void RemoteChanged() => Dispatcher.UIThread.Post(() => { if (!disposed && remotePageActive) PresentRemote(); });
    private void PresentRemote()
    {
        if (remote is null || disposed) return;
        remoteNotice.Text = remote.Notice + (remote.TasksTruncated ? "\n任务较多，仅显示前 40 项，运行中的任务优先。" : "");
        remoteUpdateNotice.Text = remote.UpdateNotice;
        remoteError.Text = remote.Error;
        remoteError.IsVisible = remote.Error.Length != 0;
        remoteProgress.IsVisible = remoteCancel.IsVisible = remote.Busy;
        remoteActions.IsVisible = remote.Invitation is not null || remote.Busy || remote.Tasks.Length > 0;
        remoteUpdateControls.IsVisible = remote.Invitation?.Feed is not null;
        remoteRevokeConfirmed.IsVisible = remote.Hosts.Length > 0;
        remoteRefresh.IsEnabled = !remote.Busy && remote.Invitation is not null;
        remotePairing.IsEnabled = remoteInvitation.IsEnabled = !remote.Busy;
        remoteHostPin.Text = remote.PendingHost is { } pending
            ? $"电脑：{pending.Name}\n电脑 SHA-256：\n{FormatPin(pending.Fingerprint)}\n\n手机 SHA-256：\n{FormatPin(remote.OwnOffer.Fingerprint)}"
            : "先导入电脑公开信息。仅粘贴邀请不会自动建立信任。";
        remoteHosts.Children.Clear();
        if (remote.Hosts.Length == 0) remoteHosts.Children.Add(RemoteText("还没有已信任的电脑。展开下方配对步骤开始。", "caption"));
        foreach (PairedDevice host in remote.Hosts)
        {
            var row = new StackPanel { Spacing = 5 };
            row.Children.Add(RemoteText(host.Name, "section"));
            AddRemoteHostActions(row, host);
            row.Children.Add(RemoteText(host.Permissions.Contains(HelL.Contracts.HostPermission.CancelTasks) ? "允许读取及请求取消任务" : "只读 · 无法从手机取消任务", "caption"));
            row.Children.Add(Pin(host.Fingerprint));
            Button edit = RemoteButton("重新核对 / 修改权限", EditRemoteHost); edit.Tag = host;
            Button revoke = RemoteButton("撤销此电脑信任", RevokeRemoteHost); revoke.Tag = host.DeviceId;
            row.Children.Add(edit); row.Children.Add(revoke);
            remoteHosts.Children.Add(RemoteGroup(row));
        }
        remoteTasks.Children.Clear();
        if (remote.Tasks.Length == 0) remoteTasks.Children.Add(RemoteText(remote.Invitation is null ? "尚无任务快照，请先完成配对并导入邀请。" : "暂无可显示任务。读取快照后这里会显示电脑状态。", "caption"));
        foreach (HelL.Contracts.HostTask task in remote.Tasks)
        {
            var row = new StackPanel { Spacing = 6 };
            row.Children.Add(RemoteText(string.IsNullOrWhiteSpace(task.Title) ? task.Id : task.Title, "section"));
            row.Children.Add(RemoteText(task.State switch { "running" => "运行中", "completed" => "已完成", "failed" => "失败 / 已取消", _ => "状态未知，请刷新" }, "caption"));
            AddRemoteTaskDetails(row, task);
            if (task.State == "running")
            {
                Button cancel = RemoteButton("请求取消此任务", CancelRemoteTask);
                cancel.Tag = task.Id; cancel.IsEnabled = remote.CanCancel && !remote.Busy;
                cancel.Classes.Add("remote-task-cancel");
                row.Children.Add(cancel);
            }
            remoteTasks.Children.Add(RemoteGroup(row));
        }
        PresentRemoteDetails();
    }

    private async Task RemoteAction(Func<Task> action)
    {
        try { await action(); }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            remoteError.Text = error is InvalidDataException or UnauthorizedAccessException or InvalidOperationException
                ? error.Message : "操作未完成。请检查系统密钥库和公开信息格式后重试。";
            remoteError.IsVisible = true;
        }
    }
    private async void RefreshRemote(object? sender, RoutedEventArgs e)
    {
        if (!MobileRuntime.RemotePreview) StartRemotePolling(RemoteRefreshInterval);
        await RemoteAction(() => RefreshRemoteStateAsync(remote!, CancellationToken.None));
    }
    private void CancelRemote(object? sender, RoutedEventArgs e)
    {
        remoteAutoRefresh.IsChecked = false;
        PauseRemotePolling();
        remote?.Cancel();
    }
    private async void ResumeRemoteUpdates(object? sender, RoutedEventArgs e) => await RemoteAction(() => { remote!.StartUpdates(); return Task.CompletedTask; });
    private void PauseRemoteUpdates(object? sender, RoutedEventArgs e) => remote?.StopUpdates();
    private async void StageRemoteOffer(object? sender, RoutedEventArgs e) => await RemoteAction(() =>
    { remoteVerified.IsChecked = false; remote!.StageOffer(remoteOfferInput.Text ?? ""); return Task.CompletedTask; });
    private async void ConfirmRemotePairing(object? sender, RoutedEventArgs e) => await RemoteAction(() =>
    {
        remote!.ConfirmPairing(remoteVerified.IsChecked == true, remoteAllowCancel.IsChecked == true, remoteAllowControlGame.IsChecked == true);
        remoteVerified.IsChecked = false; remoteOfferInput.Clear();
        remotePairing.IsExpanded = false; remoteInvitation.IsExpanded = true;
        return Task.CompletedTask;
    });
    private async void ImportRemoteInvitation(object? sender, RoutedEventArgs e) => await RemoteAction(() =>
    { remote!.ImportInvitation(remoteInviteInput.Text ?? ""); remoteInviteInput.Clear(); return Task.CompletedTask; });
    private async void RevokeRemoteHost(object? sender, RoutedEventArgs e) => await RemoteAction(() =>
    {
        if (remoteRevokeConfirmed.IsChecked != true) throw new InvalidOperationException("请先勾选撤销确认，再选择要撤销的电脑。");
        if (sender is Button { Tag: string id }) remote!.Revoke(id);
        remoteRevokeConfirmed.IsChecked = false;
        return Task.CompletedTask;
    });
    private async void EditRemoteHost(object? sender, RoutedEventArgs e) => await RemoteAction(() =>
    {
        if (sender is Button { Tag: PairedDevice host })
        {
            remoteVerified.IsChecked = false;
            remoteAllowCancel.IsChecked = host.Permissions.Contains(HelL.Contracts.HostPermission.CancelTasks);
            remoteAllowControlGame.IsChecked = host.Permissions.Contains(HelL.Contracts.HostPermission.ControlGame);
            remote!.StageOffer(System.Text.Json.JsonSerializer.Serialize(new RemotePairingOffer(host.DeviceId, host.Name, host.Fingerprint)));
            remotePairing.IsExpanded = true;
            remotePairing.BringIntoView();
        }
        return Task.CompletedTask;
    });
    private async void CancelRemoteTask(object? sender, RoutedEventArgs e) => await RemoteAction(() =>
        sender is Button { Tag: string id } ? remote!.CancelTaskAsync(id) : Task.CompletedTask);
    private async void CopyRemoteOffer(object? sender, RoutedEventArgs e) => await RemoteAction(async () =>
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard ?? throw new InvalidOperationException("系统剪贴板暂不可用。");
        await clipboard.SetTextAsync(remote!.OfferJson);
    });
    private void DetachRemote()
    {
        SetRemotePageActive(false);
        if (remote is null) return;
        remote.Cancel(); remote.StopUpdates(); remote.Changed -= RemoteChanged;
    }
    private static TextBlock RemoteText(string text, string style) => new() { Text = text, Classes = { style }, TextWrapping = TextWrapping.Wrap };
    private static Border RemoteGroup(Control content) => new() { Classes = { "glass" }, Child = content };
    private static string FormatPin(string pin) => string.Join(" ", pin.Chunk(8).Select(chars => new string(chars)));
    private static SelectableTextBlock Pin(string pin) => new() { Text = FormatPin(pin), TextWrapping = TextWrapping.Wrap, FontSize = 13 };
    private static TextBox RemoteInput(string label, int limit, bool secret)
    {
        var field = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 80, MaxHeight = 140,
            MaxLength = limit, FontSize = 16, HorizontalAlignment = HorizontalAlignment.Stretch };
        if (secret) field.PasswordChar = '●';
        AutomationProperties.SetName(field, label);
        return field;
    }
    private static Button RemoteButton(string label, EventHandler<RoutedEventArgs> clicked)
    {
        var button = new Button { Content = label, Classes = { "secondary" }, HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 44 };
        AutomationProperties.SetName(button, label); button.Click += clicked; return button;
    }
}
