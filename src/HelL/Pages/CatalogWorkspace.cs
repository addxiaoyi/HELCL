using System.Windows;
using System.Windows.Controls;
using HelL.Services;

namespace HelL.Pages;

internal sealed class CatalogWorkspace : Grid
{
    private readonly Expander _filters;
    private readonly FrameworkElement _common;
    public CatalogWorkspace(FrameworkElement common, UIElement destination, UIElement results, UIElement targetLabel, UIElement? resultTools = null)
    {
        _common = common;
        _filters = new Expander { Header = targetLabel, Style = Ui.S("Expander.Inline"), Content = Ui.Scroll(destination), VerticalAlignment = VerticalAlignment.Top,
            Padding = new Thickness(6, 5, 8, 5), Margin = new Thickness(-6, 2, 0, 8) };
        System.Windows.Automation.AutomationProperties.SetName(_filters, I18n.L("安装位置"));
        _filters.Loaded += (_, _) =>
        {
            _filters.ApplyTemplate();
            if (_filters.Template.FindName("HeaderSite", _filters) is System.Windows.Controls.Primitives.ToggleButton toggle)
                System.Windows.Automation.AutomationProperties.SetName(toggle, I18n.L("安装位置"));
        };
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition());
        var targetRow = HelL.Services.Ui.G(null, "*,Auto"); targetRow.Add(_filters, 0);
        if (resultTools != null) targetRow.Add(resultTools, 0, 1);
        Children.Add(common); Children.Add(targetRow); Children.Add(results);
        SetRow(targetRow, 1); SetRow(results, 2);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        _common.Measure(new Size(constraint.Width, double.PositiveInfinity));
        if (!double.IsInfinity(constraint.Height))
            _filters.MaxHeight = System.Math.Max(40, System.Math.Min(124, constraint.Height - _common.DesiredSize.Height - 150));
        return base.MeasureOverride(constraint);
    }
}
