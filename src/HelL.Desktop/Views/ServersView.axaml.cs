using System.Diagnostics;
using System.Net.Sockets;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using HelL.Core;
using HelL.Core.Launcher;

namespace HelL.Desktop.Views;

public partial class ServersView : UserControl
{
    public sealed record ServerRow(long Index, string Name, string Host, int Port, string FullAddress, string AddressLabel, int? LatencyMs, string Description = "")
    {
        public string PingText => LatencyMs switch
        {
            null => "待测速",
            < 0 => "离线",
            var ms => $"{ms} ms"
        };
        public IBrush PingBg => LatencyMs switch
        {
            > 0 and < 120 => new SolidColorBrush(Color.Parse("#1F4ADE80")),
            >= 120 => new SolidColorBrush(Color.Parse("#22FBBF24")),
            < 0 => new SolidColorBrush(Color.Parse("#22F87171")),
            _ => new SolidColorBrush(Color.Parse("#1894A3B8"))
        };
        public IBrush PingBd => LatencyMs switch
        {
            > 0 and < 120 => new SolidColorBrush(Color.Parse("#525EE397")),
            >= 120 => new SolidColorBrush(Color.Parse("#55FBBF24")),
            < 0 => new SolidColorBrush(Color.Parse("#55F87171")),
            _ => new SolidColorBrush(Color.Parse("#3094A3B8"))
        };
        public IBrush PingFg => LatencyMs switch
        {
            > 0 and < 120 => new SolidColorBrush(Color.Parse("#5EE397")),
            >= 120 => new SolidColorBrush(Color.Parse("#FDE047")),
            < 0 => new SolidColorBrush(Color.Parse("#FCA5A5")),
            _ => new SolidColorBrush(Color.Parse("#94A3B8"))
        };
    }

    private MainWindow? host;
    private string currentInstance = "";
    private ServerRow[] currentRows = [];
    private (string Instance, ServerRow Server)? removed;

    public ServersView() => InitializeComponent();

    internal void Initialize(MainWindow window) => host = window;

    internal void Reload(string? instanceName = null)
    {
        if (host == null) return;
        currentInstance = instanceName ?? host.GameLauncher.SelectedInstance;
        if (string.IsNullOrWhiteSpace(currentInstance) || !host.GameLauncher.InstancesList().Any(i => i.Name == currentInstance))
            currentInstance = host.GameLauncher.InstancesList().FirstOrDefault()?.Name ?? "";

        LocalIpItems.ItemsSource = Lan.LocalIps().Select(Py.S).Where(ip => ip.Length > 0).ToArray();
        LanGuideText.Text = Lan.LanHint(25565L);

        if (string.IsNullOrWhiteSpace(currentInstance))
        {
            SubtitleText.Text = "请先在左侧选择或创建一个实例后管理服务器列表";
            ServerList.ItemsSource = Array.Empty<ServerRow>();
            EmptyServers.IsVisible = true;
            ServerCountBadge.Text = "0 个服务器";
            return;
        }

        SubtitleText.Text = $"当前实例：{currentInstance} · 与游戏内多人游戏列表自动互通";
        var inst = new Instance(currentInstance);
        var rows = Servers.ListServers(inst).OfType<PyDict>().Select((dict, idx) =>
        {
            string ip = Py.S(dict.Get("ip"));
            long port = Py.Int(dict.Get("port", 25565L));
            string full = port == 25565 ? ip : $"{ip}:{port}";
            string name = Py.S(dict.Get("name", full));
            string desc = Py.S(dict.Get("description"));
            string label = string.IsNullOrWhiteSpace(desc) ? full : $"{full} · {desc}";
            return new ServerRow(idx, name, ip, (int)Math.Clamp(port, 1, 65535), full, label, null, desc);
        }).ToArray();

        currentRows = rows;
        ServerList.ItemsSource = rows;
        EmptyServers.IsVisible = rows.Length == 0;
        ServerCountBadge.Text = $"{rows.Length} 个服务器";
    }

    private void RefreshServers(object? sender, RoutedEventArgs e) => Reload(currentInstance);

