using System;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using HelL.Models;
using HelL.Services;

namespace HelL.Pages.Ai;

public sealed partial class MinecraftPanel : PageBase
{
    private readonly TextBox _host = Ui.Input("127.0.0.1");
    private readonly TextBox _port = Ui.Input("25565");
    private readonly TextBox _name = Ui.Input("HelLAI");
    private readonly TextBox _version = Ui.Input(I18n.L("自动检测"));
    private readonly TextBox _node = Ui.Input("node.exe");
    private readonly ComboBox _auth = Ui.Combo(new[] { I18n.L("局域网 / 离线验证"), I18n.L("微软账号") });
    private readonly TextBlock _status = Ui.Txt(I18n.L("正在读取连接状态…"), 16, true).Wrap();
    private readonly TextBlock _position = Ui.Muted("").Wrap();
    private readonly TextBlock _error = Ui.Txt("", 12, false, "B.Danger").Wrap();
    private readonly TextBlock _runtime = Ui.Small("").Wrap();
    private readonly TextBlock _login = Ui.Txt("", 20, true).Wrap();
    private readonly TextBlock _authHint = Ui.Small("").Wrap();
    private readonly ContentControl _section = new ContentControl();
    private readonly MinecraftDashboard _dashboard = new MinecraftDashboard();
    private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
    private readonly CheckBox _autoStart;
    private readonly Button _primary;
    private readonly Button _stop;
    private readonly Button _loginButton;
    private readonly Button _modelSetup;
    private readonly SPanel _credentials;
    private readonly FrameworkElement[] _sections;
    private bool _refreshing, _closed, _inputsLoaded, _working, _autoSync;
    private bool _installed, _connected, _controlled, _delegated;
    private string _phase = "disconnected", _loginUri = "";
    private string _lastConnectionError = "";
    private int _operationRevision;

    public UIElement Actions { get; }

    public MinecraftPanel()
    {
        MinWidth = 540;
        _primary = Ui.Btn(I18n.L("连接玩家"), BtnKind.Primary, (_, _) => Run(PrimaryAction));
        _stop = Ui.Btn(I18n.L("断开连接"), BtnKind.Ghost, (_, _) => Run(Stop));
        _loginButton = Ui.Btn(I18n.L("打开微软授权页面"), BtnKind.Chip, (_, _) => Run(OpenLogin));
        _modelSetup = Ui.Btn(I18n.L("准备独立游戏模型"), BtnKind.Chip, (_, _) => Run(() => Perform(PrepareModel, I18n.L("正在准备模型…"))));
        _autoStart = Ui.Switch(false, (_, _) => { if (!_autoSync) Run(SaveAutoStart); });
        AutomationProperties.SetName(_autoStart, I18n.L("随启动器启动游戏模型"));
        _credentials = Ui.V(8, _login, _loginButton.Left()); _credentials.Visibility = Visibility.Collapsed;
        _sections = new[] { BuildConnection(), BuildWorld(), BuildAdvanced() };
        _section.Content = _sections[0];
        var tabs = Ui.Segmented(new[] { I18n.L("连接"), I18n.L("世界状态"), I18n.L("高级") }, index => _section.Content = _sections[index]);
        _position.Visibility = Visibility.Collapsed;
        _error.Visibility = Visibility.Collapsed;
        Content = Ui.V(14, Ui.V(4, _status, _position), tabs, _section, _credentials, _error, _fix.Left());
        var buttons = Ui.H(8, _stop, _primary); buttons.Wrap = true;
        Actions = buttons.Right();
        _stop.Visibility = Visibility.Collapsed;
        _primary.IsEnabled = false;
        _auth.SelectionChanged += (_, _) => UpdateAuthHint();
        UpdateAuthHint();
        _dashboard.RetryRequested += receipt => Run(async () =>
        {
            string details = Text(receipt, "action") + "\n" + Text(receipt, "arguments") + "\n\n" + I18n.L("将重新执行这个移动或视角动作。请先确认当前世界状态。");
            if (await Dlg.Confirm(I18n.L("重试游戏动作"), details, I18n.L("执行")))
                await Perform(async () => { await Api.CallAsync("minecraft_retry_action", new { id = Text(receipt, "id") }); }, I18n.L("正在执行动作…"));
        });
        _timer.Tick += (_, _) => Run(RefreshState);
        Unloaded += (_, _) => { _closed = true; _timer.Stop(); };
    }

