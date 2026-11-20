using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HelL;
using HelL.Design;
using HelL.Services;

internal static class WpfIconCheck
{
    internal static async Task Run(MainWindow window, string folder)
    {
        Directory.CreateDirectory(folder);
        var checks = new List<string>();
        var failures = new List<string>();
        void Check(bool passed, string label)
        {
            checks.Add(label);
            if (!passed) failures.Add(label);
            Console.WriteLine((passed ? "[PASS] " : "[FAIL] ") + label);
        }

        TextBlock icon = Ui.Glyph("\ue713", 14);
        icon.Foreground = Brushes.Red;
        BitmapSource initial = Draw(icon);
        Check(icon.Background is DrawingBrush, "common WPF glyphs render shared vector geometry");
        Check(icon.Text == "\ue713", "vector rendering preserves the legacy Text contract");
        Check(icon.DesiredSize.Width == 14 && icon.DesiredSize.Height == 14, "a small vector glyph has a square layout frame");
        Check(Pixels(initial).Count(pixel => pixel.R > pixel.B + 40 && pixel.A > 10) > 5,
            "the actual vector raster contains its foreground color");
        Save(initial, Path.Combine(folder, "settings-14.png"));

        string before = Digest(initial);
        icon.Text = "\ue896";
        BitmapSource changed = Draw(icon);
        Check(icon.Text == "\ue896" && Digest(changed) != before, "changing Text redraws the icon without replacing its control");
        icon.Foreground = Brushes.Blue;
        BitmapSource blue = Draw(icon);
        Check(Pixels(blue).Count(pixel => pixel.B > pixel.R + 40 && pixel.A > 10) > 5
            && Pixels(blue).All(pixel => pixel.A < 10 || pixel.B >= pixel.R),
            "changing the foreground recolors the vector without retaining old ink");
        icon.FontSize = 22;
        Draw(icon);
        Check(icon.DesiredSize.Width == 22 && icon.DesiredSize.Height == 22, "font-size changes resize the vector canvas");
        Check(!icon.Focusable && !icon.IsHitTestVisible && UIElementAutomationPeer.CreatePeerForElement(icon) is null,
            "decorative glyphs neither intercept input nor add duplicate accessible content");
        Button action = Ui.IconBtn("\ue713", "设置");
        Check(AutomationProperties.GetName(action) == "设置", "the parent action retains its explicit accessible name");
        Type aiIcons = typeof(Ui).Assembly.GetType("HelL.Pages.Ai.AiIcons", true)!;
        string copyPath = (string)aiIcons.GetField("Copy", BindingFlags.Static | BindingFlags.Public)!.GetValue(null)!;
        var aiCopy = (System.Windows.Shapes.Path)aiIcons.GetMethod("Path", BindingFlags.Static | BindingFlags.Public)!
            .Invoke(null, new object[] { copyPath, 14.0, Brushes.Red })!;
        System.Windows.Shapes.Path otherCopy = Lucide.Icon(IconCatalog.GetPath("Copy"), 14);
        Check(copyPath == IconCatalog.GetPath("Copy") && aiCopy.StrokeThickness == IconCatalog.StrokeWidth
            && otherCopy.StrokeThickness == IconCatalog.StrokeWidth && Pixels(Draw(aiCopy)).Any(pixel => pixel.A > 10),
            "AI action icons share the same drawing and stroke as the other clients");

        icon.Text = "?";
        BitmapSource fallback = Draw(icon);
        Check(icon.Background is null && icon.TextEffects is null && Pixels(fallback).Any(pixel => pixel.A > 10),
            "unmapped symbols retain their original text fallback");

        foreach (var state in new[] { (Outline: "\ue734", Selected: "\ue735", Name: "star"), (Outline: "\ue718", Selected: "\ue840", Name: "pin") })
        {
            TextBlock toggle = Ui.Glyph(state.Outline, 20);
            toggle.Foreground = Brushes.Black;
            BitmapSource outline = Draw(toggle);
            int outlineInk = Pixels(outline).Count(pixel => pixel.A > 128);
            toggle.Text = state.Selected;
            BitmapSource selected = Draw(toggle);
            int selectedInk = Pixels(selected).Count(pixel => pixel.A > 128);
            Save(outline, Path.Combine(folder, state.Name + "-outline-black.png"));
            Save(selected, Path.Combine(folder, state.Name + "-selected-black.png"));
            Check(Digest(selected) != Digest(outline) && selectedInk > outlineInk,
                "selected " + state.Name + " retains a filled shape in the same foreground color");
            Console.WriteLine("[ICON STATE] " + state.Name + " outline=" + outlineInk + " selected=" + selectedInk);
        }

        foreach (string name in IconCatalog.Names)
        {
            FrameworkElement sample = CatalogIcon(name, 20, Brushes.Black);
            Check(Pixels(Draw(sample)).Count(pixel => pixel.A > 10) >= 2, "the canonical icon produces visible ink: " + name);
            Geometry shape = Geometry.Parse(IconCatalog.GetPath(name));
            Rect ink = shape.GetRenderBounds(new Pen(Brushes.Black, IconCatalog.StrokeWidth)
            {
                StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round
            });
            Check(ink.Left >= 0 && ink.Top >= 0 && ink.Right <= IconCatalog.Frame && ink.Bottom <= IconCatalog.Frame,
                "the complete stroke stays inside the shared frame: " + name);
        }

        bool originalDark = App.IsDark;
        try
        {
            foreach (bool dark in new[] { false, true })
            {
                App.ApplyTheme(dark);
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                Save(Draw(Atlas()), Path.Combine(folder, "icon-atlas-" + (dark ? "dark" : "light") + ".png"));
            }
        }
        finally { App.ApplyTheme(originalDark); }

        window.Navigate("launch", true);
        await Program.WaitForPage(window, "launch");
        Check(CustomizationPreview.Walk(window).OfType<TextBlock>().Count(glyph => glyph.Background is DrawingBrush) >= 8,
            "the real navigation and controls use vector-backed glyphs");
        var maximize = (Button)window.FindName("MaxBtn");
        object original = maximize.Content;
        try
        {
            maximize.Content = "\ue922";
            await Settle(window);
            TextBlock caption = CustomizationPreview.Walk(maximize).OfType<TextBlock>().Single();
            BitmapSource maximizing = Crop(window, maximize);
            Save(maximizing, Path.Combine(folder, "caption-maximize.png"));
            string maximized = Digest(maximizing);
            maximize.Content = "\ue923";
            await Settle(window);
            BitmapSource restoring = Crop(window, maximize);
            Save(restoring, Path.Combine(folder, "caption-restore.png"));
            Check(caption.Text == "\ue923" && caption.Background is DrawingBrush && Digest(restoring) != maximized,
                "the window caption updates its vector when maximize changes to restore");
        }
        finally { maximize.Content = original; }
        window.UpdateLayout();
        foreach (string name in new[] { "MinBtn", "MaxBtn", "CloseBtn" })
        {
            var button = (Button)window.FindName(name);
            TextBlock symbol = CustomizationPreview.Walk(button).OfType<TextBlock>().Single();
            Point offset = symbol.TranslatePoint(default, button);
            Check(Math.Abs(offset.Y + symbol.ActualHeight / 2 - button.ActualHeight / 2) < 1
                && symbol.ActualWidth == 14 && symbol.ActualHeight == 14,
                "window controls keep one centered fourteen-pixel frame: " + name);
            var drawing = (GeometryDrawing)((DrawingBrush)symbol.Background).Drawing;
            Rect ink = drawing.Geometry.GetRenderBounds(drawing.Pen);
            Check(Math.Abs(ink.Top + ink.Height / 2 - IconCatalog.Frame / 2) < 0.01,
                "window control ink is vertically centered inside its frame: " + name);
        }
        Program.SaveSnapshot(window, Path.Combine(folder, "navigation-vector-icons.png"));
        Program.SaveSnapshot(window, Path.Combine(folder, "navigation-vector-icons-2x.png"), 2);
        window.Width = 900;
        await Settle(window);
        Program.SaveSnapshot(window, Path.Combine(folder, "navigation-vector-icons-900.png"));
        window.Width = 1180;
        await Settle(window);

        File.WriteAllText(Path.Combine(folder, "wpf-icon-check.json"), JsonSerializer.Serialize(new
        {
            ok = failures.Count == 0, checks, failures, icons = IconCatalog.Names.Count,
            rasterVerified = true, nativeScreenReaderVerified = false
        }));
        if (failures.Count > 0) throw new IOException("WPF vector icons: " + string.Join("; ", failures));
    }

