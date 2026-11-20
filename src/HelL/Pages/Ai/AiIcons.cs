using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using HelL.Design;
using HelL.Services;

namespace HelL.Pages.Ai;

internal static class AiIcons
{
	public const string Spark = "M12 3l1.8 5.2L19 10l-5.2 1.8L12 17l-1.8-5.2L5 10l5.2-1.8z";

	public static readonly string Layers = IconCatalog.GetPath("Layers");

	public static readonly string Check = IconCatalog.GetPath("Check");

	public static readonly string X = IconCatalog.GetPath("Close");

	public static readonly string Skip = IconCatalog.GetPath("ChevronRight");

	public static readonly string ChevDown = IconCatalog.GetPath("ChevronDown");

	public static readonly string ChevRight = IconCatalog.GetPath("ChevronRight");

	public static readonly string ArrowUp = IconCatalog.GetPath("ArrowUp");

	public static readonly string Undo = IconCatalog.GetPath("Undo");

	public static readonly string Retry = IconCatalog.GetPath("Refresh");

	public static readonly string Copy = IconCatalog.GetPath("Copy");

	public const string ThumbUp = "M7 10v12 M15 5.9L14 10h5.8a2 2 0 0 1 2 2.3l-1.4 8a2 2 0 0 1-2 1.7H7V10l4-8a3 3 0 0 1 4 3.9z";

	public const string ThumbDown = "M17 14V2 M9 18.1L10 14H4.2a2 2 0 0 1-2-2.3l1.4-8A2 2 0 0 1 5.6 2H17v12l-4 8a3 3 0 0 1-4-3.9z";

	public const string Shield = "M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z";

	public const string Cpu = "M7 5h10a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2z M9 9h6v6H9z M9 2v3 M15 2v3 M9 19v3 M15 19v3 M2 9h3 M2 15h3 M19 9h3 M19 15h3";

	public static readonly string Plus = IconCatalog.GetPath("Plus");

	public const string Panel = "M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z M9 3v18";

	public static readonly string More = IconCatalog.GetPath("More");

	public const string Branch = "M6 3a3 3 0 1 0 0 6a3 3 0 1 0 0-6z M6 15a3 3 0 1 0 0 6a3 3 0 1 0 0-6z M18 5a3 3 0 1 0 0 6a3 3 0 1 0 0-6z M6 9v6 M18 11a6 6 0 0 1-6 6H9";

	public static readonly string Alert = IconCatalog.GetPath("Warning");

	public static readonly string Trash = IconCatalog.GetPath("Trash");

	public static readonly string Pen = IconCatalog.GetPath("Edit");

	public static readonly string Globe = IconCatalog.GetPath("World");

	public static readonly string Eye = IconCatalog.GetPath("Eye");

	public static readonly string EyeOff = IconCatalog.GetPath("EyeOff");

	public const string Plug = "M12 22v-5 M9 8V2 M15 8V2 M18 8v5a6 6 0 0 1-12 0V8z";

	public static readonly string Box = IconCatalog.GetPath("Cube");

	public const string List = "M8 6h13 M8 12h13 M8 18h13 M3 6h.01 M3 12h.01 M3 18h.01";

	public const string Archive = "M3 3h18a1 1 0 0 1 1 1v3a1 1 0 0 1-1 1H3a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1z M4 8v11a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8 M10 12h4";

	public static readonly string Clock = IconCatalog.GetPath("Clock");

	public static readonly string Search = IconCatalog.GetPath("Search");

	public static readonly string File = IconCatalog.GetPath("File");

	public static readonly string Download = IconCatalog.GetPath("Download");

	public const string Scan = "M3 7V5a2 2 0 0 1 2-2h2 M17 3h2a2 2 0 0 1 2 2v2 M21 17v2a2 2 0 0 1-2 2h-2 M7 21H5a2 2 0 0 1-2-2v-2 M7 12h10";

	public static readonly string Settings = IconCatalog.GetPath("Settings");

	public static Path Icon(string data, double size = 16.0, string brushKey = "B.InkMuted")
	{
		return Lucide.Icon(data, size, brushKey);
	}

	public static Path Path(string data, double size, Brush brush)
	{
		double num = size / 24.0;
		return new Path
		{
			Data = Geometry.Parse(data),
			Width = 24.0,
			Height = 24.0,
			Stretch = Stretch.None,
			StrokeThickness = IconCatalog.StrokeWidth,
			StrokeStartLineCap = PenLineCap.Round,
			StrokeEndLineCap = PenLineCap.Round,
			StrokeLineJoin = PenLineJoin.Round,
			LayoutTransform = new ScaleTransform(num, num),
			IsHitTestVisible = false,
			Stroke = brush
		};
	}

