using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using HelL.Services;

namespace HelL.Pages.Ai;

internal static class AiIcons
{
	public const string Spark = "M12 3l1.8 5.2L19 10l-5.2 1.8L12 17l-1.8-5.2L5 10l5.2-1.8z";

	public const string Layers = "M12 2l10 6-10 6L2 8z M2 14l10 6 10-6";

	public const string Check = "M20 6L9 17l-5-5";

	public const string X = "M18 6L6 18 M6 6l12 12";

	public const string Skip = "M9 6l6 6-6 6";

	public const string ChevDown = "M6 9l6 6 6-6";

	public const string ChevRight = "M9 6l6 6-6 6";

	public const string ArrowUp = "M12 19V5 M5 12l7-7 7 7";

	public const string Undo = "M3 7v6h6 M21 17a9 9 0 0 0-15-6.7L3 13";

	public const string Retry = "M21 12a9 9 0 1 1-3-6.7L21 8 M21 3v5h-5";

	public const string Copy = "M11 9h9a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2h-9a2 2 0 0 1-2-2v-9a2 2 0 0 1 2-2z M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1";

	public const string ThumbUp = "M7 10v12 M15 5.9L14 10h5.8a2 2 0 0 1 2 2.3l-1.4 8a2 2 0 0 1-2 1.7H7V10l4-8a3 3 0 0 1 4 3.9z";

	public const string ThumbDown = "M17 14V2 M9 18.1L10 14H4.2a2 2 0 0 1-2-2.3l1.4-8A2 2 0 0 1 5.6 2H17v12l-4 8a3 3 0 0 1-4-3.9z";

	public const string Shield = "M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z";

	public const string Cpu = "M7 5h10a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2z M9 9h6v6H9z M9 2v3 M15 2v3 M9 19v3 M15 19v3 M2 9h3 M2 15h3 M19 9h3 M19 15h3";

	public const string Plus = "M12 5v14 M5 12h14";

	public const string Panel = "M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z M9 3v18";

	public const string More = "M12 11a1 1 0 1 0 0 2a1 1 0 1 0 0-2z M19 11a1 1 0 1 0 0 2a1 1 0 1 0 0-2z M5 11a1 1 0 1 0 0 2a1 1 0 1 0 0-2z";

	public const string Branch = "M6 3a3 3 0 1 0 0 6a3 3 0 1 0 0-6z M6 15a3 3 0 1 0 0 6a3 3 0 1 0 0-6z M18 5a3 3 0 1 0 0 6a3 3 0 1 0 0-6z M6 9v6 M18 11a6 6 0 0 1-6 6H9";

	public const string Alert = "M10.3 3.9L1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0z M12 9v4 M12 17h.01";

	public const string Trash = "M3 6h18 M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2 M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6";

	public const string Pen = "M12 20h9 M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4z";

	public const string Globe = "M12 3a9 9 0 1 0 0 18a9 9 0 1 0 0-18z M3 12h18 M12 3a14 14 0 0 1 0 18 M12 3a14 14 0 0 0 0 18";

	public const string Eye = "M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7S2 12 2 12z M12 9a3 3 0 1 0 0 6a3 3 0 1 0 0-6z";

	public const string EyeOff = "M9.88 9.88a3 3 0 1 0 4.24 4.24 M10.73 5.08A10.43 10.43 0 0 1 12 5c7 0 10 7 10 7a13.16 13.16 0 0 1-1.67 2.68 M6.61 6.61A13.53 13.53 0 0 0 2 12s3 7 10 7a9.74 9.74 0 0 0 5.39-1.61 M2 2l20 20";

	public const string Plug = "M12 22v-5 M9 8V2 M15 8V2 M18 8v5a6 6 0 0 1-12 0V8z";

	public const string Box = "M21 8l-9-5-9 5v8l9 5 9-5z M3 8l9 5 9-5 M12 13v8";

	public const string List = "M8 6h13 M8 12h13 M8 18h13 M3 6h.01 M3 12h.01 M3 18h.01";

	public const string Archive = "M3 3h18a1 1 0 0 1 1 1v3a1 1 0 0 1-1 1H3a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1z M4 8v11a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8 M10 12h4";

	public const string Clock = "M12 3a9 9 0 1 0 0 18a9 9 0 1 0 0-18z M12 7v5l3 2";

	public const string Search = "M21 21l-4.34-4.34 M11 3a8 8 0 1 0 0 16a8 8 0 1 0 0-16z";

	public const string File = "M6 22a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h8l6 6v12a2 2 0 0 1-2 2z M14 2v6h6 M8 13h8 M8 17h5";

	public const string Download = "M12 3v12 M6 11l6 6 6-6 M4 21h16";

	public const string Scan = "M3 7V5a2 2 0 0 1 2-2h2 M17 3h2a2 2 0 0 1 2 2v2 M21 17v2a2 2 0 0 1-2 2h-2 M7 21H5a2 2 0 0 1-2-2v-2 M7 12h10";

	public const string Settings = "M12 9a3 3 0 1 0 0 6a3 3 0 1 0 0-6z M12 2v3 M12 19v3 M2 12h3 M19 12h3 M4.9 4.9l2.1 2.1 M17 17l2.1 2.1 M4.9 19.1L7 17 M17 7l2.1-2.1";

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
			StrokeThickness = 2.0,
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
