using System;
using System.Windows;
using System.Windows.Controls;

namespace HelL.Services;

public sealed class PageHeader : Grid
{
    private readonly UIElement _heading;
    private readonly Border _actions;
    public double MinimumHeadingWidth { get; set; } = 280;

    public PageHeader(UIElement heading, UIElement actions)
    {
        _heading = heading;
        _actions = new Border { Child = actions, VerticalAlignment = VerticalAlignment.Center };
        ColumnDefinitions.Add(new ColumnDefinition());
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Children.Add(heading);
        Children.Add(_actions);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        _actions.Child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double actionWidth = Math.Ceiling(_actions.Child.DesiredSize.Width) + 1;
        bool stacked = constraint.Width < actionWidth + MinimumHeadingWidth;
        _actions.Padding = stacked ? new Thickness(0, 12, 0, 0) : new Thickness(16, 0, 0, 0);
        ColumnDefinitions[1].Width = new GridLength(stacked ? 0 : actionWidth + 16);
        _actions.SetValue(HorizontalAlignmentProperty, stacked ? HorizontalAlignment.Left : HorizontalAlignment.Right);
        SetRow(_actions, stacked ? 1 : 0);
        SetColumn(_actions, stacked ? 0 : 1);
        SetColumnSpan(_actions, stacked ? 2 : 1);
        SetColumnSpan(_heading, stacked ? 2 : 1);
        return base.MeasureOverride(constraint);
    }
}
