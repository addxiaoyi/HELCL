using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace HelL.Services;

public sealed class SPanel : Panel
{
	public Orientation Orientation { get; set; } = Orientation.Vertical;

	public double Spacing { get; set; } = 8.0;

	public bool Wrap { get; set; }

	protected override Size MeasureOverride(Size available)
	{
		if (Wrap && Orientation == Orientation.Horizontal) return Flow(available.Width, false);
		double num = 0.0;
		double num2 = 0.0;
		int num3 = 0;
		Size availableSize = ((Orientation == Orientation.Vertical) ? new Size(available.Width, double.PositiveInfinity) : new Size(double.PositiveInfinity, available.Height));
		foreach (UIElement internalChild in base.InternalChildren)
		{
			internalChild.Measure(availableSize);
			if (internalChild.Visibility != Visibility.Collapsed)
			{
				num3++;
				if (Orientation == Orientation.Vertical)
				{
					num = Math.Max(num, internalChild.DesiredSize.Width);
					num2 += internalChild.DesiredSize.Height;
				}
				else
				{
					num2 = Math.Max(num2, internalChild.DesiredSize.Height);
					num += internalChild.DesiredSize.Width;
				}
			}
		}
		double num4 = (double)Math.Max(0, num3 - 1) * Spacing;
		if (Orientation == Orientation.Vertical)
		{
			num2 += num4;
		}
		else
		{
			num += num4;
		}
		return new Size(double.IsInfinity(available.Width) ? num : Math.Min(num, Math.Max(num, available.Width)), double.IsInfinity(available.Height) ? num2 : num2);
	}

	protected override Size ArrangeOverride(Size final)
	{
		if (Wrap && Orientation == Orientation.Horizontal)
		{
			Flow(final.Width, true);
			return final;
		}
		double num = 0.0;
		foreach (UIElement internalChild in base.InternalChildren)
		{
			if (internalChild.Visibility != Visibility.Collapsed)
			{
				if (Orientation == Orientation.Vertical)
				{
					internalChild.Arrange(new Rect(0.0, num, final.Width, internalChild.DesiredSize.Height));
					num += internalChild.DesiredSize.Height + Spacing;
				}
				else
				{
					internalChild.Arrange(new Rect(num, 0.0, internalChild.DesiredSize.Width, final.Height));
					num += internalChild.DesiredSize.Width + Spacing;
				}
			}
		}
		return final;
	}

	private Size Flow(double width, bool arrange)
	{
		double x = 0, y = 0, rowHeight = 0, usedWidth = 0;
		var row = new List<(UIElement Child, double X, double Width, double Height)>();
		void PlaceRow()
		{
			foreach (var item in row)
			{
				var alignment = (item.Child as FrameworkElement)?.VerticalAlignment ?? VerticalAlignment.Center;
				double offset = alignment == VerticalAlignment.Top ? 0 : alignment == VerticalAlignment.Bottom ? rowHeight - item.Height : (rowHeight - item.Height) / 2;
				item.Child.Arrange(new Rect(item.X, y + offset, item.Width, item.Height));
			}
			row.Clear();
		}
		foreach (UIElement child in InternalChildren)
		{
			if (child.Visibility == Visibility.Collapsed) continue;
			if (!arrange) child.Measure(new Size(width, double.PositiveInfinity));
			Size size = child.DesiredSize;
			if (x > 0 && x + size.Width > width + 0.5)
			{
				if (arrange) PlaceRow();
				x = 0;
				y += rowHeight + Spacing;
				rowHeight = 0;
			}
			if (arrange) row.Add((child, x, Math.Min(width, size.Width), size.Height));
			usedWidth = Math.Max(usedWidth, x + size.Width);
			x += size.Width + Spacing;
			rowHeight = Math.Max(rowHeight, size.Height);
		}
		if (arrange) PlaceRow();
		return new Size(usedWidth, y + rowHeight);
	}
}
