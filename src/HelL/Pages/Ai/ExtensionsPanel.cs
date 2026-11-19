using System;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using HelL.Services;

namespace HelL.Pages.Ai;

public sealed class ExtensionsPanel : PageBase
{
    private readonly CheckBox _feiyu;
    private readonly TextBlock _gameStatus = Ui.Small("");
    private readonly TextBlock _linkStatus = Ui.Small("").Wrap();
    private readonly TextBlock _error = Ui.Txt("", 12, false, "B.Danger").Wrap();
    private readonly SPanel _linkOptions;
    private readonly Button _handoff;
    private readonly Button _stop;
    private readonly Button _retry;
    private bool _sync;
    private bool _ready;

    public ExtensionsPanel(Action close)
    {
        var back = Ui.Btn(I18n.L("返回对话"), BtnKind.Ghost, (_, _) => close(), "\ue72b");
        var open = Ui.Btn(I18n.L("打开 AI 玩家"), BtnKind.Chip, (_, _) => Run(OpenPlayer));
        _feiyu = Ui.Switch(false, (_, _) => { if (!_sync) Run(ToggleFeiyu); });
        AutomationProperties.SetName(_feiyu, I18n.L("启用大肥鱼联动"));
        _handoff = Ui.Btn(I18n.L("交给大肥鱼"), BtnKind.Chip, (_, _) => RunOnce(_handoff!, Handoff));
        _stop = Ui.Btn(I18n.L("停止联动"), BtnKind.Ghost, (_, _) => Run(async () => { await Api.CallAsync("minecraft_stop"); await RefreshAsync(); }));
        _retry = Ui.Btn(I18n.L("重试"), BtnKind.Ghost, (_, _) => Run(RefreshAsync)); _retry.Visibility = Visibility.Collapsed;
        var linkActions = Ui.H(8, _handoff, _stop); linkActions.Wrap = true;
        _linkOptions = Ui.V(10, Ui.Inset(Ui.V(10, _linkStatus, linkActions))); _linkOptions.Visibility = Visibility.Collapsed;
        Content = ScrollBody(back.Left(), Ui.V(5, Ui.H1(I18n.L("拓展")), Ui.Muted(I18n.L("按需使用，让启动器保持简单。")).Wrap()),
            ExtensionRow(I18n.L("AI 玩家"), I18n.L("让独立角色加入 Java 世界，观察、跟随和执行游戏任务。"), open, _gameStatus),
            Ui.Sep(), ExtensionRow(I18n.L("大肥鱼联动"), I18n.L("可选接入大肥鱼工作台，共享已连接的游戏角色。"), _feiyu, Ui.Small(I18n.L("默认关闭 · 启用后仍需单独授权接管"))),
            _linkOptions, _error, _retry.Left());
    }

    private static FrameworkElement ExtensionRow(string title, string description, UIElement action, UIElement note)
    {
        if (action is FrameworkElement control) control.VerticalAlignment = VerticalAlignment.Center;
        return new PageHeader(Ui.V(5, Ui.Txt(title, 16, true).Wrap(), Ui.Muted(description).Wrap(), note), action);
    }
    protected override async Task LoadAsync()
    {
        try
        {
            var settings = await Api.CallAsync("minecraft_extensions");
            var game = await Api.CallAsync("minecraft_status");
            _sync = true;
            try { _feiyu.IsChecked = settings.GetProperty("feiyu_enabled").GetBoolean(); }
            finally { _sync = false; }
            bool enabled = _feiyu.IsChecked == true;
            _ready = game.TryGetProperty("connected", out var connected) && connected.ValueKind == JsonValueKind.True;
            bool delegated = game.TryGetProperty("controller", out var controller) && controller.GetString() == "feiyu";
            _gameStatus.Text = I18n.L(_ready ? "已连接" : "按需打开，不会自动连接游戏");
            _linkOptions.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            _linkStatus.Text = I18n.L(delegated ? "大肥鱼正在控制。停止会断开当前 AI 玩家。" : _ready ? "AI 玩家已连接，可将控制权交给大肥鱼。" : "先连接 AI 玩家，再授权大肥鱼接管。");
            _handoff.Content = I18n.L(_ready ? "交给大肥鱼" : "连接 AI 玩家");
            AutomationProperties.SetName(_handoff, I18n.L(_ready ? "交给大肥鱼" : "连接 AI 玩家"));
            _handoff.Visibility = delegated ? Visibility.Collapsed : Visibility.Visible;
            _stop.Visibility = delegated ? Visibility.Visible : Visibility.Collapsed;
            _error.Text = ""; _retry.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex) { _error.Text = I18n.L("拓展状态读取失败：{0}", ex.Message); _retry.Visibility = Visibility.Visible; }
    }
    private async Task OpenPlayer()
    {
        var panel = new MinecraftPanel();
        Dlg.Panel(I18n.L("AI 玩家"), panel, 660, onClose: () => Run(RefreshAsync), footer: panel.Actions);
        await panel.EnsureLoadedAsync();
    }
    private async Task ToggleFeiyu()
    {
        _feiyu.IsEnabled = false;
        try
        {
            await Api.CallAsync("minecraft_extension_set", new { extension = "feiyu", enabled = _feiyu.IsChecked == true });
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _sync = true; _feiyu.IsChecked = !(_feiyu.IsChecked == true); _sync = false;
            _error.Text = ex.Message;
        }
        finally { _feiyu.IsEnabled = true; }
    }
    private async Task Handoff()
    {
        if (!_ready) { await OpenPlayer(); return; }
        if (!await Dlg.Ask(I18n.L("大肥鱼联动"), Ui.Muted(I18n.L("允许大肥鱼控制当前角色。接管期间，启动器对话暂停操作；任一侧停止都会断开角色。")).Wrap(), I18n.L("允许接管"))) return;
        await Api.CallAsync("minecraft_allow_feiyu");
        await RefreshAsync();
    }
}
