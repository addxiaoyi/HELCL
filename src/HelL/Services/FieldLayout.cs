using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace HelL.Services;

public sealed class FieldLayout : Grid
{
    private readonly FrameworkElement _label;
    private readonly UIElement _control;
    private readonly FrameworkElement _notes;
    private readonly double _labelWidth;
    private bool? _stacked;

    public FieldLayout(FrameworkElement label, UIElement control, FrameworkElement notes, double labelWidth)
    {
        _label = label;
        _control = control;
        _notes = notes;
        _labelWidth = labelWidth;
        _label.VerticalAlignment = VerticalAlignment.Center;
        _notes.Margin = new Thickness(0, 4, 0, 0);
        for (int i = 0; i < 3; i++)
        {
            ColumnDefinitions.Add(new ColumnDefinition());
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        Children.Add(label);
        Children.Add(control);
        Children.Add(notes);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        bool stacked = _labelWidth > 0 && constraint.Width < _labelWidth + 240;
        if (_stacked != stacked)
        {
            _stacked = stacked;
            ColumnDefinitions[0].Width = stacked ? new GridLength(1, GridUnitType.Star) : new GridLength(_labelWidth);
            ColumnDefinitions[1].Width = new GridLength(stacked || _labelWidth == 0 ? 0 : 12);
            ColumnDefinitions[2].Width = stacked ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            SetColumn(_control, stacked ? 0 : 2);
            SetColumn(_notes, stacked ? 0 : 2);
            SetRow(_control, stacked ? 1 : 0);
            SetRow(_notes, stacked ? 2 : 1);
            _label.Margin = stacked ? new Thickness(0, 0, 0, 6) : new Thickness(0);
        }
        if (!stacked && _control is SPanel { Orientation: Orientation.Vertical } column)
        {
            double editorWidth = Math.Max(0, constraint.Width - _labelWidth - 12);
            column.Measure(new Size(editorWidth, double.PositiveInfinity));
            _label.Margin = new Thickness(0);
            _label.Measure(new Size(_labelWidth, double.PositiveInfinity));
            double firstHeight = column.Children.Cast<UIElement>().FirstOrDefault(child => child.Visibility != Visibility.Collapsed)?.DesiredSize.Height ?? 0;
            _label.VerticalAlignment = VerticalAlignment.Top;
            _label.Margin = new Thickness(0, Math.Max(0, (firstHeight - _label.DesiredSize.Height) / 2), 0, 0);
        }
        else _label.VerticalAlignment = stacked ? VerticalAlignment.Top : VerticalAlignment.Center;
        return base.MeasureOverride(constraint);
    }
}
