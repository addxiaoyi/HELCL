using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HelL.Models;
using HelL.Services;

namespace HelL.Pages;

public sealed partial class LaunchPage
{
    private HomeOptions _homeOptions = new();
    private readonly Border _infoRail = new();
    private readonly Grid _homeFrame = Ui.G(null, "*,0");
    private async Task LoadHomeOptions()
    {
        _homeOptions = await Api.CallAsync<HomeOptions>("home_options") ?? new();
        _infoRail.Child = Ui.Scroll(new HomeInfoPanel(_homeOptions, () => _inst.Str()));
        _infoRail.Padding = new Thickness(18, 16, 18, 0); _infoRail.BorderThickness = new Thickness(1, 0, 0, 0);
        _infoRail.SetResourceReference(Border.BorderBrushProperty, "B.LineSoft");
        ArrangeInfoRail();
    }
    private void ArrangeInfoRail()
    {
        bool show = _homeOptions.SidebarEnabled && _homeFrame.ActualWidth >= 1140;
        _homeFrame.ColumnDefinitions[1].Width = new GridLength(show ? 268 : 0);
        _infoRail.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }
    private void OpenInfoRail()
    {
        var body = Ui.V(12, new HomeInfoPanel(_homeOptions, () => _inst.Str()), Ui.Btn("配置显示内容", BtnKind.Ghost, (_, _) => Run(ConfigureInfoRail)));
        Dlg.Panel("首页信息栏", body, 460);
    }
    private async Task ConfigureInfoRail()
    {
        var enabled = Ui.Check("宽窗口显示右侧信息栏", _homeOptions.SidebarEnabled);
        var list = Ui.V(10); var order = new List<(string Key, CheckBox Check)>();
        foreach (var entry in new[] { ("clock", "时间与日期"), ("worlds", "最近世界"), ("tasks", "下载进度"), ("playtime", "游玩时长") })
        {
            var check = Ui.Check(entry.Item2, _homeOptions.SidebarModules.Contains(entry.Item1)); order.Add((entry.Item1, check)); list.Children.Add(check);
        }
        if (!await Dlg.Ask("配置首页信息栏", Ui.V(14, enabled, Ui.Muted("窄窗口通过右上角「信息栏」查看。"), list), "保存")) return;
        await Api.CallAsync("save_home_sidebar", new { enabled = enabled.IsChecked == true, modules = order.Where(entry => entry.Check.IsChecked == true).Select(entry => entry.Key).ToList() });
        await LoadHomeOptions();
    }
}
