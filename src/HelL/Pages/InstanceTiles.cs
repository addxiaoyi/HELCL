using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace HelL.Pages;

internal sealed class InstanceTiles : Panel
{
    private const double Gap = 12;
    private const double MinimumTileWidth = 238;
    private readonly List<double> _rowHeights = new();
    private bool _covers = true;
    public int Columns { get; private set; } = 1;

    public bool Covers
    {
        get => _covers;
        set { if (_covers == value) return; _covers = value; InvalidateMeasure(); }
    }

    protected override Size MeasureOverride(Size available)
    {
        double width = double.IsInfinity(available.Width) ? MinimumTileWidth : Math.Max(0, available.Width);
        Columns = Covers ? Math.Max(1, (int)((width + Gap) / (MinimumTileWidth + Gap))) : 1;
        double cell = Math.Max(0, (width - Gap * (Columns - 1)) / Columns);
        _rowHeights.Clear();
        for (int index = 0; index < Children.Count; index++)
        {
            Children[index].Measure(new Size(cell, double.PositiveInfinity));
            int row = index / Columns;
            if (row == _rowHeights.Count) _rowHeights.Add(0);
            _rowHeights[row] = Math.Max(_rowHeights[row], Children[index].DesiredSize.Height);
        }
        double height = 0;
        foreach (double rowHeight in _rowHeights) height += rowHeight;
        return new Size(width, height + Math.Max(0, _rowHeights.Count - 1) * (Covers ? Gap : 2));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double cell = Math.Max(0, (finalSize.Width - Gap * (Columns - 1)) / Columns);
        double top = 0;
        for (int index = 0; index < Children.Count; index++)
        {
            int row = index / Columns, column = index % Columns;
            Children[index].Arrange(new Rect(column * (cell + Gap), top, cell, _rowHeights[row]));
            if (column == Columns - 1) top += _rowHeights[row] + (Covers ? Gap : 2);
        }
        return finalSize;
    }
}
