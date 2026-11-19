using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using HelL.Services;

namespace HelL.Pages.Ai;

public sealed partial class MinecraftPanel
{
    private FrameworkElement BuildConnection()
    {
        var address = Ui.G(null, "*,12,100");
        address.Add(Field(I18n.L("服务器地址"), _host), 0); address.Add(Field(I18n.L("端口"), _port), 0, 2);
        var account = Ui.G(null, "*,12,*");
        account.Add(Field(I18n.L("登录方式"), _auth), 0); account.Add(Field(I18n.L("玩家名 / 账号标识"), _name), 0, 2);
        return Ui.V(14, address, BuildNetworkTools(), account, _authHint, Ui.Sep(), _runtime,
            Ui.Small(I18n.L("同一台电脑填 127.0.0.1，端口使用游戏开放局域网后显示的数字。")).Wrap());
    }
    private FrameworkElement BuildWorld() => Ui.V(10, _dashboard,
        Ui.Small(I18n.L("连接后在对话中安排任务，移动和修改世界遵循你的权限设置。")).Wrap());

    private FrameworkElement BuildAdvanced()
    {
        var pickNode = Ui.Btn(I18n.L("选择"), BtnKind.Ghost, (_, _) =>
        {
            string? path = Dlg.PickFile("Node.js (node.exe)|node.exe", I18n.L("选择 Node.js"));
            if (path != null) _node.Text = path;
        });
        var nodeRow = Ui.G(null, "*,Auto"); nodeRow.Add(_node, 0); nodeRow.Add(pickNode.M(8, 0, 0, 0), 0, 1);
        var repair = Ui.Btn(I18n.L("修复游戏组件"), BtnKind.Ghost, (_, _) => Run(() => Perform(Install, I18n.L("正在修复组件…"))));
        var diagnose = Ui.Btn(I18n.L("检测游戏组件"), BtnKind.Ghost, (_, _) => Run(() => Perform(Diagnose, I18n.L("正在检测组件…"))));
        var clearLogin = Ui.Btn(I18n.L("清除机器人登录"), BtnKind.Ghost, (_, _) => Run(async () =>
        {
            if (await Dlg.Ask(I18n.L("清除机器人登录"), Ui.Muted(I18n.L("下次使用微软模式需要重新授权。启动器中的玩家账号不受影响。")).Wrap(), I18n.L("清除")))
                await Perform(async () => { await Api.CallAsync("minecraft_clear_login"); }, I18n.L("正在清除登录…"));
        }));
        var maintenance = Ui.H(8, diagnose, repair, clearLogin); maintenance.Wrap = true;
        var auto = Ui.G(null, "*,Auto");
        auto.Add(Ui.V(3, Ui.Txt(I18n.L("随启动器启动游戏模型"), 13), Ui.Small(I18n.L("默认关闭，准备好模型后仍可随时手动启动。")).Wrap()), 0);
        auto.Add(_autoStart.VCenter().M(12, 0, 0, 0), 0, 1);
        return Ui.V(14, Field(I18n.L("游戏版本"), _version), Field("Node.js 22+", nodeRow), maintenance,
            Ui.Sep(), Ui.Txt(I18n.L("独立本地模型 · 可选"), 14, true),
            Ui.Muted(I18n.L("Qwen3 1.7B，约 1.4 GB。使用独立目录和端口，适合本地游戏任务。")).Wrap(),
            _modelSetup.Left(), auto);
    }
    private static FrameworkElement Field(string label, FrameworkElement input)
    {
        AutomationProperties.SetName(input, label);
        var caption = Ui.Small(label);
        AutomationProperties.SetLabeledBy(input, caption);
        return Ui.V(5, caption, input, FieldState.ErrorText(input));
    }
}
