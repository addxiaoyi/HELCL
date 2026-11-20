using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using HelL.Design;

namespace HelL.Services;

public sealed class VectorGlyph : TextBlock
{
    private static readonly Dictionary<string, Geometry> Shapes = new Dictionary<string, Geometry>();
    private static readonly TextEffectCollection HiddenText = CreateHiddenText();
    private bool updating;
    private bool vector;
    private double vectorSize;

    static VectorGlyph()
    {
        TextProperty.OverrideMetadata(typeof(VectorGlyph), new FrameworkPropertyMetadata(IconChanged));
        FontSizeProperty.OverrideMetadata(typeof(VectorGlyph), new FrameworkPropertyMetadata(IconChanged));
        ForegroundProperty.OverrideMetadata(typeof(VectorGlyph), new FrameworkPropertyMetadata(IconChanged));
    }

    public VectorGlyph()
    {
        IsHitTestVisible = false;
        Focusable = false;
    }

    private static void IconChanged(DependencyObject owner, DependencyPropertyChangedEventArgs change) => ((VectorGlyph)owner).PaintIcon();

    protected override AutomationPeer OnCreateAutomationPeer() => null;

    private void PaintIcon()
    {
        if (updating) return;
        updating = true;
        try
        {
            if (!IconCatalog.TryGetGlyph(Text, out string path))
            {
                if (!vector) return;
                Background = null;
                TextEffects = null;
                if (Width == vectorSize) ClearValue(WidthProperty);
                if (Height == vectorSize) ClearValue(HeightProperty);
                vector = false;
                return;
            }

            if (double.IsNaN(Width) || vector && Width == vectorSize) Width = FontSize;
            if (double.IsNaN(Height) || vector && Height == vectorSize) Height = FontSize;
            vectorSize = FontSize;
            vector = true;
            TextEffects = HiddenText;
            var pen = new Pen(Foreground, IconCatalog.StrokeWidth)
            {
                StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round
            };
            // TextBlock's sealed renderer keeps Text binding compatibility; the actual ink uses the shared frame.
            Background = new DrawingBrush(new GeometryDrawing(null, pen, Shape(path)))
            {
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(0, 0, IconCatalog.Frame, IconCatalog.Frame),
                ViewportUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0, FontSize, FontSize),
                Stretch = Stretch.Uniform,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center
            };
        }
        finally { updating = false; }
    }

    private static Geometry Shape(string path)
    {
        lock (Shapes)
        {
            if (Shapes.TryGetValue(path, out Geometry shape)) return shape;
            Geometry parsed = Geometry.Parse(path);
            parsed.Freeze();
            Shapes.Add(path, parsed);
            return parsed;
        }
    }

    private static TextEffectCollection CreateHiddenText()
    {
        var effects = new TextEffectCollection { new TextEffect(null, Brushes.Transparent, null, 0, int.MaxValue) };
        effects.Freeze();
        return effects;
    }
}