    protected override async Task LoadAsync()
    {
        try
        {
            var extensions = await Api.CallAsync("minecraft_extensions");
            _autoSync = true; _autoStart.IsChecked = Flag(extensions, "model_auto_start"); _autoSync = false;
        }
        catch (Exception ex) { ShowError(ex.Message); }
        await RefreshState();
    }

    private Task PrimaryAction() => Perform(_installed ? _connected ? AllowControl : Connect : Install,
        I18n.L(_installed ? _connected ? "正在授权…" : "正在连接…" : "正在安装组件…"));

    private async Task Perform(Func<Task> operation, string label)
    {
        if (_working || _closed) return;
        _working = true; _primary.IsEnabled = false; _modelSetup.IsEnabled = false; _autoStart.IsEnabled = false;
        int revision = ++_operationRevision;
        _discover.IsEnabled = _probe.IsEnabled = _fix.IsEnabled = false;
        _error.Visibility = Visibility.Collapsed; _status.Text = label;
        try { await operation(); }
        catch (FieldValidationException ex) { if (revision == _operationRevision) { FieldState.SetError(ex.Field, ex.Message); ex.Field.Focus(); } }
        catch (Exception ex) { if (revision == _operationRevision) { ShowError(ex.Message); Smoke.Note("minecraft-panel", ex.Message); } }
        finally
        {
            if (revision == _operationRevision)
            {
                _working = false; _modelSetup.IsEnabled = true; _autoStart.IsEnabled = true;
                _discover.IsEnabled = _probe.IsEnabled = _fix.IsEnabled = true;
                await RefreshState();
            }
        }
    }

    private async Task Save()
    {
        if (Uri.CheckHostName(_host.Text.Trim()) == UriHostNameType.Unknown) throw new FieldValidationException(_host, I18n.L("请输入主机名或 IP，不要带协议或端口。"));
        if (!int.TryParse(_port.Text, out int port) || port < 1 || port > 65535) throw new FieldValidationException(_port, I18n.L("端口应在 1 至 65535 之间。"));
        await Api.CallAsync("minecraft_configure", new { host = _host.Text, port, name = _name.Text, auth = _auth.SelectedIndex == 1 ? "microsoft" : "offline", version = _version.Text, node = _node.Text });
    }
    private async Task Install()
    {
        await WaitForTask(await Api.StartTaskAsync("minecraft_install", new { node = _node.Text }), 330);
        _runtimeDiagnosis = null;
        await Diagnose();
    }
    private async Task Connect()
    {
        int revision = _operationRevision;
        await Save();
        if (_closed || revision != _operationRevision) return;
        foreach (var input in ConnectionInputs()) input.IsEnabled = false;
        _stop.Visibility = Visibility.Visible; SetLabel(_stop, I18n.L("取消连接"));
        await Api.CallAsync("minecraft_connect");
    }
    private async Task AllowControl() { await Api.CallAsync("minecraft_enable_tools", new { enabled = true }); }
    private async Task Stop()
    {
        ++_operationRevision; ++_targetRevision;
        await Api.CallAsync("minecraft_stop"); _working = false;
        _discover.IsEnabled = _probe.IsEnabled = _fix.IsEnabled = _autoStart.IsEnabled = _modelSetup.IsEnabled = true;
        _error.Visibility = _fix.Visibility = Visibility.Collapsed; await RefreshState();
    }
    private async Task PrepareModel() { await WaitForTask(await Api.StartTaskAsync("minecraft_prepare_model"), 1200); }
    private static async Task WaitForTask(string task, int timeout)
    {
        var reply = await Api.CallAsync<OpResult>("wait_task", new { task_id = task, timeout });
        if (reply?.Ok != true) throw new InvalidOperationException(reply?.Message ?? I18n.L("操作未完成，请查看下载任务。"));
    }
    private async Task SaveAutoStart()
    {
        _autoStart.IsEnabled = false;
        try { await Api.CallAsync("minecraft_extension_set", new { extension = "model_auto_start", enabled = _autoStart.IsChecked == true }); }
        catch (Exception ex) { _autoSync = true; _autoStart.IsChecked = !(_autoStart.IsChecked == true); _autoSync = false; ShowError(ex.Message); }
        finally { _autoStart.IsEnabled = true; }
    }

    private async Task RefreshState()
    {
        if (_refreshing || _closed || _working) return;
        _refreshing = true;
        int revision = _operationRevision;
        try { var state = await Api.CallAsync("minecraft_status"); if (!_closed && !_working && revision == _operationRevision) ApplyState(state); }
        catch (Exception ex) { ShowError(I18n.L("状态读取失败：{0}", ex.Message)); }
        finally { _refreshing = false; }
        if (!_closed && !_working && revision != _operationRevision) await RefreshState();
    }

