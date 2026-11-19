using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace HelL.Services;

public static class SideFrost
{
	public const int DefaultRadius = 30;

	private static Border? _side;

	private static Grid? _layer;

	private static readonly Rectangle _tint = new Rectangle
	{
		IsHitTestVisible = false
	};

    private static WallpaperMaterial? _material;

	public static bool Enabled { get; private set; } = true;

	public static int Radius { get; private set; } = 30;

	public static double Inset { get; private set; }

	public static void SetInset(double inset)
	{
		Inset = Math.Max(0.0, inset);
	}

	public static void Attach(Grid wallHost, Border side, bool enabled, int radius)
	{
		_side = side;
		Enabled = enabled;
		Radius = Math.Max(8, Math.Min(60, radius));
		UIElement child = side.Child;
		side.Child = null;
		_layer = new Grid
		{
			ClipToBounds = true
		};
        _material = new WallpaperMaterial(_layer, wallHost);
		_layer.Children.Add(_tint);
		if (child != null)
		{
			_layer.Children.Add(child);
		}
		side.Child = _layer;
		App.ThemeVisualsChanged -= Refresh;
		App.ThemeVisualsChanged += Refresh;
		SideFrostEx.Install(_side, _layer, _tint);
		Refresh();
	}

	public static void SetEnabled(bool on)
	{
		Enabled = on;
		Wallpaper.OnThemeChanged();
	}

	public static void SetRadius(int radius)
	{
		Radius = Math.Max(8, Math.Min(60, radius));
		Refresh();
	}

	public static void Refresh()
	{
		if (_side == null || _layer == null)
		{
			SideFrostEx.Update();
		}
		else if (!Enabled || !Backdrop.EffectsEnabled)
		{
            _material?.Update(false);
			_tint.Fill = null;
			_side.SetResourceReference(Border.BackgroundProperty, "B.Side");
			SideFrostEx.Update();
		}
		else
		{
			_side.Background = Brushes.Transparent;
            _material?.Update(Wallpaper.Active && !Wallpaper.SharedVideoFrost, Glass.Frost);
			if (Backdrop.NativeAvailable && !Wallpaper.Active) _tint.SetResourceReference(Shape.FillProperty, "B.NativeChrome");
			else _tint.SetResourceReference(Shape.FillProperty, "B.Glass");
			SideFrostEx.Update();
		}
	}

}
