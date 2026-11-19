using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace HelL.Services;

public static class SideFrostEx
{
	private static Border? _side;
	private static Grid? _layer;
	private static Grid? _finish;

	public static void Install(Border side, Grid layer, Rectangle tint)
	{
		_side = side;
		_layer = layer;
		_finish = new Grid { IsHitTestVisible = false };
		var sheen = new Rectangle();
		sheen.SetResourceReference(Shape.FillProperty, "B.GlassSheen");
		_finish.Children.Add(sheen);
		_finish.Children.Add(new Rectangle { Fill = Grain(), Opacity = 0.3 });
		layer.Children.Insert(layer.Children.IndexOf(tint) + 1, _finish);
		layer.SizeChanged += (_, _) => LayoutNow();
		Update();
	}

	public static void Update()
	{
		if (_finish == null) return;
		_finish.Visibility = SideFrost.Enabled && Backdrop.EffectsEnabled && Wallpaper.Active && !Glass.ClearWallpaper ? Visibility.Visible : Visibility.Collapsed;
		LayoutNow();
	}

	public static void LayoutNow()
	{
		if (_side == null || _layer == null) return;
		double radius = _side.CornerRadius.TopLeft;
		_layer.Clip = radius <= 0 ? null : new RectangleGeometry(
			new Rect(0, 0, _layer.ActualWidth, _layer.ActualHeight), radius, radius);
	}

	private static ImageBrush Grain()
	{
		const int size = 64;
		var pixels = new byte[size * size * 4];
		uint noise = 2654435769;
		for (int i = 0; i < size * size; i++)
		{
			noise ^= noise << 13;
			noise ^= noise >> 17;
			noise ^= noise << 5;
			byte alpha = (byte)((noise >> 8) % 8);
			byte shade = (noise & 1) == 0 ? alpha : (byte)0;
			pixels[i * 4] = pixels[i * 4 + 1] = pixels[i * 4 + 2] = shade;
			pixels[i * 4 + 3] = alpha;
		}
		var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Pbgra32, null, pixels, size * 4);
		bitmap.Freeze();
		var brush = new ImageBrush(bitmap)
		{
			TileMode = TileMode.Tile,
			Stretch = Stretch.None,
			ViewportUnits = BrushMappingMode.Absolute,
			Viewport = new Rect(0, 0, size, size)
		};
		brush.Freeze();
		return brush;
	}
}