    private static FrameworkElement Atlas()
    {
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 8, Width = 960 };
        foreach (string name in IconCatalog.Names)
        {
            var glyphs = Ui.H(12, CatalogIcon(name, 14, Ui.Res("B.Ink")), CatalogIcon(name, 20, Ui.Res("B.Ink")), CatalogIcon(name, 28, Ui.Res("B.Ink")));
            grid.Children.Add(Ui.V(8, glyphs, Ui.Txt(name, 10, fg: "B.InkMuted")).M(12));
        }
        var surface = new Border { Padding = new Thickness(12), Child = grid };
        surface.SetResourceReference(Border.BackgroundProperty, "B.Paper");
        return surface;
    }

    private static async Task Settle(MainWindow window)
    {
        window.UpdateLayout();
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
    }

    private static FrameworkElement CatalogIcon(string name, double size, Brush brush)
    {
        var canvas = new Canvas { Width = IconCatalog.Frame, Height = IconCatalog.Frame };
        canvas.Children.Add(new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(IconCatalog.GetPath(name)), Stroke = brush, StrokeThickness = IconCatalog.StrokeWidth,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round
        });
        return new Viewbox { Width = size, Height = size, Child = canvas };
    }

    private static BitmapSource Draw(FrameworkElement control)
    {
        if (VisualTreeHelper.GetParent(control) is null)
        {
            control.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            control.Arrange(new Rect(control.DesiredSize));
        }
        control.UpdateLayout();
        int width = Math.Max(1, (int)Math.Ceiling(double.IsNaN(control.Width) ? control.RenderSize.Width : control.Width));
        int height = Math.Max(1, (int)Math.Ceiling(double.IsNaN(control.Height) ? control.RenderSize.Height : control.Height));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var sample = new DrawingVisual();
        using (DrawingContext drawing = sample.RenderOpen())
            drawing.DrawRectangle(new VisualBrush(control)
            {
                ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, width, height), Stretch = Stretch.Fill
            }, null, new Rect(0, 0, width, height));
        bitmap.Render(sample);
        return bitmap;
    }

    private static BitmapSource Crop(MainWindow window, FrameworkElement target)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        Point offset = target.TranslatePoint(default, window);
        return new CroppedBitmap(bitmap, new Int32Rect((int)Math.Floor(offset.X), (int)Math.Floor(offset.Y),
            (int)Math.Ceiling(target.ActualWidth), (int)Math.Ceiling(target.ActualHeight)));
    }

    private static IEnumerable<(byte R, byte B, byte A)> Pixels(BitmapSource bitmap)
    {
        byte[] bytes = Bytes(bitmap);
        for (int offset = 0; offset < bytes.Length; offset += 4) yield return (bytes[offset + 2], bytes[offset], bytes[offset + 3]);
    }

    private static byte[] Bytes(BitmapSource bitmap)
    {
        byte[] bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(bytes, bitmap.PixelWidth * 4, 0);
        return bytes;
    }

    private static string Digest(BitmapSource bitmap)
    {
        using var hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(Bytes(bitmap)));
    }

    private static void Save(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }
}
