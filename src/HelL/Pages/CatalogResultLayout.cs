using System.Windows;
using System.Windows.Controls;

namespace HelL.Pages;

internal sealed class CatalogResultLayout : Grid
{
    private readonly FrameworkElement _actions;
    private bool? _wide;

    public CatalogResultLayout(FrameworkElement icon, FrameworkElement body, FrameworkElement actions)
    {
        _actions = actions;
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition());
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        icon.Margin = new Thickness(0, 2, 14, 0); icon.VerticalAlignment = VerticalAlignment.Top;
        actions.VerticalAlignment = VerticalAlignment.Top;
        Children.Add(icon); Children.Add(body); Children.Add(actions);
        SetColumn(body, 1); SetRowSpan(icon, 2);
    }

    protected override Size MeasureOverride(Size available)
    {
        bool wide = available.Width >= 700;
        if (_wide != wide)
        {
            _wide = wide;
            SetColumn(_actions, wide ? 2 : 1); SetRow(_actions, wide ? 0 : 1);
            _actions.Margin = wide ? new Thickness(12, 0, 0, 0) : new Thickness(0, 4, 0, 0);
        }
        return base.MeasureOverride(available);
    }
}
