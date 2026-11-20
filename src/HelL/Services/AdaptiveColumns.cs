using System;
using System.Windows;
using System.Windows.Controls;

namespace HelL.Services;

/// <summary>Keeps paired content beside each other only while both columns fit.</summary>
public sealed class AdaptiveColumns : Grid
{
    private readonly double _fixedWidth;
    private readonly double _breakpoint;
    private readonly bool _fixedFirst;
    private readonly double _gap;
    private readonly double _fixedRatio;
    private bool? _stacked;
    private bool? _firstCollapsed;
    private bool? _secondCollapsed;

    public AdaptiveColumns(UIElement first, UIElement second, double fixedWidth, double breakpoint, bool fixedFirst = false, double gap = 16, double fixedRatio = 0)
    {
        _fixedWidth = fixedWidth;
        _breakpoint = breakpoint;
        _fixedFirst = fixedFirst;
        _gap = gap;
        if (fixedRatio < 0 || fixedRatio >= 1 || double.IsNaN(fixedRatio)) throw new ArgumentOutOfRangeException(nameof(fixedRatio));
        _fixedRatio = fixedRatio;
        Children.Add(first);
        Children.Add(second);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        bool stacked = constraint.Width < _breakpoint;
        bool firstCollapsed = Children[0].Visibility == Visibility.Collapsed;
        bool secondCollapsed = Children[1].Visibility == Visibility.Collapsed;
        if (_stacked != stacked || _firstCollapsed != firstCollapsed || _secondCollapsed != secondCollapsed)
        {
            _stacked = stacked;
            _firstCollapsed = firstCollapsed;
            _secondCollapsed = secondCollapsed;
            ColumnDefinitions.Clear();
            RowDefinitions.Clear();
            if (firstCollapsed || secondCollapsed)
            {
                SetColumn(Children[1], 0);
                SetRow(Children[1], 0);
            }
            else if (stacked)
            {
                RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                RowDefinitions.Add(new RowDefinition { Height = new GridLength(_gap) });
                RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                SetColumn(Children[1], 0);
                SetRow(Children[1], 2);
            }
            else
            {
                var fixedColumn = new GridLength(_fixedWidth);
                var flexibleColumn = new GridLength(1, GridUnitType.Star);
                ColumnDefinitions.Add(new ColumnDefinition { Width = _fixedFirst ? fixedColumn : flexibleColumn });
                ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_gap) });
                ColumnDefinitions.Add(new ColumnDefinition { Width = _fixedFirst ? flexibleColumn : fixedColumn });
                SetColumn(Children[1], 2);
                SetRow(Children[1], 0);
            }
        }
        if (!stacked && !firstCollapsed && !secondCollapsed && _fixedRatio > 0 && !double.IsInfinity(constraint.Width))
            ColumnDefinitions[_fixedFirst ? 0 : 2].Width = new GridLength(Math.Min(_fixedWidth, constraint.Width * _fixedRatio));
        return base.MeasureOverride(constraint);
    }
}
