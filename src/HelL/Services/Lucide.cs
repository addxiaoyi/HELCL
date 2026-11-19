using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using HelL.Pages.Ai;

namespace HelL.Services;

public static class Lucide
{
	public const string Brain = "M12 18V5 M15 13a4.17 4.17 0 0 1-3-4 4.17 4.17 0 0 1-3 4 M17.598 6.5A3 3 0 1 0 12 5a3 3 0 1 0-5.598 1.5 M17.997 5.125a4 4 0 0 1 2.526 5.77 M18 18a4 4 0 0 0 2-7.464 M19.967 17.483A4 4 0 1 1 12 18a4 4 0 1 1-7.967-.517 M6 18a4 4 0 0 1-2-7.464 M6.003 5.125a4 4 0 0 0-2.526 5.77";

	public const string ChevronRight = "M9 18l6-6-6-6";

	public const string LoaderCircle = "M21 12a9 9 0 1 1-6.219-8.56";

	private const string Circle12 = "M2 12a10 10 0 1 0 20 0a10 10 0 1 0-20 0";

	public const string CircleCheck = "M2 12a10 10 0 1 0 20 0a10 10 0 1 0-20 0 M9 12l2 2 4-4";

	public const string CircleSlash2 = "M2 12a10 10 0 1 0 20 0a10 10 0 1 0-20 0 M22 2L2 22";

	public const string CircleX = "M2 12a10 10 0 1 0 20 0a10 10 0 1 0-20 0 M15 9l-6 6 M9 9l6 6";

	public const string Search = "M21 21l-4.34-4.34 M3 11a8 8 0 1 0 16 0a8 8 0 1 0-16 0";

	public const string FileText = "M6 22a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h8a2.4 2.4 0 0 1 1.704.706l3.588 3.588A2.4 2.4 0 0 1 20 8v12a2 2 0 0 1-2 2z M14 2v5a1 1 0 0 0 1 1h5 M10 9H8 M16 13H8 M16 17H8";

	public const string FilePenLine = "M14.364 13.634a2 2 0 0 0-.506.854l-.837 2.87a.5.5 0 0 0 .62.62l2.87-.837a2 2 0 0 0 .854-.506l4.013-4.009a1 1 0 0 0-3.004-3.004z M14.487 7.858A1 1 0 0 1 14 7V2 M20 19.645V20a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h8a2.4 2.4 0 0 1 1.704.706l2.516 2.516 M8 18h1";

	public const string ShieldAlert = "M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z M12 8v4 M12 16h.01";

	public const string Eye = "M2.062 12.348a1 1 0 0 1 0-.696 10.75 10.75 0 0 1 19.876 0 1 1 0 0 1 0 .696 10.75 10.75 0 0 1-19.876 0 M9 12a3 3 0 1 0 6 0a3 3 0 1 0-6 0";

	public const string Sparkles = "M11.017 2.814a1 1 0 0 1 1.966 0l1.051 5.558a2 2 0 0 0 1.594 1.594l5.558 1.051a1 1 0 0 1 0 1.966l-5.558 1.051a2 2 0 0 0-1.594 1.594l-1.051 5.558a1 1 0 0 1-1.966 0l-1.051-5.558a2 2 0 0 0-1.594-1.594l-5.558-1.051a1 1 0 0 1 0-1.966l5.558-1.051a2 2 0 0 0 1.594-1.594z M20 2v4 M22 4h-4 M2 20a2 2 0 1 0 4 0a2 2 0 1 0-4 0";

	public const string SlidersHorizontal = "M10 5H3 M12 19H3 M14 3v4 M16 17v4 M21 12h-9 M21 19h-5 M21 5h-7 M8 10v4 M8 12H3";

	public const string Ban = "M2 12a10 10 0 1 0 20 0a10 10 0 1 0-20 0 M4.929 4.929L19.07 19.071";

	public const string Trash2 = "M10 11v6 M14 11v6 M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6 M3 6h18 M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2";

