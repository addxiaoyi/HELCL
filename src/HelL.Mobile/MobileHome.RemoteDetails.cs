using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using HelL.Contracts;
using HelL.Remote;

namespace HelL.Mobile;

public sealed partial class MobileHome
{
    private readonly StackPanel remoteGame = new() { Spacing = 8 };
    private readonly StackPanel remoteTaskDetail = new() { Spacing = 8 };
    private readonly CheckBox remoteAllowControlGame = new() { Content = "允许从此手机请求游戏正常退出" };
    private async void RestoreRemoteConnection() => await RemoteAction(() => remote!.RestoreConnectionAsync());

    private void AddRemoteHostActions(StackPanel row, PairedDevice host)
    {
        bool selected = remote!.SelectedHostId == host.DeviceId;
        row.Children.Add(RemoteText((selected ? "当前电脑 · " : "") + remote.HostState(host.DeviceId), "caption"));
        long? last = remote.LastConnected(host.DeviceId);
        row.Children.Add(RemoteText(last.HasValue ? "最近读取成功：" + DateTimeOffset.FromUnixTimeSeconds(last.Value).ToLocalTime().ToString("g") : "尚未成功读取", "caption"));
        row.Children.Add(RemoteText(host.Permissions.Contains(HostPermission.ControlGame) ? "允许请求游戏正常退出；电脑仍会独立检查权限" : "未授权请求游戏退出", "caption"));
        Button select = RemoteButton(selected ? "已选中这台电脑" : "选择这台电脑", SelectRemoteHost);
        select.Tag = host.DeviceId;
        select.IsEnabled = !selected;
        row.Children.Add(select);
    }

    private void AddRemoteTaskDetails(StackPanel row, HostTask task)
    {
        if (task.ProgressPercent is double progress)
            row.Children.Add(new ProgressBar { Minimum = 0, Maximum = 100, Value = Math.Clamp(progress, 0, 100), Height = 6 });
        if (!string.IsNullOrWhiteSpace(task.Stage)) row.Children.Add(RemoteText(task.Stage, "caption"));
        if (!string.IsNullOrWhiteSpace(task.Speed)) row.Children.Add(RemoteText(task.Speed, "caption"));
        if (!string.IsNullOrWhiteSpace(task.Message)) row.Children.Add(RemoteText(task.Message, "caption"));
        Button details = RemoteButton("查看任务详情与脱敏日志", ReadRemoteTaskDetails);
        details.Classes.Add("remote-task-details");
        details.Tag = task.Id;
        details.IsEnabled = !remote!.Busy;
        row.Children.Add(details);
    }

    private void PresentRemoteDetails()
    {
        CheckBox? previousConfirmation = remoteGame.Children.OfType<CheckBox>().FirstOrDefault();
        string? previousHost = remoteGame.Tag as string;
        remoteGame.Children.Clear();
        remoteGame.Tag = remote?.SelectedHostId;
        remoteGame.Children.Add(RemoteText("游戏会话", "section"));
        Button refresh = RemoteButton("读取游戏状态", ReadRemoteGame);
        refresh.IsEnabled = remote?.Invitation is not null && !remote.Busy;
        remoteGame.Children.Add(refresh);
        if (remote?.Game is { } game)
        {
            remoteGame.Children.Add(RemoteText(game.State switch { "running" => "游戏运行中", "stopping" => "已请求正常退出，等待游戏响应", _ => "游戏未运行" }, "caption"));
            if (game.Message.Length != 0) remoteGame.Children.Add(RemoteText(game.Message, "caption"));
            if (game.Running)
            {
                var confirmed = new CheckBox { Content = "确认请求当前游戏正常退出；游戏可能要求电脑上的玩家保存或确认", Tag = game.SessionId };
                confirmed.IsChecked = previousHost == remote?.SelectedHostId
                    && previousConfirmation?.Tag is string previousSession && previousSession == game.SessionId
                    && previousConfirmation.IsChecked == true;
                AutomationProperties.SetName(confirmed, "确认请求当前游戏正常退出");
                remoteGame.Children.Add(confirmed);
                Button stop = RemoteButton("请求游戏正常退出", StopRemoteGame);
                stop.Classes.Add("remote-game-stop");
                stop.Tag = confirmed;
                stop.IsEnabled = remote is { CanControlGame: true, Busy: false };
                remoteGame.Children.Add(stop);
            }
        }
        else remoteGame.Children.Add(RemoteText("读取状态后可查看当前会话。退出请求需要手机和电脑分别授权。", "caption"));
        remoteTaskDetail.Children.Clear();
        if (remote?.Detail is not { } detail) return;
        remoteTaskDetail.Children.Add(RemoteText("任务详情 · " + detail.TaskId, "section"));
        if (detail.Message.Length != 0) remoteTaskDetail.Children.Add(RemoteText(detail.Message, "caption"));
        if (detail.Crash) remoteTaskDetail.Children.Add(RemoteText("此任务记录了崩溃。", "caption"));
        string[] lines = detail.RecentLogs ?? [];
        remoteTaskDetail.Children.Add(RemoteText(lines.Length == 0 ? "没有可显示的日志。" : string.Join(Environment.NewLine, lines.Take(20)), "caption"));
    }

    private async void SelectRemoteHost(object? sender, RoutedEventArgs e) => await RemoteAction(async () =>
    {
        remoteRevokeConfirmed.IsChecked = false;
        if (sender is Button { Tag: string id }) remote!.SelectHost(id);
        await remote!.RestoreConnectionAsync();
    });
    private async void ReadRemoteTaskDetails(object? sender, RoutedEventArgs e) => await RemoteAction(async () =>
    {
        if (sender is not Button { Tag: string id }) return;
        await remote!.ReadTaskDetailsAsync(id);
        PresentRemote();
        remoteTaskDetail.BringIntoView();
    });
    private async void ReadRemoteGame(object? sender, RoutedEventArgs e) => await RemoteAction(async () =>
    {
        await remote!.ReadGameAsync();
        PresentRemote();
        remoteGame.BringIntoView();
    });
    private async void StopRemoteGame(object? sender, RoutedEventArgs e) => await RemoteAction(async () =>
    {
        if (sender is not Button { Tag: CheckBox { Tag: string id } confirmed }) return;
        bool accepted = confirmed.IsChecked == true;
        confirmed.IsChecked = false;
        await remote!.StopGameAsync(id, accepted);
    });
}
