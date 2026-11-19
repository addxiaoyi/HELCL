using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HelL.Core.Launcher;
using HelL.Models;
using HelL.Services;

namespace HelL.Pages;

public sealed partial class InstancePage
{
    private readonly HashSet<string> _pins = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _lastPlayed = new(StringComparer.Ordinal);
    private readonly ComboBox _sort = Ui.Combo(new[] { "最近游玩", "名称 A–Z", "版本数量" }, "最近游玩", 140);
    private readonly CheckBox _pinnedOnly = Ui.Check("只看固定");
    private readonly Border _inspector = new();
    private readonly Grid _libraryFrame = Ui.G(null, "*,0");

    private async Task LoadLibraryState()
    {
        var options = await Api.CallAsync<HomeOptions>("home_options") ?? new();
        _pins.Clear(); foreach (string name in options.PinnedInstances) _pins.Add(name);
        var recent = await Api.CallAsync<List<DashboardInstance>>("dashboard_instances", new { limit = Math.Max(24, _rows.Count) }) ?? new();
        _lastPlayed.Clear(); foreach (var row in recent) _lastPlayed[row.Instance] = row.LastPlayed;
    }
    private List<InstanceInfo> FilterLibrary(string query)
    {
        IEnumerable<InstanceInfo> items = _rows.Where(row => (query.Length == 0 || InstanceUtils.MatchesPinyinOrSubstr(row.Name, query)) && (_pinnedOnly.IsChecked != true || _pins.Contains(row.Name))
            && (SelectedGroup == null || string.Equals(row.Group, SelectedGroup, StringComparison.OrdinalIgnoreCase)));
        var ordered = items.OrderByDescending(row => _pins.Contains(row.Name));
        if (_sort.SelectedIndex == 0) ordered = ordered.ThenByDescending(row => _lastPlayed.TryGetValue(row.Name, out long played) ? played : 0);
        else if (_sort.SelectedIndex == 2) ordered = ordered.ThenByDescending(row => row.Versions);
        return ordered.ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
    private UIElement BuildLibrarySurface(FrameworkElement heading, FrameworkElement filters)
    {
        var root = Ui.G("Auto,Auto,Auto,*"); root.Margin = new Thickness(UiMetrics.PageInset, 20, UiMetrics.PageInset, 20);
        root.Add(heading, 0); root.Add(filters.M(0, 18, 0, 12), 1);
        var results = new Grid();
        results.Children.Add(_list); results.Children.Add(_empty);
        _libraryFrame.Add(results, 0);
        _inspector.Padding = new Thickness(20, 0, 0, 0); _inspector.Margin = new Thickness(20, 0, 0, 0); _inspector.BorderThickness = new Thickness(1, 0, 0, 0);
        _inspector.SetResourceReference(Border.BorderBrushProperty, "B.LineSoft"); _libraryFrame.Add(Ui.Scroll(_inspector), 0, 1);
        _libraryFrame.SizeChanged += (_, _) => ArrangeInspector();
        root.Add(_batchBar, 2); root.Add(_libraryFrame, 3); return root;
    }
    private async Task TogglePin(string instance)
    {
        await Api.CallAsync("pin_instance", new { instance, pinned = !_pins.Contains(instance) }); await LoadLibraryState(); Render();
    }
    private UIElement InstanceDetails(InstanceInfo instance)
    {
        var picture = new LocalPicture(double.NaN, 142, "尚未设置封面");
        Run(async () => { var visual = await Api.CallAsync<InstanceVisual>("instance_visual", new { instance = instance.Name }); if (visual?.Path.Length > 0) await picture.SetPath(visual.Path, System.IO.Path.GetDirectoryName(visual.Path)!, instance.Name); });
        string played = _lastPlayed.TryGetValue(instance.Name, out long last) && last > 0 ? DateTimeOffset.FromUnixTimeSeconds(last).LocalDateTime.ToString("yyyy-MM-dd HH:mm") : "尚未游玩";
        Button ActionRow(string label, string glyph, Func<Task> work)
        {
            var rowGrid = Ui.G(null, "18,*,Auto");
            rowGrid.Add(Ui.Glyph(glyph, 13, "B.InkMuted"), 0);
            rowGrid.Add(Ui.Txt(label, 13).VCenter().M(8, 0, 0, 0), 0, 1);
            rowGrid.Add(Ui.Glyph("\ue76c", 10, "B.InkFaint"), 0, 2);
            var btn = new Button { Style = Ui.S("Btn.Ghost"), Content = rowGrid, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(10, 7, 10, 7) };
            System.Windows.Automation.AutomationProperties.SetName(btn, label);
            btn.Click += (_, _) => Run(work);
            return btn;
        }
        var secondaryActions = Ui.G(null, "*,*");
        var pinBtn = Ui.Btn(_pins.Contains(instance.Name) ? "取消固定" : "固定到首页", BtnKind.Chip, (_, _) => Run(() => TogglePin(instance.Name)), "\ue718");
        var groupBtn = Ui.Btn(instance.Group.Length > 0 ? "分组 · " + instance.Group : "设置分组…", BtnKind.Chip, (_, _) => Run(() => EditGroup(instance.Name)), "\ue8fd");
        pinBtn.HorizontalAlignment = groupBtn.HorizontalAlignment = HorizontalAlignment.Stretch;
        secondaryActions.Add(pinBtn.M(0, 0, 4, 0), 0);
        secondaryActions.Add(groupBtn.M(4, 0, 0, 0), 0, 1);
        return Ui.V(10, picture, Ui.Txt(instance.Name, 19, true).Wrap(), Ui.Small(instance.Versions + " 个游戏版本 · " + (_pins.Contains(instance.Name) ? "已固定" : "未固定")).Wrap(),
            Ui.Small("最近游玩 · " + played).Wrap(),
            Ui.Btn("进入启动页", BtnKind.Primary, (_, _) => Run(() => LaunchWithAsync(instance.Name)), "\ue768"),
            secondaryActions,
            Ui.Sep().M(0, 4, 0, 2),
            ActionRow("模组管理", "\uea86", () => ModsDialog.ShowAsync(instance.Name)),
            ActionRow("存档与备份", "\ue74e", () => SavesDialog.ShowAsync(instance.Name, "")),
            ActionRow("封面与全部截图", "\ueb9f", () => InstanceArtworkDialog.Show(instance.Name, LoadAsync)),
            ActionRow("Java 与运行环境", "\uec32", () => PickJavaAsync(instance.Name)));
    }
    private void RefreshInspector()
    {
        if (_inspector.Visibility != Visibility.Visible) return;
        var selected = _rows.FirstOrDefault(row => row.Name == _selected);
        _inspector.Child = selected == null ? Ui.Muted("选择实例查看详情和常用操作。").Wrap() : InstanceDetails(selected);
    }
    private void ShowInstanceDetails(string name)
    {
        Select(name); var instance = _rows.FirstOrDefault(row => row.Name == name);
        if (instance != null && _inspector.Visibility != Visibility.Visible) Dlg.Panel("实例详情", InstanceDetails(instance), 520);
    }
}
