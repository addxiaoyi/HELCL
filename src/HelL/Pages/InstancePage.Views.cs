using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using HelL.Services;

namespace HelL.Pages;

public sealed partial class InstancePage
{
    private bool _coverView = true;
    private readonly ToggleButton _coversButton = new() { Style = Ui.S("TabItemBtn"), Content = I18n.L("封面") };
    private readonly ToggleButton _listButton = new() { Style = Ui.S("TabItemBtn"), Content = I18n.L("列表") };

    private UIElement ViewSwitch()
    {
        System.Windows.Automation.AutomationProperties.SetName(_coversButton, I18n.L("封面视图"));
        System.Windows.Automation.AutomationProperties.SetName(_listButton, I18n.L("紧凑列表"));
        _coversButton.ToolTip = I18n.L("查看实例封面");
        _listButton.ToolTip = I18n.L("以紧凑列表管理实例");
        _coversButton.Click += (_, _) => SetLibraryView(true);
        _listButton.Click += (_, _) => SetLibraryView(false);
        _coverView = Win?.Prefs.LibraryView != "list";
        _list.Covers = _coverView;
        _coversButton.Padding = _listButton.Padding = new Thickness(11, 5, 11, 6);
        _coversButton.IsChecked = _coverView; _listButton.IsChecked = !_coverView;
        var pill = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = Ui.H(2, _coversButton, _listButton)
        };
        pill.SetResourceReference(Border.BackgroundProperty, "B.SurfaceInset");
        pill.SetResourceReference(Border.BorderBrushProperty, "B.SurfaceEdge");
        return pill;
    }

    private void SetLibraryView(bool covers)
    {
        _coversButton.IsChecked = covers; _listButton.IsChecked = !covers;
        if (_coverView == covers) return;
        _coverView = covers; _list.Covers = covers;
        // The selected instance and sorting belong to the library, not to either presentation.
        Render();
        if (_selected != null) _list.ScrollToIndex(_filtered.FindIndex(row => row.Name == _selected));
        if (Win != null)
        {
            Win.Prefs.LibraryView = covers ? "covers" : "list";
            Win.SaveUiPrefs();
        }
    }
}
