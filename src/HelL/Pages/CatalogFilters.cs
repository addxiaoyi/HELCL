using System;
using System.Windows;
using System.Windows.Controls;

namespace HelL.Pages;

internal sealed class CatalogFilters : WrapPanel
{
    public int MaximumColumns { get; set; } = 3;
    public double MinimumFieldWidth { get; set; } = 145;

    protected override Size MeasureOverride(Size available)
    {
        double width = double.IsInfinity(available.Width) ? 220 : available.Width;
        int columns = Math.Max(1, Math.Min(MaximumColumns, (int)(width / MinimumFieldWidth)));
        ItemWidth = Math.Max(0, Math.Floor(width / columns));
        return base.MeasureOverride(available);
    }
}
