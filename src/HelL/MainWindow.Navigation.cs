using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using HelL.Services;

namespace HelL;

public partial class MainWindow
{
    private ContextMenu? _navigationMenu;

    private void ToggleNavigation()
    {
        _prefs.SideCollapsed = !_prefs.SideCollapsed;
        SideSkin.SetCollapsed(_prefs.SideCollapsed);
        BuildNav();
        SaveUiPrefs();
        NavToggle.Focus();
    }

    private void SyncNavigationToggle()
    {
        bool compact = _prefs.SideCollapsed;
        string label = I18n.L(compact ? "展开导航" : "收起导航");
        if (compact)
        {
            NavToggle.Padding = new Thickness(0);
            NavToggle.Content = Ui.Glyph("\ue700", 16);
            NavToggle.HorizontalContentAlignment = HorizontalAlignment.Center;
        }
        else
        {
            NavToggle.Padding = new Thickness(14, 0, 12, 0);
            Grid row = Ui.G(null, "18,*");
            row.Add(Ui.Glyph("\ue700", 14), 0);
            row.Add(Ui.Txt(label, 12.5, false, "B.InkMuted").VCenter().M(10, 0, 0, 0), 0, 1);
            NavToggle.Content = row;
            NavToggle.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        }
        NavToggle.ToolTip = label;
        System.Windows.Automation.AutomationProperties.SetName(NavToggle, label);
    }

    private UIElement BuildCompactGroup(string section, string label, List<string> members)
    {
        ToggleButton button = MakeRow(section, label, null, draggable: true);
        button.Click += (_, _) =>
        {
            SyncNavSelection();
            if (_navigationMenu != null) _navigationMenu.IsOpen = false;
            var menu = _navigationMenu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Right };
            foreach (string key in members)
            {
                string? page = PageOf(key);
                var item = new MenuItem
                {
                    Header = NavModel.NavLabel(key),
                    Icon = Ui.Glyph(NavGlyphs.GetValueOrDefault(key, "\ue80a")),
                    IsCheckable = true,
                    IsChecked = page == _currentId
                };
                item.Click += (_, _) => { if (page != null) Navigate(page); };
                menu.Items.Add(item);
            }
            menu.Closed += (_, _) => { SyncNavSelection(); button.Focus(); };
            menu.IsOpen = true;
        };
        return button;
    }
}
