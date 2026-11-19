using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using HelL.Core.Launcher;
using HelL.Models;
using HelL.Services;

namespace HelL.Pages;

public sealed class HomeInfoPanel : StackPanel
{
    private readonly HomeOptions _options;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMinutes(1) };
    private readonly TextBlock _time = Ui.Txt("", 28, true);
    private readonly TextBlock _date = Ui.Muted("");
    private readonly SPanel _worlds = Ui.V(8);
    private readonly TextBlock _total = Ui.Muted("");
    private readonly Func<string> _instance;
    private int _generation;
    public HomeInfoPanel(HomeOptions options, Func<string> instance)
    {
        _options = options; _instance = instance;
        foreach (string key in options.SidebarModules)
        {
            UIElement? body = key switch {
                "clock" => Ui.V(4, _time, _date),
                "worlds" => Ui.V(10, Ui.H3("最近世界"), _worlds),
                "tasks" => new DashboardLiveModule("tasks", instance) { Height = 190 },
                "playtime" => Ui.V(8, Ui.H3("游玩时长"), _total), _ => null
            };
            if (body != null) Children.Add(new Border { Child = body, Margin = new Thickness(0, 0, 0, 24) });
        }
        IsVisibleChanged += (_, _) => { if (IsVisible) { RefreshClock(); _clock.Start(); PageBase.Run(Refresh); } else { _clock.Stop(); _generation++; } };
        Unloaded += (_, _) => { _clock.Stop(); _generation++; };
        _clock.Tick += (_, _) => RefreshClock();
    }
    private void RefreshClock() { var now = DateTime.Now; _time.Text = now.ToString("HH:mm"); _date.Text = now.ToString("M 月 d 日 · dddd"); }
    public async Task Refresh()
    {
        int generation = ++_generation;
        if (_options.SidebarModules.Contains("worlds"))
        {
            _worlds.Children.Clear(); _worlds.Children.Add(Ui.Muted("正在读取存档…"));
            try
            {
                var rows = await AppServices.Client.CallAsync<List<RecentWorld>>("recent_worlds", new { limit = 4 }) ?? new();
                if (generation != _generation) return;
                _worlds.Children.Clear();
                foreach (var world in rows)
                {
                    var icon = new LocalPicture(40, 40, "世界"); _ = icon.SetPath(world.Icon, world.Path, world.Name);
                    var open = Ui.Btn("", BtnKind.Ghost, (_, _) => PageBase.Run(() => SavesDialog.ShowAsync(world.Instance, world.Version)));
                    var info = Ui.V(2, Ui.Txt(world.Name, 13, true).Trim(), Ui.Small(world.Instance).Trim(), Ui.Small((world.Estimated ? "文件更新 " : "最近游玩 ") + DateTimeOffset.FromUnixTimeSeconds(world.LastPlayed).LocalDateTime.ToString("MM-dd HH:mm")));
                    var row = Ui.G(null, "Auto,*"); row.Add(icon.M(0, 0, 10, 0), 0); row.Add(info, 0, 1);
                    open.Content = row; open.HorizontalContentAlignment = HorizontalAlignment.Stretch; open.Padding = new Thickness(0, 6, 0, 6);
                    System.Windows.Automation.AutomationProperties.SetName(open, "管理世界 " + world.Name); _worlds.Children.Add(open);
                }
                if (rows.Count == 0) _worlds.Children.Add(Ui.Muted("创建世界后，这里会显示最近的存档和缩略图。").Wrap());
            }
            catch (Exception ex) { if (generation == _generation) { _worlds.Children.Clear(); _worlds.Children.Add(Ui.V(8, Ui.Muted("存档读取失败：" + ex.Message).Wrap(), Ui.Btn("重试", BtnKind.Ghost, (_, _) => PageBase.Run(Refresh)))); } Smoke.Note("home-worlds", ex.Message); }
        }
        if (_options.SidebarModules.Contains("playtime"))
        {
            try
            {
                var total = await AppServices.Client.CallAsync<long>("get_total_playtime");
                if (generation == _generation) _total.Text = Playtime.FormatDuration(total);
            }
            catch (Exception ex)
            {
                if (generation == _generation) _total.Text = "游玩时长暂不可用。重新打开信息栏可重试。";
                Smoke.Note("home-playtime", ex.ToString());
            }
        }
    }
}
