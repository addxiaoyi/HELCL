using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace HelL.Design;

/// <summary>A decorative vector whose foreground follows its owning button.</summary>
public sealed class LauncherIcon : TemplatedControl
{
    public static readonly StyledProperty<string> KindProperty =
        AvaloniaProperty.Register<LauncherIcon, string>(nameof(Kind), "CubeOutline");
    public static readonly StyledProperty<bool> ActiveProperty =
        AvaloniaProperty.Register<LauncherIcon, bool>(nameof(Active));

    private static readonly ConcurrentDictionary<string, Geometry> Shapes = new();

    static LauncherIcon() => AffectsRender<LauncherIcon>(KindProperty, ActiveProperty, ForegroundProperty);

    public LauncherIcon()
    {
        Focusable = false;
        IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
    }

    public string Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public bool Active { get => GetValue(ActiveProperty); set => SetValue(ActiveProperty, value); }

    protected override Size MeasureOverride(Size availableSize) => new(
        double.IsNaN(Width) ? IconCatalog.Frame : Width,
        double.IsNaN(Height) ? IconCatalog.Frame : Height);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        double size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0 || Foreground is null) return;
        Geometry drawing = Shapes.GetOrAdd(Kind, key => Geometry.Parse(IconCatalog.GetPath(key)));
        double scale = size / IconCatalog.Frame;
        var placement = new Matrix(scale, 0, 0, scale, (Bounds.Width - size) / 2, (Bounds.Height - size) / 2);
        var pen = new Pen(Foreground, Active ? IconCatalog.ActiveStrokeWidth : IconCatalog.StrokeWidth,
            lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        using (context.PushTransform(placement)) context.DrawGeometry(null, pen, drawing);
    }
}
