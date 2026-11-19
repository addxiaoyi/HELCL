using Avalonia.Controls;
using HelL.Contracts;
using HelL.Remote;

namespace HelL.Mobile;

public sealed partial class MobileHome
{
    private sealed record RemoteHostRow(Border Surface, TextBlock Name, TextBlock State, TextBlock Activity, TextBlock Permissions,
        SelectableTextBlock Fingerprint, Button Select, Button Edit, Button Revoke);
    private sealed record RemoteTaskRow(Border Surface, TextBlock Name, TextBlock State, TextBlock Stage, TextBlock Speed,
        TextBlock Message, ProgressBar Progress, Button Details, Button Cancel);
    private readonly Dictionary<string, RemoteHostRow> hostRows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RemoteTaskRow> taskRows = new(StringComparer.Ordinal);
    private readonly TextBlock noRemoteHosts = RemoteText("还没有已信任的电脑，展开下方配对步骤开始。", "caption");
    private readonly TextBlock noRemoteTasks = RemoteText("暂无可显示任务，读取电脑状态后显示。", "caption");

    private void PresentStableRemoteRows()
    {
        if (remote is null) return;
        var hosts = new List<Control>();
        foreach (PairedDevice host in remote.Hosts)
        {
            if (!hostRows.TryGetValue(host.DeviceId, out RemoteHostRow? row))
            {
                var body = new StackPanel { Spacing = 6 };
                var name = RemoteText("", "section"); var state = RemoteText("", "caption"); var activity = RemoteText("", "caption");
                var permissions = RemoteText("", "caption"); var fingerprint = Pin(host.Fingerprint);
                var select = RemoteButton("选择此电脑", SelectRemoteHost); select.Tag = host.DeviceId;
                var edit = RemoteButton("重新核对 / 修改权限", EditRemoteHost);
                var revoke = RemoteButton("撤销此电脑信任", RevokeRemoteHost); revoke.Tag = host.DeviceId;
                foreach (Control child in new Control[] { name, state, activity, permissions, fingerprint, select, edit, revoke }) body.Children.Add(child);
                row = new(RemoteGroup(body), name, state, activity, permissions, fingerprint, select, edit, revoke);
                hostRows.Add(host.DeviceId, row);
            }
            bool selectedHost = remote.SelectedHostId == host.DeviceId;
            row.Name.Text = host.Name;
            row.State.Text = (selectedHost ? "当前电脑 · " : "") + remote.HostState(host.DeviceId);
            long? connected = remote.LastConnected(host.DeviceId);
            row.Activity.Text = connected.HasValue ? "最近读取成功：" + DateTimeOffset.FromUnixTimeSeconds(connected.Value).ToLocalTime().ToString("g") : "尚未成功读取";
            if (selectedHost && remote.Invitation is { } invitation)
            {
                row.Activity.Text += "\n房间票据到期：" + DateTimeOffset.FromUnixTimeSeconds(invitation.Expires).ToLocalTime().ToString("g");
                if (invitation.Feed is { } feed) row.Activity.Text += "\n续期订阅到期：" + DateTimeOffset.FromUnixTimeSeconds(feed.Expires).ToLocalTime().ToString("g");
            }
            row.Permissions.Text = "允许读取" + (host.Permissions.Contains(HostPermission.CancelTasks) ? "、取消任务" : "（只读）")
                + (host.Permissions.Contains(HostPermission.ControlGame) ? "、请求游戏退出；电脑仍独立校验权限" : "；未授权游戏控制");
            row.Select.IsEnabled = !selectedHost;
            row.Select.Content = selectedHost ? "已选中此电脑" : "选择此电脑";
            row.Edit.Tag = host;
            hosts.Add(row.Surface);
        }
        foreach (string id in hostRows.Keys.Except(remote.Hosts.Select(host => host.DeviceId)).ToArray()) hostRows.Remove(id);
        SyncRemoteChildren(remoteHosts, hosts.Count == 0 ? [noRemoteHosts] : hosts);

        var tasks = new List<Control>();
        foreach (HostTask task in remote.Tasks)
        {
            if (!taskRows.TryGetValue(task.Id, out RemoteTaskRow? row))
            {
                var body = new StackPanel { Spacing = 6 };
                var name = RemoteText("", "section"); var state = RemoteText("", "caption"); var stage = RemoteText("", "caption");
                var speed = RemoteText("", "caption"); var message = RemoteText("", "caption"); var progress = new ProgressBar { Minimum = 0, Maximum = 100, Height = 6 };
                var details = RemoteButton("查看任务详情与脱敏日志", ReadRemoteTaskDetails); details.Tag = task.Id; details.Classes.Add("remote-task-details");
                var cancel = RemoteButton("请求取消此任务", CancelRemoteTask); cancel.Tag = task.Id; cancel.Classes.Add("remote-task-cancel");
                foreach (Control child in new Control[] { name, state, stage, speed, message, progress, details, cancel }) body.Children.Add(child);
                row = new(RemoteGroup(body), name, state, stage, speed, message, progress, details, cancel);
                taskRows.Add(task.Id, row);
            }
            row.Name.Text = string.IsNullOrWhiteSpace(task.Title) ? task.Id : task.Title;
            row.State.Text = task.State switch { "running" => "运行中", "completed" => "已完成", _ => "失败 / 已取消" };
            row.Stage.Text = task.Stage; row.Stage.IsVisible = task.Stage.Length > 0;
            row.Speed.Text = task.Speed; row.Speed.IsVisible = task.Speed.Length > 0;
            row.Message.Text = task.Message; row.Message.IsVisible = task.Message.Length > 0;
            row.Progress.IsVisible = task.ProgressPercent.HasValue;
            row.Progress.Value = task.ProgressPercent ?? 0;
            row.Details.IsEnabled = !remote.Busy;
            row.Cancel.IsVisible = task.State == "running";
            row.Cancel.IsEnabled = remote.CanCancel && !remote.Busy;
            tasks.Add(row.Surface);
        }
        foreach (string id in taskRows.Keys.Except(remote.Tasks.Select(task => task.Id)).ToArray()) taskRows.Remove(id);
        SyncRemoteChildren(remoteTasks, tasks.Count == 0 ? [noRemoteTasks] : tasks);
    }

    private static void SyncRemoteChildren(StackPanel panel, IReadOnlyList<Control> next)
    {
        for (int index = panel.Children.Count - 1; index >= 0; index--)
            if (!next.Contains(panel.Children[index])) panel.Children.RemoveAt(index);
        for (int index = 0; index < next.Count; index++)
        {
            if (index < panel.Children.Count && ReferenceEquals(panel.Children[index], next[index])) continue;
            if (panel.Children.Contains(next[index])) panel.Children.Remove(next[index]);
            panel.Children.Insert(index, next[index]);
        }
    }
}
