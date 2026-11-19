using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using HelL.Core;
using HelL.Core.Launcher.Ai.Minecraft;
using HelL.Models;
using HelL.Services;

namespace HelL.Pages;

internal sealed class DashboardLiveModule : Grid
{
    private readonly string _kind;
    private readonly Func<string> _instance;
    private readonly SPanel _rows = Ui.V(8);
    private readonly TextBlock _status = Ui.Small("");
    private readonly Button _refresh;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(30) };
    private bool _busy;
    private int _generation;

    public DashboardLiveModule(string kind, Func<string> instance)
    {
        _kind = kind; _instance = instance;
        if (kind == "tasks") _timer.Interval = TimeSpan.FromSeconds(1);
        _refresh = Ui.IconBtn("\ue72c", "刷新模块", (_, _) => PageBase.Run(Refresh));
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); RowDefinitions.Add(new RowDefinition());
        var header = Ui.G(null, "*,Auto"); header.Add(_status, 0); header.Add(_refresh, 0, 1); Children.Add(header);
        var scroll = Ui.Scroll(_rows); SetRow(scroll, 1); Children.Add(scroll);
        Loaded += (_, _) => { PageBase.Run(Refresh); _timer.Start(); };
        Unloaded += (_, _) => { _generation++; _timer.Stop(); };
        _timer.Tick += (_, _) => PageBase.Run(Refresh);
    }

    private async Task Refresh()
    {
        if (_busy) return;
        _busy = true; int generation = ++_generation; string instance = _instance(); _refresh.IsEnabled = false;
        _status.Text = "正在刷新…";
        try
        {
            if (_kind == "screenshots")
            {
                if (instance.Length == 0) { Empty("先选择一个实例，即可浏览它的截图。"); return; }
                var shots = await AppServices.Client.CallAsync<ScreenshotPage>("instance_screenshots", new { instance, limit = 4, offset = 0 }) ?? new();
                if (generation != _generation) return;
                _rows.Children.Clear(); _status.Text = instance + " · " + shots.Total + " 张截图";
                var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
                foreach (var shot in shots.Items)
                {
                    var image = new LocalPicture(double.NaN, 110, "正在读取截图…"); _ = image.SetPath(shot.Path, Path.GetDirectoryName(shot.Path)!, shot.Name);
                    var open = Ui.Btn("", BtnKind.Ghost, (_, _) => PageBase.Run(() => InstanceArtworkDialog.Show(instance, Refresh))); open.Content = image; open.Padding = new Thickness(0); open.Margin = new Thickness(3); open.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                    System.Windows.Automation.AutomationProperties.SetName(open, "浏览实例截图 " + shot.Name); grid.Children.Add(open);
                }
                if (shots.Total == 0) _rows.Children.Add(Ui.Muted("还没有截图。在游戏中按 F2 拍摄后，这里会显示真实游戏画面。").Wrap());
                else _rows.Children.Add(grid);
                _rows.Children.Add(Ui.Btn("全部截图与封面", BtnKind.Ghost, (_, _) => PageBase.Run(() => InstanceArtworkDialog.Show(instance, Refresh))));
            }
            else if (_kind == "servers")
            {
                if (instance.Length == 0) { Empty("先选择实例，或在服务器页添加常用服务器。"); return; }
                var servers = await AppServices.Client.CallAsync<System.Collections.Generic.List<ServerRow>>("list_servers", new { instance }) ?? new();
                var checks = await Task.WhenAll(servers.Take(3).Select(async server => (Server: server, Status: await MinecraftServerProbe.CheckAsync(server.Ip, server.Port, timeoutMs: 1800))));
                if (generation != _generation) return;
                _rows.Children.Clear(); _status.Text = "服务器状态 · " + DateTime.Now.ToString("HH:mm") + " 更新";
                foreach (var check in checks)
                {
                    bool online = Py.Truthy(check.Status.Get("online"));
                    string summary = online ? Py.S(check.Status.Get("players")) + " 人在线 · " + Py.S(check.Status.Get("latency_ms")) + " ms · " + Py.S(check.Status.Get("version")) : "暂时无法连接 · 可刷新重试";
                    var state = Ui.Txt(online ? "在线" : "离线", 12, true, online ? "B.Ok" : "B.InkMuted");
                    var serverRow = Ui.G(null, "*,Auto");
                    serverRow.Add(Ui.V(4, Ui.Txt(check.Server.Name.Length > 0 ? check.Server.Name : check.Server.Ip, 14, true).Trim(), Ui.Small(summary).Wrap()), 0);
                    serverRow.Add(state.M(8, 0, 0, 0), 0, 1); _rows.Children.Add(serverRow.M(0, 6, 0, 6));
                }
                if (servers.Count == 0) _rows.Children.Add(Ui.Muted("这个实例尚未添加服务器。").Wrap());
                _rows.Children.Add(Ui.Btn("管理与连接服务器", BtnKind.Ghost, (_, _) => AppServices.Window?.Navigate("servers")).Left());
            }
            else
            {
                _rows.Children.Clear(); var tasks = TaskStore.Rows.Where(task => !task.Finished).Take(4).ToList();
                _status.Text = tasks.Count == 0 ? "下载队列为空" : TaskStore.RunningCount + " 项任务进行中";
                foreach (var task in tasks)
                {
                    var progress = Ui.Prog(); progress.Value = task.Progress; progress.IsIndeterminate = task.Indeterminate; progress.Height = 4;
                    _rows.Children.Add(Ui.V(5, Ui.Txt(task.Title, 13, true).Trim(), progress, Ui.Small(task.Status + " " + task.Speed).Wrap()).M(0, 5, 0, 5));
                }
                if (tasks.Count == 0) _rows.Children.Add(Ui.Muted("安装游戏、模组或整合包时，可在这里查看进度。").Wrap());
                _rows.Children.Add(Ui.Btn("查看下载任务", BtnKind.Ghost, (_, _) => AppServices.Window?.Navigate("tasks")).Left());
            }
        }
        catch (Exception ex) { if (generation == _generation) { _status.Text = "模块读取失败"; Empty(ex.Message + " 点击刷新重试。"); } Smoke.Note("home-" + _kind, ex.ToString()); }
        finally { _busy = false; _refresh.IsEnabled = true; }
    }
    private void Empty(string message) { _status.Text = "暂无内容"; _rows.Children.Clear(); _rows.Children.Add(Ui.Muted(message).Wrap()); }
}
