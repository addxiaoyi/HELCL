using System;
using System.Windows;
using System.Windows.Controls;
using HelL.Services;

namespace HelL.Pages;

internal sealed class SettingsWorkspace : Grid
{
    private readonly Rail _rail;
    private readonly UIElement _body;
    private readonly UIElement _footer;
    private bool? _compact;

    public SettingsWorkspace(Rail rail, UIElement body, UIElement footer)
    {
        _rail = rail; _body = body; _footer = footer;
        Children.Add(rail); Children.Add(body); Children.Add(footer);
    }

    protected override Size MeasureOverride(Size available)
    {
        bool compact = available.Width < 720;
        if (_compact != compact)
        {
            _compact = compact; ColumnDefinitions.Clear(); RowDefinitions.Clear();
            ColumnDefinitions.Add(new ColumnDefinition { Width = compact ? new GridLength(1, GridUnitType.Star) : new GridLength(180) });
            if (!compact) ColumnDefinitions.Add(new ColumnDefinition());
            if (compact) RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition()); RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _rail.Orientation = compact ? Orientation.Horizontal : Orientation.Vertical;
            _rail.Margin = compact ? new Thickness(UiMetrics.PageInset, 10, UiMetrics.PageInset, 4) : new Thickness(0, 12, 0, 0);
            _rail.Restyle();
            _rail.VerticalAlignment = VerticalAlignment.Top;
            SetRow(_body, compact ? 1 : 0); SetColumn(_body, compact ? 0 : 1);
            SetRow(_footer, compact ? 2 : 1); SetColumnSpan(_footer, compact ? 1 : 2);
        }
        return base.MeasureOverride(available);
    }
}