    private async void PingServers(object? sender, RoutedEventArgs e)
    {
        if (host == null || currentRows.Length == 0) return;
        await host.RunTask(async token =>
        {
            host.SetStatus("正在检测服务器连通性与 TCP 延迟…");
            var tasks = currentRows.Select(async row =>
            {
                try
                {
                    using var tcp = new TcpClient();
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(token);
                    linked.CancelAfter(TimeSpan.FromMilliseconds(2200));
                    var sw = Stopwatch.StartNew();
                    await tcp.ConnectAsync(row.Host, row.Port, linked.Token);
                    sw.Stop();
                    return row with { LatencyMs = Math.Max(1, (int)sw.ElapsedMilliseconds) };
                }
                catch
                {
                    return row with { LatencyMs = -1 };
                }
            });
            currentRows = await Task.WhenAll(tasks);
            ServerList.ItemsSource = currentRows;
            int online = currentRows.Count(r => r.LatencyMs is > 0);
            host.SetStatus($"服务器测速完成：{online}/{currentRows.Length} 个可达。");
        });
    }

    private async void AddServer(object? sender, RoutedEventArgs e)
    {
        if (host == null || string.IsNullOrEmpty(currentInstance)) return;
        string rawHost = ServerHostInput.Text?.Trim() ?? "";
        if (rawHost.Length == 0)
        {
            host.SetStatus("请先填写服务器 IP 或域名地址。");
            ServerHostInput.Focus();
            return;
        }
        string name = ServerNameInput.Text?.Trim() ?? "";
        if (name.Length == 0) name = rawHost;
        string note = ServerNoteInput.Text?.Trim() ?? "";
        await host.RunTask(_ =>
        {
            var (addr, port) = Servers._split_addr(rawHost);
            Servers.AddServer(new Instance(currentInstance), name, addr, port, note);
            ServerNameInput.Clear();
            ServerHostInput.Clear();
            ServerNoteInput.Clear();
            Reload(currentInstance);
            host.SetStatus($"已将服务器「{name}」保存到实例 {currentInstance}。");
            return Task.CompletedTask;
        });
    }

    private async void RemoveServer(object? sender, RoutedEventArgs e)
    {
        if (host == null || string.IsNullOrEmpty(currentInstance) || sender is not Button { Tag: long index }) return;
        string target = currentInstance;
        ServerRow? server = currentRows.FirstOrDefault(row => row.Index == index);
        if (server is null || !await host.ConfirmAction("移除服务器", $"实例：{target}\n服务器：{server.Name}\n地址：{server.FullAddress}\n仅移除本机列表记录，不影响服务器。", "移除")) return;
        await host.RunTask(_ =>
        {
            Servers.DeleteServer(new Instance(target), index);
            removed = (target, server);
            UndoRemoveButton.IsVisible = true;
            Reload(currentInstance);
            host.SetStatus("已移除所选服务器。");
            return Task.CompletedTask;
        });
    }

    private async void UndoRemoveServer(object? sender, RoutedEventArgs e)
    {
        if (host == null || removed is not { } undo) return;
        await host.RunTask(_ =>
        {
            Servers.AddServer(new Instance(undo.Instance), undo.Server.Name, undo.Server.Host, undo.Server.Port, undo.Server.Description);
            removed = null; UndoRemoveButton.IsVisible = false;
            Reload(undo.Instance); host.SetStatus("已恢复服务器列表记录。");
            return Task.CompletedTask;
        });
    }

    private async void CopyServerAddress(object? sender, RoutedEventArgs e)
    {
        if (host?.Clipboard == null || sender is not Button { Tag: string text }) return;
        await host.Clipboard.SetTextAsync(text);
        host.SetStatus($"已复制地址：{text}");
    }

    private void ConnectServer(object? sender, RoutedEventArgs e)
    {
        if (host == null || string.IsNullOrEmpty(currentInstance) || sender is not Button { Tag: string addr }) return;
        host.QuickConnectServer(currentInstance, addr);
    }
}