	private static readonly ConditionalWeakTable<TextBlock, TranslateTransform> _shimmerSlide = new ConditionalWeakTable<TextBlock, TranslateTransform>();

	public static string? ToolIcon(string? tool)
	{
		switch (tool)
		{
		case "search_modpacks":
		case "search_versions":
		case "search_content":
		case "search_worlds":
		case "search_mods":
			return "M21 21l-4.34-4.34 M3 11a8 8 0 1 0 16 0a8 8 0 1 0-16 0";
		case "read_mod_config":
		case "get_latest_log":
		case "read_artifact":
		case "get_crash_report":
		case "list_mod_configs":
			return "M6 22a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h8a2.4 2.4 0 0 1 1.704.706l3.588 3.588A2.4 2.4 0 0 1 20 8v12a2 2 0 0 1-2 2z M14 2v5a1 1 0 0 0 1 1h5 M10 9H8 M16 13H8 M16 17H8";
		case "write_mod_config":
			return "M14.364 13.634a2 2 0 0 0-.506.854l-.837 2.87a.5.5 0 0 0 .62.62l2.87-.837a2 2 0 0 0 .854-.506l4.013-4.009a1 1 0 0 0-3.004-3.004z M14.487 7.858A1 1 0 0 1 14 7V2 M20 19.645V20a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h8a2.4 2.4 0 0 1 1.704.706l2.516 2.516 M8 18h1";
		default:
			if (AiUiText.IsMcp(tool))
			{
				return "M12 22v-5 M9 8V2 M15 8V2 M18 8v5a6 6 0 0 1-12 0V8z";
			}
			return null;
		}
	}

	public static System.Windows.Shapes.Path Icon(string data, double size = 16.0, string brushKey = "B.InkMuted")
	{
		double num = size / 24.0;
		System.Windows.Shapes.Path path = new System.Windows.Shapes.Path();
		path.Data = Geometry.Parse(data);
		path.Width = 24.0;
		path.Height = 24.0;
		path.Stretch = Stretch.None;
		path.StrokeThickness = 2.0;
		path.StrokeStartLineCap = PenLineCap.Round;
		path.StrokeEndLineCap = PenLineCap.Round;
		path.StrokeLineJoin = PenLineJoin.Round;
		path.LayoutTransform = new ScaleTransform(num, num);
		path.RenderTransformOrigin = new Point(0.5, 0.5);
		path.SnapsToDevicePixels = false;
		path.IsHitTestVisible = false;
		path.SetResourceReference(Shape.StrokeProperty, brushKey);
		return path;
	}

	public static System.Windows.Shapes.Path Spinner(double size = 16.0, string brushKey = "B.AccentDeep")
	{
		System.Windows.Shapes.Path path = Icon("M21 12a9 9 0 1 1-6.219-8.56", size, brushKey);
		Transform transform = (path.RenderTransform = new RotateTransform(0.0));
		RotateTransform rot = (RotateTransform)transform;
		path.RenderTransformOrigin = new Point(0.5, 0.5);
		Motion.Spin(path, rot);
		return path;
	}

	public static FrameworkElement Asset(string tool, double size = 16.0, string brushKey = "B.AccentDeep")
	{
		if (!LucideAssets.Icons.TryGetValue(tool ?? "", out byte[] value))
		{
			return Spinner(size, brushKey);
		}
		BitmapImage bitmapImage = new BitmapImage();
		using (MemoryStream streamSource = new MemoryStream(value))
		{
			bitmapImage.BeginInit();
			bitmapImage.StreamSource = streamSource;
			bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
			bitmapImage.EndInit();
		}
		bitmapImage.Freeze();
		Rectangle rectangle = new Rectangle();
		rectangle.Width = size;
		rectangle.Height = size;
		rectangle.IsHitTestVisible = false;
		rectangle.SetResourceReference(Shape.FillProperty, brushKey);
		rectangle.OpacityMask = new ImageBrush
		{
			ImageSource = bitmapImage,
			Stretch = Stretch.Uniform
		};
		return rectangle;
	}