    private void ApplyState(JsonElement state)
    {
        if (!_inputsLoaded && state.TryGetProperty("options", out var options))
        {
            _host.Text = Text(options, "host"); _port.Text = Text(options, "port"); _name.Text = Text(options, "name");
            _version.Text = Text(options, "version"); _node.Text = Text(options, "node");
            _auth.SelectedIndex = Text(options, "auth") == "microsoft" ? 1 : 0; _inputsLoaded = true;
        }
        _phase = Text(state, "phase"); _installed = Flag(state, "runtime_ready") && (!_runtimeDiagnosis.HasValue || Flag(_runtimeDiagnosis.Value, "ready")); _connected = Flag(state, "connected");
        _controlled = Flag(state, "tools_enabled"); _delegated = Text(state, "controller") == "feiyu";
        bool active = _connected || _phase is "checking" or "connecting" or "authorizing";
        _status.Text = I18n.L(_phase switch
        {
            "checking" => "正在检测服务器和组件…", "connecting" => "正在连接服务器…", "authorizing" => "等待微软授权…", "dead" => "角色已死亡", "error" => "连接未完成",
            "ready" => _delegated ? "大肥鱼正在控制" : _controlled ? "已连接 · AI 控制已开启" : "已连接 · 等待授权", _ => "尚未连接"
        });
        _runtime.Text = _runtimeDiagnosis.HasValue ? I18n.L(Text(_runtimeDiagnosis.Value, "message")) : I18n.L(_installed ? "游戏组件已就绪" : "首次连接前需要安装游戏组件");
        _position.Text = _connected ? Text(state, "player") + " · " + Text(state, "host") + ":" + Text(state, "port") : "";
        _position.Visibility = _connected ? Visibility.Visible : Visibility.Collapsed;
        _dashboard.Update(state);
        foreach (var input in ConnectionInputs()) input.IsEnabled = !active;
        _discover.IsEnabled = _probe.IsEnabled = !active;
        SetLabel(_primary, I18n.L(_connected ? "允许 AI 控制" : _installed ? _phase == "error" ? "重新连接" : "连接玩家" : "安装游戏组件"));
        _primary.Visibility = _controlled || _delegated ? Visibility.Collapsed : Visibility.Visible;
        _primary.IsEnabled = !_working && (_connected || !active);
        _stop.Visibility = active || _phase == "dead" ? Visibility.Visible : Visibility.Collapsed;
        SetLabel(_stop, I18n.L(_controlled || _delegated || Text(state, "action").Length > 0 ? "立即停止并断开" : "断开连接"));
        _modelSetup.IsEnabled = !active && !_working;
        _loginUri = ""; _login.Text = "";
        if (state.TryGetProperty("login", out var login) && login.ValueKind == JsonValueKind.Object) { _loginUri = Text(login, "uri"); _login.Text = Text(login, "code"); }
        _credentials.Visibility = _loginUri.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        string error = Text(state, "error");
        if (error.Length > 0 && error != _lastConnectionError) { ShowError(error); SetRecovery(Text(state, "recovery").Length > 0 ? Text(state, "recovery") : "retry"); }
        _lastConnectionError = error;
        if (state.TryGetProperty("server", out var server) && server.ValueKind == JsonValueKind.Object && active) ShowProbe(server);
        if (active) _timer.Start(); else _timer.Stop();
    }

    private Control[] ConnectionInputs() => new Control[] { _host, _port, _name, _auth, _version, _node };
    private void ShowError(string message) { _error.Text = I18n.L(message); _error.Visibility = Visibility.Visible; }
    private void UpdateAuthHint() => _authHint.Text = I18n.L(_auth.SelectedIndex == 1 ? "使用机器人自己的正版账号，连接后完成微软授权。" : "适用于开放到局域网的世界或允许离线玩家的服务器。");
    private Task OpenLogin()
    {
        if (Uri.TryCreate(_loginUri, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
            (uri.Host == "microsoft.com" || uri.Host == "www.microsoft.com" || uri.Host == "login.live.com")) Ui.OpenUrl(_loginUri);
        else throw new InvalidOperationException(I18n.L("微软授权地址无效。"));
        return Task.CompletedTask;
    }
    private static void SetLabel(Button button, string text) { button.Content = text; AutomationProperties.SetName(button, text); }
    private static bool Flag(JsonElement value, string key) => value.TryGetProperty(key, out var property) && property.ValueKind == JsonValueKind.True;
    private static string Text(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var property) && property.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined) ? property.ToString() : "";
}
