using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace HelL.Services;

// Samples only the launcher's wallpaper. DWM supplies the desktop material when no wallpaper is active.
internal sealed class WallpaperMaterial
{
    private readonly Grid _surface;
    private readonly Grid _source;
    private readonly Rectangle _sample = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly VisualBrush _brush;
    private readonly BlurEffect _blur = new() { KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance };
    private double _radius = 28;

    public WallpaperMaterial(Grid surface, Grid source)
    {
        _surface = surface; _source = source;
        _brush = new VisualBrush(source) { ViewboxUnits = BrushMappingMode.Absolute, ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.Fill, TileMode = TileMode.FlipXY };
        _sample.Fill = _brush; _sample.Effect = _blur;
        surface.Children.Insert(0, _sample); surface.ClipToBounds = true;
        surface.SizeChanged += (_, _) => ArrangeSample();
        surface.Loaded += (_, _) => ArrangeSample();
        source.SizeChanged += (_, _) => ArrangeSample();
    }

    public void Update(bool enabled, double radius = 28)
    {
        _radius = Math.Max(0, Math.Min(60, radius)); _blur.Radius = _radius;
        _sample.Effect = _radius > 0 ? _blur : null;
        _sample.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        if (enabled) ArrangeSample();
    }

    private void ArrangeSample()
    {
        if (!_sample.IsVisible || _surface.ActualWidth <= 0 || _surface.ActualHeight <= 0 || _source.ActualWidth <= 0 || _source.ActualHeight <= 0) return;
        Point origin;
        try { origin = _surface.TranslatePoint(new Point(), _source); }
        catch (InvalidOperationException) { return; } // A hidden page can be detached during navigation.
        double padding = Math.Max(1, _radius * 2);
        _sample.Margin = new Thickness(-padding);
        _brush.Viewbox = new Rect(0, 0, _source.ActualWidth, _source.ActualHeight);
        // Mirror beyond the wallpaper edges, so blur never samples a transparent strip at the window corners.
        _brush.Viewport = new Rect(padding - origin.X, padding - origin.Y, _source.ActualWidth, _source.ActualHeight);
    }
}
