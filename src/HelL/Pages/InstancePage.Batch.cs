using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using HelL.Services;

namespace HelL.Pages;

public sealed partial class InstancePage
{
    private readonly HashSet<string> _marked = new(StringComparer.Ordinal);
    private readonly ToggleButton _batchToggle = new() { Style = Ui.S("Btn.Ghost"), Content = "多选" };
    private readonly TextBlock _batchCount = Ui.Small("");
    private readonly SPanel _batchBar = Ui.H(8);
    private Button? _batchGroup, _batchPin, _batchUnpin;
    private bool BatchMode => _batchToggle.IsChecked == true;

    private UIElement BatchToggle()
    {
        System.Windows.Automation.AutomationProperties.SetName(_batchToggle, "批量管理实例");
        _batchToggle.Click += (_, _) => { if (!BatchMode) _marked.Clear(); SyncBatch(); };
        _batchBar.Wrap = true; _batchBar.Margin = new Thickness(0, 0, 0, 12);
        _batchGroup = Ui.Btn("分组…", BtnKind.Normal, (_, _) => Run(EditSelectedGroup));
        _batchPin = Ui.Btn("固定到首页", BtnKind.Ghost, (_, _) => RunOnce(_batchPin!, () => PinSelected(true), "批量固定失败"));
        _batchUnpin = Ui.Btn("取消固定", BtnKind.Ghost, (_, _) => RunOnce(_batchUnpin!, () => PinSelected(false), "取消固定失败"));
        foreach (UIElement element in new UIElement[] { _batchCount.VCenter(),
            Ui.Btn("全选当前结果", BtnKind.Ghost, (_, _) => MarkVisible()),
            Ui.Btn("取消选择", BtnKind.Ghost, (_, _) => { _marked.Clear(); SyncBatch(); }), _batchGroup, _batchPin, _batchUnpin }) _batchBar.Children.Add(element);
        _batchBar.Visibility = Visibility.Collapsed;
        return _batchToggle;
    }

    private CheckBox SelectionBox(string name)
    {
        var box = Ui.Check(""); box.IsChecked = _marked.Contains(name); box.VerticalAlignment = VerticalAlignment.Center;
        box.Visibility = BatchMode ? Visibility.Visible : Visibility.Collapsed;
        System.Windows.Automation.AutomationProperties.SetName(box, "选择实例 " + name);
        box.Click += (_, e) => { e.Handled = true; if (box.IsChecked == true) _marked.Add(name); else _marked.Remove(name); SyncBatch(); };
        return box;
    }

    private void SyncBatch()
    {
        _marked.IntersectWith(_rows.Select(row => row.Name));
        _batchBar.Visibility = BatchMode ? Visibility.Visible : Visibility.Collapsed;
        _batchToggle.Content = BatchMode ? "完成多选" : "多选";
        int visible = _order.Count(row => _marked.Contains(row.Key));
        _batchCount.Text = "已选 " + _marked.Count + (visible != _marked.Count ? " · 当前可见 " + visible : "");
        foreach (var row in _cards.Values)
        {
            row.Selection.Visibility = BatchMode ? Visibility.Visible : Visibility.Collapsed;
            row.Selection.IsChecked = _marked.Contains(row.Key);
        }
        if (_batchGroup != null) _batchGroup.IsEnabled = _batchPin!.IsEnabled = _batchUnpin!.IsEnabled = _marked.Count > 0;
    }

    private void MarkVisible()
    {
        _batchToggle.IsChecked = true;
        foreach (var row in _order) _marked.Add(row.Key);
        SyncBatch();
    }

    private async Task PinSelected(bool pinned)
    {
        string[] names = _marked.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
        int visible = _order.Count(row => _marked.Contains(row.Key));
        string preview = string.Join("\n", names.Take(25)) + (names.Length > 25 ? "\n…共 " + names.Length + " 项" : "");
        if (!await Dlg.Confirm(pinned ? "固定这些实例？" : "取消固定这些实例？",
            "本次处理 " + names.Length + " 项，其中 " + (names.Length - visible) + " 项不在当前可见结果中。\n\n" + preview, "确认处理", "取消")) return;
        await Api.CallAsync("pin_instances", new { instances = names, pinned });
        await LoadLibraryState(); Render(); Toast("批量操作完成", (pinned ? "已固定 " : "已取消固定 ") + names.Length + " 个实例。");
    }

    private Task EditSelectedGroup() => GroupDialog(_marked.ToArray());

    private Task GroupDialog(string[] names)
    {
        if (names.Length == 0) return Task.CompletedTask;
        var input = Ui.Combo(_rows.Select(row => row.Group).Where(group => group.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase),
            placeholder: "输入新分组，或选择已有分组", editable: true);
        input.Text = ""; System.Windows.Automation.AutomationProperties.SetName(input, "批量分组名称");
        var save = Ui.Btn("保存批量分组", BtnKind.Primary); bool saving = false;
        Dlg.Layer? layer = null;
        layer = Dlg.Panel("批量分组", Ui.V(12, Ui.H3("已选 " + names.Length + " 个实例"), input,
            Ui.Muted("包含筛选后暂不可见的已选实例。留空会移出分组，游戏文件不移动。").Wrap()), 520,
            footer: Ui.H(8, Ui.Btn("取消", BtnKind.Ghost, (_, _) => layer!.Dismiss()), save), canDismiss: () => !saving);
        save.Click += (_, _) => RunOnce(save, async () =>
        {
            saving = true; input.IsEnabled = false;
            try { await Api.CallAsync("set_instance_groups", new { instances = names, group = input.Text }); await LoadAsync(); layer.Close(); }
            finally { saving = false; input.IsEnabled = true; }
        }, "批量分组失败");
        return Task.CompletedTask;
    }

    private async Task RenameSelectedGroup()
    {
        string? group = SelectedGroup;
        if (string.IsNullOrEmpty(group)) { Toast("请先选择分组", "在分组筛选中选择要重命名的分组。"); return; }
        string? name = await Dlg.Prompt("重命名分组", "新分组名称", group);
        if (string.IsNullOrWhiteSpace(name) || name == group) return;
        await Api.CallAsync("rename_instance_group", new { group, name });
        _loadingGroups = true; _groupFilter.Items.Clear(); _loadingGroups = false;
        if (Win != null) { Win.Prefs.LibraryGroup = name.Trim(); Win.SaveUiPrefs(); }
        await LoadAsync();
    }
}
