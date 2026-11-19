using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace HelL.Services;

public static class UiExt
{
	public static T Dyn<T>(this T el, DependencyProperty p, string key) where T : FrameworkElement
	{
		el.SetResourceReference(p, key);
		return el;
	}

	public static T M<T>(this T el, double l, double t = 0.0, double r = 0.0, double b = 0.0) where T : FrameworkElement
	{
		el.Margin = new Thickness(l, t, r, b);
		return el;
	}

	public static T M<T>(this T el, double all) where T : FrameworkElement
	{
		el.Margin = new Thickness(all);
		return el;
	}

	public static T W<T>(this T el, double w) where T : FrameworkElement
	{
		el.Width = w;
		return el;
	}

	public static T MinW<T>(this T el, double w) where T : FrameworkElement
	{
		el.MinWidth = w;
		return el;
	}

	public static T Hh<T>(this T el, double h) where T : FrameworkElement
	{
		el.Height = h;
		return el;
	}

	public static T At<T>(this T el, int row, int col = 0, int rowSpan = 1, int colSpan = 1) where T : UIElement
	{
		Grid.SetRow(el, row);
		Grid.SetColumn(el, col);
		if (rowSpan > 1)
		{
			Grid.SetRowSpan(el, rowSpan);
		}
		if (colSpan > 1)
		{
			Grid.SetColumnSpan(el, colSpan);
		}
		return el;
	}

	public static Grid Add(this Grid g, UIElement el, int row, int col = 0, int rowSpan = 1, int colSpan = 1)
	{
		el.At(row, col, rowSpan, colSpan);
		g.Children.Add(el);
		return g;
	}

	public static T Left<T>(this T el) where T : FrameworkElement
	{
		el.HorizontalAlignment = HorizontalAlignment.Left;
		return el;
	}

	public static T Right<T>(this T el) where T : FrameworkElement
	{
		el.HorizontalAlignment = HorizontalAlignment.Right;
		return el;
	}

	public static T Center<T>(this T el) where T : FrameworkElement
	{
		el.HorizontalAlignment = HorizontalAlignment.Center;
		return el;
	}

	public static T Stretch<T>(this T el) where T : FrameworkElement
	{
		el.HorizontalAlignment = HorizontalAlignment.Stretch;
		return el;
	}

	public static T VCenter<T>(this T el) where T : FrameworkElement
	{
		el.VerticalAlignment = VerticalAlignment.Center;
		return el;
	}

	public static T VTop<T>(this T el) where T : FrameworkElement
	{
		el.VerticalAlignment = VerticalAlignment.Top;
		return el;
	}

	public static T Wrap<T>(this T el) where T : TextBlock
	{
		el.TextWrapping = TextWrapping.Wrap;
		return el;
	}

	public static T Trim<T>(this T el) where T : TextBlock
	{
		el.TextTrimming = TextTrimming.CharacterEllipsis;
		return el;
	}

	public static T Tip<T>(this T el, string tip) where T : FrameworkElement
	{
		el.ToolTip = tip;
		return el;
	}

	public static T Tag2<T>(this T el, object tag) where T : FrameworkElement
	{
		el.Tag = tag;
		return el;
	}

	public static T Hide<T>(this T el, bool hidden = true) where T : UIElement
	{
		el.Visibility = (hidden ? Visibility.Collapsed : Visibility.Visible);
		return el;
	}

	public static T Show<T>(this T el, bool shown = true) where T : UIElement
	{
		el.Visibility = ((!shown) ? Visibility.Collapsed : Visibility.Visible);
		return el;
	}

	public static bool Shown(this UIElement el)
	{
		return el.Visibility == Visibility.Visible;
	}

	public static string Str(this ComboBox c)
	{
		return (c.SelectedItem as string) ?? "";
	}

	public static void Fill(this ComboBox c, IEnumerable<string> items, string? keep = null)
	{
		string text = keep ?? (c.SelectedItem as string);
		c.Items.Clear();
		foreach (string item in items)
		{
			c.Items.Add(item);
		}
		if (text != null && c.Items.Contains(text))
		{
			c.SelectedItem = text;
		}
		else if (c.Items.Count > 0)
		{
			c.SelectedIndex = 0;
		}
	}
}