	public static void Rotate(System.Windows.Shapes.Path chevron, bool open)
	{
		RotateTransform rotateTransform = chevron.RenderTransform as RotateTransform;
		if (rotateTransform == null)
		{
			Transform transform = (chevron.RenderTransform = new RotateTransform(0.0));
			rotateTransform = (RotateTransform)transform;
		}
		int num = (open ? 90 : 0);
		if (!Motion.Enabled)
		{
			rotateTransform.BeginAnimation(RotateTransform.AngleProperty, null);
			rotateTransform.Angle = num;
		}
		else
		{
			rotateTransform.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(num, TimeSpan.FromMilliseconds(160.0))
			{
				EasingFunction = new CubicEase
				{
					EasingMode = EasingMode.EaseOut
				}
			});
		}
	}

	public static void Shimmer(TextBlock tb, bool on, string restoreKey)
	{
		if (_shimmerSlide.TryGetValue(tb, out TranslateTransform value))
		{
			value.BeginAnimation(TranslateTransform.XProperty, null);
			_shimmerSlide.Remove(tb);
		}
		if (!on || !Motion.Enabled)
		{
			tb.SetResourceReference(TextBlock.ForegroundProperty, restoreKey);
			return;
		}
		Color strong = (tb.Foreground as SolidColorBrush)?.Color ?? (tb.TryFindResource(restoreKey) as SolidColorBrush)?.Color ?? Colors.Gray;
		Color color = Color.FromArgb(56, strong.R, strong.G, strong.B);
		TranslateTransform slide = new TranslateTransform(-2.0, 0.0);
		LinearGradientBrush brush = new LinearGradientBrush
		{
			StartPoint = new Point(0.0, 0.0),
			EndPoint = new Point(1.0, 0.0),
			MappingMode = BrushMappingMode.RelativeToBoundingBox,
			RelativeTransform = new TransformGroup
			{
				Children = 
				{
					(Transform)new ScaleTransform(3.0, 1.0),
					(Transform)slide
				}
			}
		};
		brush.GradientStops.Add(new GradientStop(strong, 0.0));
		brush.GradientStops.Add(new GradientStop(strong, 0.34));
		brush.GradientStops.Add(new GradientStop(color, 0.5));
		brush.GradientStops.Add(new GradientStop(strong, 0.66));
		brush.GradientStops.Add(new GradientStop(strong, 1.0));
		tb.Foreground = brush;
		_shimmerSlide.Add(tb, slide);
		App.ThemeVisualsChanged += Recolor;
		Motion.Loop(tb, delegate
		{
			if (tb.Foreground == brush)
			{
				DoubleAnimationUsingKeyFrames doubleAnimationUsingKeyFrames = new DoubleAnimationUsingKeyFrames
				{
					RepeatBehavior = RepeatBehavior.Forever
				};
				doubleAnimationUsingKeyFrames.KeyFrames.Add(new LinearDoubleKeyFrame(-2.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
				doubleAnimationUsingKeyFrames.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.0))));
				doubleAnimationUsingKeyFrames.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4.0))));
				slide.BeginAnimation(TranslateTransform.XProperty, doubleAnimationUsingKeyFrames);
			}
		}, delegate
		{
			slide.BeginAnimation(TranslateTransform.XProperty, null);
		});
		void Recolor()
		{
			if (tb.Foreground != brush)
			{
				App.ThemeVisualsChanged -= Recolor;
			}
			else
			{
				Color color2 = (tb.TryFindResource(restoreKey) as SolidColorBrush)?.Color ?? strong;
				Color color3 = Color.FromArgb(56, color2.R, color2.G, color2.B);
				GradientStop gradientStop = brush.GradientStops[0];
				Color color4 = (brush.GradientStops[1].Color = color2);
				Color color6 = color4;
				gradientStop.Color = color6;
				GradientStop gradientStop2 = brush.GradientStops[3];
				color4 = (brush.GradientStops[4].Color = color2);
				color6 = color4;
				gradientStop2.Color = color6;
				brush.GradientStops[2].Color = color3;
			}
		}
	}
}
