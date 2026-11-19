using System;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using HelL.Services;

namespace HelL.Pages.Ai;

public sealed partial class MinecraftPanel
{
    private readonly TextBlock _serverInfo = Ui.Muted("").Wrap();
    private readonly SPanel _servers = Ui.V(6);
    private readonly Button _discover = Ui.Btn(I18n.L("查找局域网"), BtnKind.Ghost, glyph: "\ue774");
    private readonly Button _probe = Ui.Btn(I18n.L("检测服务器"), BtnKind.Ghost, glyph: "\ue72c");
    private readonly Button _fix = Ui.Btn(I18n.L("重新检测"), BtnKind.Chip);
    private string _recovery = "";
    private int _targetRevision;
    private JsonElement? _runtimeDiagnosis;

    private FrameworkElement BuildNetworkTools()
    {
        _fix.Visibility = Visibility.Collapsed; _serverInfo.Visibility = Visibility.Collapsed;
        _discover.Click += (_, _) => Run(() => Perform(Discover, I18n.L("正在查找局域网世界…")));
        _probe.Click += (_, _) => Run(() => Perform(Probe, I18n.L("正在检测服务器…")));
        _fix.Click += (_, _) => Run(() => Perform(Recover, I18n.L("正在处理…")));
        _host.TextChanged += (_, _) => InvalidateProbe(); _port.TextChanged += (_, _) => InvalidateProbe(); _version.TextChanged += (_, _) => InvalidateProbe();
        _node.TextChanged += (_, _) => { _runtimeDiagnosis = null; };
        var actions = Ui.H(8, _discover, _probe); actions.Wrap = true;
        var list = Ui.Scroll(_servers); list.MaxHeight = 160;
        return Ui.V(8, actions, _serverInfo, list);
    }
    private void InvalidateProbe()
    {
        _targetRevision++; _recovery = ""; _serverInfo.Text = ""; _serverInfo.Visibility = Visibility.Collapsed;
        _fix.Visibility = Visibility.Collapsed; _error.Visibility = Visibility.Collapsed;
    }
    private (string Host, int Port) Target()
    {
        if (Uri.CheckHostName(_host.Text.Trim()) == UriHostNameType.Unknown) throw new FieldValidationException(_host, I18n.L("请输入主机名或 IP，不要带协议或端口。"));
        if (!int.TryParse(_port.Text, out int port) || port < 1 || port > 65535) throw new FieldValidationException(_port, I18n.L("端口应在 1 至 65535 之间。"));
        return (_host.Text.Trim(), port);
    }
    private async Task Probe()
    {
        var target = Target(); int revision = _targetRevision;
        var status = await Api.CallAsync("minecraft_probe", new { host = target.Host, port = target.Port, version = _version.Text.Trim() });
        if (_closed || revision != _targetRevision) return;
        ShowProbe(status);
    }
    private void ShowProbe(JsonElement status)
    {
        _serverInfo.Visibility = Visibility.Visible;
        bool online = Flag(status, "online");
        _serverInfo.Text = online
            ? I18n.L("{0} · {1} ms · {2}/{3} 人", Text(status, "version"), Text(status, "latency_ms"), Text(status, "players"), Text(status, "max_players"))
                + "\n" + I18n.L(Text(status, "message"))
            : I18n.L(Text(status, "message"));
        if (Text(status, "code") == "unsupported_protocol") _serverInfo.Text += "\n" + I18n.L("当前组件范围：{0}", Text(status, "supported_range"));
        SetRecovery(Text(status, "recovery"));
    }
    private async Task Discover()
    {
        int revision = _targetRevision;
        _servers.Children.Clear();
        var found = await Api.CallAsync("minecraft_discover");
        if (_closed || revision != _targetRevision) return;
        SetRecovery("none");
        _serverInfo.Visibility = Visibility.Visible; _serverInfo.Text = I18n.L(Text(found, "message"));
        if (found.TryGetProperty("warnings", out var warnings))
            foreach (var warning in warnings.EnumerateArray()) _serverInfo.Text += "\n" + I18n.L(warning.GetString() ?? "");
        if (found.TryGetProperty("unreachable", out var unreachable))
            foreach (var entry in unreachable.EnumerateArray())
                _serverInfo.Text += "\n" + Text(entry, "host") + ":" + Text(entry, "port") + " · " + I18n.L(Text(entry, "message"));
        foreach (var entry in found.GetProperty("servers").EnumerateArray())
        {
            var server = entry.Clone();
            var use = Ui.Btn(I18n.L("使用此世界"), BtnKind.Ghost, (_, _) =>
            {
                _host.Text = Text(server, "host"); _port.Text = Text(server, "port");
                _servers.Children.Clear(); Run(() => Perform(Probe, I18n.L("正在检测服务器…")));
            });
            string address = Text(server, "host") + ":" + Text(server, "port");
            var row = new PageHeader(Ui.V(2, Ui.Txt(Text(server, "name"), 13, true).Wrap(), Ui.Small(address + " · " + Text(server, "version")).Wrap()), use.VCenter());
            _servers.Children.Add(row);
        }
    }
    private void SetRecovery(string recovery)
    {
        _recovery = recovery;
        string label = recovery switch { "repair" => "修复并重新检测", "auto_version" => "恢复自动版本", "choose_node" => "选择 Node.js", "choose_server" => "查找其他世界", "discover" => "查找局域网", "retry" => "重新检测", _ => "" };
        _fix.Visibility = label.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        SetLabel(_fix, I18n.L(label));
    }
    private async Task Recover()
    {
        switch (_recovery)
        {
            case "repair": await Install(); if (_installed) await Probe(); break;
            case "auto_version": _version.Text = ""; await Probe(); break;
            case "discover": case "choose_server": await Discover(); break;
            case "choose_node":
                string? node = Dlg.PickFile("Node.js (node.exe)|node.exe", I18n.L("选择 Node.js"));
                if (node != null) { _node.Text = node; await Diagnose(); } break;
            default: await Probe(); break;
        }
    }
    private async Task Diagnose()
    {
        string node = _node.Text;
        var status = await Api.CallAsync("minecraft_diagnose", new { node });
        if (_closed || _node.Text != node) return;
        var runtime = status.GetProperty("runtime"); _runtimeDiagnosis = runtime.Clone(); _installed = Flag(runtime, "ready");
        _runtime.Text = I18n.L(Text(runtime, "message"));
        if (!_installed) ShowError(Text(runtime, "message"));
        SetRecovery(Text(runtime, "recovery"));
    }
}