	public static ControlTemplate FlatButtonTemplate()
	{
		ControlTemplate controlTemplate = new ControlTemplate(typeof(Button));
		FrameworkElementFactory frameworkElementFactory = new FrameworkElementFactory(typeof(Border), "bd");
		frameworkElementFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(7.0));
		frameworkElementFactory.SetBinding(Border.BackgroundProperty, new Binding("Background")
		{
			RelativeSource = RelativeSource.TemplatedParent
		});
		frameworkElementFactory.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush")
		{
			RelativeSource = RelativeSource.TemplatedParent
		});
		frameworkElementFactory.SetBinding(Border.BorderThicknessProperty, new Binding("BorderThickness")
		{
			RelativeSource = RelativeSource.TemplatedParent
		});
		frameworkElementFactory.SetBinding(Border.PaddingProperty, new Binding("Padding")
		{
			RelativeSource = RelativeSource.TemplatedParent
		});
		FrameworkElementFactory frameworkElementFactory2 = new FrameworkElementFactory(typeof(ContentPresenter));
		frameworkElementFactory2.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
		frameworkElementFactory2.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
		frameworkElementFactory.AppendChild(frameworkElementFactory2);
		controlTemplate.VisualTree = frameworkElementFactory;
		return controlTemplate;
	}

	public static Button Flat(string icon, string? text, string? tip, Action click, string fg = "B.InkMuted", double iconSize = 14.0, bool pill = false)
	{
		Button b = new Button
		{
			Template = FlatButtonTemplate(),
			Background = Brushes.Transparent,
			BorderThickness = new Thickness(pill ? 1 : 0),
			Padding = (string.IsNullOrEmpty(text) ? new Thickness(5.0) : new Thickness(8.0, 3.0, 9.0, 3.0)),
			Cursor = Cursors.Hand,
			Focusable = false
		};
		if (pill)
		{
			b.SetResourceReference(Control.BorderBrushProperty, "B.Line");
			b.SetResourceReference(Control.BackgroundProperty, "B.Paper");
		}
		TextBlock textBlock = (string.IsNullOrEmpty(text) ? null : Ui.Txt(text, 11.5, bold: false, fg));
		b.Content = Ui.H(5.0, Icon(icon, iconSize, fg).VCenter(), textBlock?.VCenter());
		if (tip != null)
		{
			b.ToolTip = tip;
		}
		b.MouseEnter += delegate
		{
			b.SetResourceReference(Control.BackgroundProperty, "B.Hover");
		};
		b.MouseLeave += delegate
		{
			if (pill)
			{
				b.SetResourceReference(Control.BackgroundProperty, "B.Paper");
			}
			else
			{
				b.Background = Brushes.Transparent;
			}
		};
		b.Click += delegate
		{
			click();
		};
		return b;
	}

	public static Grid Tile(FrameworkElement icon, ToolState state)
	{
		Border border = new Border
		{
			Width = 24.0,
			Height = 24.0,
			CornerRadius = new CornerRadius(7.0),
			Child = icon.Mid()
		};
		border.SetResourceReference(Border.BackgroundProperty, state switch
		{
			ToolState.Failed => "B.DangerSoft", 
			ToolState.Skipped => "B.Hover", 
			_ => "B.AccentSoft", 
		});
		Grid grid = new Grid
		{
			Width = 26.0,
			Height = 26.0,
			VerticalAlignment = VerticalAlignment.Top
		};
		grid.Children.Add(border.TopLeft());
		if (state == ToolState.Done || state == ToolState.Failed || state == ToolState.Skipped)
		{
			string data = state switch
			{
				ToolState.Done => "M20 6L9 17l-5-5", 
				ToolState.Failed => "M18 6L6 18 M6 6l12 12", 
				_ => "M9 6l6 6-6 6", 
			};
			Border border2 = new Border
			{
				Width = 12.0,
				Height = 12.0,
				CornerRadius = new CornerRadius(6.0),
				BorderThickness = new Thickness(1.5),
				HorizontalAlignment = HorizontalAlignment.Right,
				VerticalAlignment = VerticalAlignment.Bottom,
				Child = Path(data, 7.0, Brushes.White).Mid()
			};
			((Path)border2.Child).StrokeThickness = 4.0;
			border2.SetResourceReference(Border.BorderBrushProperty, "B.Paper");
			Border border3 = border2;
			border3.SetResourceReference(Border.BackgroundProperty, state switch
			{
				ToolState.Done => "B.Accent", 
				ToolState.Failed => "B.Danger", 
				_ => "B.InkFaint", 
			});
			grid.Children.Add(border2);
		}
		return grid;
	}

	public static T Mid<T>(this T el) where T : FrameworkElement
	{
		el.HorizontalAlignment = HorizontalAlignment.Center;
		el.VerticalAlignment = VerticalAlignment.Center;
		return el;
	}

	public static T TopLeft<T>(this T el) where T : FrameworkElement
	{
		el.HorizontalAlignment = HorizontalAlignment.Left;
		el.VerticalAlignment = VerticalAlignment.Top;
		return el;
	}

	public static Grid Ring(double ratio, double size = 18.0)
	{
		Grid grid = new Grid
		{
			Width = size,
			Height = size
		};
		Ellipse ellipse = new Ellipse
		{
			StrokeThickness = 2.4
		};
		ellipse.SetResourceReference(Shape.StrokeProperty, "B.Line");
		grid.Children.Add(ellipse);
		if (ratio > 0.001)
		{
			double num = size / 2.0 - 1.2;
			double num2 = size / 2.0;
			double num3 = Math.Min(ratio, 0.9999) * 2.0 * Math.PI;
			Point point = new Point(num2 + num * Math.Sin(num3), num2 - num * Math.Cos(num3));
			PathFigure pathFigure = new PathFigure
			{
				StartPoint = new Point(num2, num2 - num),
				IsClosed = false
			};
			pathFigure.Segments.Add(new ArcSegment(point, new Size(num, num), 0.0, num3 > Math.PI, SweepDirection.Clockwise, isStroked: true));
			Path path = new Path();
			path.Data = new PathGeometry(new PathFigure[1] { pathFigure });
			path.StrokeThickness = 2.4;
			path.StrokeStartLineCap = PenLineCap.Round;
			path.StrokeEndLineCap = PenLineCap.Round;
			Path path2 = path;
			path2.SetResourceReference(Shape.StrokeProperty, (ratio > 0.85) ? "B.Danger" : ((ratio > 0.65) ? "B.Warn" : "B.Accent"));
			grid.Children.Add(path2);
		}
		return grid;
	}
}
