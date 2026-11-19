using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using HelL.Services;

namespace HelL.Pages;

public sealed partial class InstancePage
{
    private readonly ComboBox _groupFilter = Ui.Combo(width: 144);
    private readonly ToggleButton _detailsToggle = new() { Style = Ui.S("Btn.Icon"), Content = Ui.Glyph("\ue9f5", 15) };
    private bool _loadingGroups;

    private string? SelectedGroup => (_groupFilter.SelectedItem as ComboBoxItem)?.Tag as string;

    private UIElement DetailsToggle()
    {
        _detailsToggle.ToolTip = "显示或收起实例详情";
        System.Windows.Automation.AutomationProperties.SetName(_detailsToggle, "实例详情栏");
        _detailsToggle.IsChecked = Win?.Prefs.LibraryDetails != false;
        _detailsToggle.Click += (_, _) =>
        {
            if (_libraryFrame.ActualWidth < 860)
            {
                _detailsToggle.IsChecked = Win?.Prefs.LibraryDetails != false;
                if (_selected != null) ShowInstanceDetails(_selected);
                return;
            }
            if (Win != null) { Win.Prefs.LibraryDetails = _detailsToggle.IsChecked == true; Win.SaveUiPrefs(); }
            ArrangeInspector();
        };
        return _detailsToggle;
    }

    private void ArrangeInspector()
    {
        bool visible = _libraryFrame.ActualWidth >= 860 && _detailsToggle.IsChecked == true;
        bool wasVisible = _inspector.Visibility == Visibility.Visible;
        _libraryFrame.ColumnDefinitions[1].Width = new GridLength(visible ? 270 : 0);
        _inspector.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (visible && !wasVisible) RefreshInspector();
    }

    private void FillGroups()
    {
        _loadingGroups = true;
        try
        {
            string? selected = _groupFilter.Items.Count == 0 ? Win?.Prefs.LibraryGroup : SelectedGroup;
            _groupFilter.Items.Clear();
            _groupFilter.Items.Add(new ComboBoxItem { Content = I18n.L("全部分组") });
            _groupFilter.Items.Add(new ComboBoxItem { Content = I18n.L("未分组"), Tag = "" });
            foreach (string group in _rows.Select(row => row.Group).Where(group => group.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(group => group))
                _groupFilter.Items.Add(new ComboBoxItem { Content = group, Tag = group });
            _groupFilter.SelectedItem = _groupFilter.Items.Cast<ComboBoxItem>().FirstOrDefault(item => string.Equals(item.Tag as string, selected, StringComparison.OrdinalIgnoreCase))
                ?? _groupFilter.Items[0];
        }
        finally { _loadingGroups = false; }
    }

    private Task EditGroup(string instance)
    {
        var entry = _rows.First(row => row.Name == instance);
        var input = Ui.Combo(_rows.Select(row => row.Group).Where(group => group.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase), entry.Group, placeholder: "输入新分组，或选择已有分组", editable: true);
        input.Text = entry.Group;
        System.Windows.Automation.AutomationProperties.SetName(input, "实例分组名称");
        var save = Ui.Btn("保存分组", BtnKind.Primary);
        Dlg.Layer? layer = null;
        layer = Dlg.Panel("实例分组", Ui.V(12, Ui.H3(instance), input, Ui.Muted("留空可移出分组。分组随实例保留，不会移动游戏文件。").Wrap()), 470,
            footer: Ui.H(8, Ui.Btn("取消", BtnKind.Ghost, (_, _) => layer!.Dismiss()), save));
        save.Click += (_, _) => RunOnce(save, async () =>
        {
            await Api.CallAsync("set_instance_group", new { instance, group = input.Text });
            await LoadAsync(); layer.Close();
        }, "分组未能保存");
        return Task.CompletedTask;
    }
}
