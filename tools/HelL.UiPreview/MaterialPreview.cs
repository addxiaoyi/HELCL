using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using HelL;
using HelL.Pages;
using HelL.Services;
using Path = System.IO.Path;

internal static class MaterialPreview
{
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
    private static int _checks;
    private static void Check(bool passed, string message) { if (!passed) throw new InvalidOperationException(message); _checks++; Console.WriteLine("[PASS] " + message); }
    private static async Task Layout(MainWindow window) { window.UpdateLayout(); await Task.Delay(220); window.UpdateLayout(); }
    private static void Skin(MainWindow window, string name)
    {
        typeof(SettingsPage).GetField("_visualSkin", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, name);
        Glass.Use(name); typeof(MainWindow).GetMethod("ApplySkinNow", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
    }

    internal static async Task Live(MainWindow window, string skin, bool native)
    {
        Motion.Enabled = false; window.Title = "HelL · 材质实测 · " + skin;
        window.Navigate("launch", instant: true); await Program.WaitForPage(window, "launch");
        if (native)
        {
            await AppServices.Client.CallAsync("save_settings", new { data = new System.Collections.Generic.Dictionary<string, object> { ["ui_wallpaper_mode"] = "local", ["ui_background"] = "", ["ui_background_folder"] = "" } });
            await Wallpaper.ReloadAsync();
        }
        else await Wallpaper.ReloadAsync();
        Skin(window, skin); await Layout(window);
        DwmGetWindowAttribute(new WindowInteropHelper(window).Handle, 38, out int material, sizeof(int));
        Console.WriteLine("[LIVE MATERIAL] skin=" + skin + " DWM=" + material + " wallpaper=" + Wallpaper.Active + " DPI=" + VisualTreeHelper.GetDpi(window).PixelsPerInchX);
    }

    internal static async Task Run(MainWindow window, string folder)
    {
        Directory.CreateDirectory(folder); Motion.Enabled = false;
        window.Navigate("launch", instant: true); await Program.WaitForPage(window, "launch"); await Wallpaper.ReloadAsync();
        string actualWallpaper = Wallpaper.CurrentSource;
        Glass.SetMaterial(38, 30);
        await AppServices.Client.CallAsync("save_settings", new { data = new System.Collections.Generic.Dictionary<string, object> { ["ui_wallpaper_mode"] = "local", ["ui_background"] = "", ["ui_background_folder"] = "", ["ui_glass_transparency"] = 38 } });
        await Wallpaper.ReloadAsync();
        IntPtr handle = new WindowInteropHelper(window).Handle;
        var frame = (Border)window.FindName("PageFrame"); var surface = (Grid)window.FindName("PageSurface"); var root = (Border)window.FindName("RootBorder");
        var sidebar = (Border)window.FindName("SideBar");
        foreach (string skin in new[] { "win11", "macos" })
        {
            foreach (bool dark in new[] { false, true })
            {
                App.ApplyTheme(dark); Skin(window, skin);
                foreach (bool wallpaper in new[] { false, true })
                {
                    Wallpaper.SetSource(wallpaper ? actualWallpaper : ""); await Layout(window);
                    Check(DwmGetWindowAttribute(handle, 38, out int material, sizeof(int)) == 0 && material == (skin == "win11" ? 2 : 3), skin + " uses the expected native DWM material");
                    Check(root.Clip == null && root.BorderThickness == new Thickness(0), "outer edge has no competing WPF clip or border");
                    Check(sidebar.Margin == new Thickness(0) && sidebar.CornerRadius == new CornerRadius(0), "sidebar reaches window edges without floating cutouts");
                    Check(frame.Margin == new Thickness(0) && frame.CornerRadius == new CornerRadius(0), "content panel has no inset gap or inner rounded holes");
                    var corner = frame.TranslatePoint(new Point(frame.ActualWidth, frame.ActualHeight), root);
                    Check(Math.Abs(corner.X - root.ActualWidth) < 1 && Math.Abs(corner.Y - root.ActualHeight) < 1, "page fills the client area's right and bottom edges");
                    var samples = CustomizationPreview.Walk(window).OfType<Rectangle>().Where(rectangle => rectangle.Fill is VisualBrush && rectangle.Effect is BlurEffect && rectangle.IsVisible).ToList();
                    Check(wallpaper ? samples.Count == 3 : samples.Count == 0, "wallpaper frost and native material switch cleanly");
                    foreach (var sample in samples) Check(((VisualBrush)sample.Fill).TileMode == TileMode.FlipXY && sample.Margin.Left < 0, "blur mirrors and overscans the image at outer edges");
                    if (wallpaper) Check(WallpaperContrast() >= 4.5, "wallpaper reading and navigation text retains contrast over black and white");
                    string name = skin + "-" + (dark ? "dark" : "light") + (wallpaper ? "-wallpaper" : "-native");
                    foreach (double scale in new[] { 1, 1.25, 1.5, 2 })
                    {
                        var bitmap = Render(surface, scale);
                        if (!CornersCovered(bitmap))
                        {
                            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                            using var file = File.Create(Path.Combine(folder, "corner-failure.png")); encoder.Save(file);
                            var wash = (Rectangle)window.FindName("PageWash");
                            Console.WriteLine("[CORNER FAILURE] surface=" + surface.RenderSize + " wash=" + wash.RenderSize + " at=" + wash.TranslatePoint(new Point(), surface) + " opacity=" + wash.Opacity + " fill=" + wash.Fill.Opacity);
                        }
                        Check(CornersCovered(bitmap), "content corners are painted at " + scale + " scale · " + name);
                    }
                    Program.SaveSnapshot(window, Path.Combine(folder, name + ".png"));
                }
            }
            window.WindowState = WindowState.Maximized; await Layout(window);
            Check(root.Clip == null && DwmGetWindowAttribute(handle, 33, out int preference, sizeof(int)) == 0 && preference == 1, "maximized window has square system corners · " + skin);
            window.WindowState = WindowState.Normal; await Layout(window);
            Check(DwmGetWindowAttribute(handle, 33, out preference, sizeof(int)) == 0 && preference == 2, "restored window returns to system rounded corners · " + skin);
        }
        var force = typeof(Backdrop).GetMethod("Apply", BindingFlags.Static | BindingFlags.NonPublic, null, new[] { typeof(Window), typeof(bool) }, null)!;
        force.Invoke(null, new object[] { window, false }); await Layout(window);
        Check(!Backdrop.NativeAvailable && CustomizationPreview.Walk(window).OfType<Rectangle>().All(rectangle => !rectangle.IsVisible || rectangle.Effect is not BlurEffect), "disabled system transparency removes all wallpaper blur layers");
        Check(((SolidColorBrush)((Rectangle)window.FindName("PageWash")).Fill).Opacity == 1, "disabled transparency keeps an opaque content surface");
        Program.SaveSnapshot(window, Path.Combine(folder, "solid-fallback.png"));
        Backdrop.Refresh(window); Skin(window, "win11");
        await MaterialMode(window, actualWallpaper, folder);
        Console.WriteLine("[MATERIAL] " + _checks + " checks passed; snapshots omit DWM pixels; inspect live composition separately");
    }

    private static async Task MaterialMode(MainWindow window, string wallpaper, string folder)
    {
        await AppServices.Client.CallAsync("save_settings", new { data = new System.Collections.Generic.Dictionary<string, object> { ["ui_wallpaper_mode"] = "local", ["ui_background"] = wallpaper } });
        await Wallpaper.ReloadAsync(); await WallpaperDialog.Show(); await Layout(window);
        var modes = CustomizationPreview.Walk(Dlg.Host!).OfType<ComboBox>().Single(combo => combo.Items.Contains("系统玻璃 · 不叠加壁纸"));
        modes.SelectedIndex = 3;
        CustomizationPreview.Named(Dlg.Host!, "应用材质").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var deadline = DateTime.UtcNow.AddSeconds(10); while (Wallpaper.Active && DateTime.UtcNow < deadline) await Task.Delay(100);
        Check(!Wallpaper.Active && Backdrop.NativeAvailable, "system material mode releases the wallpaper and exposes DWM");
        Check(await AppServices.Client.CallAsync<string>("get_setting", new { key = "ui_background" }) == wallpaper, "system material preserves the previous wallpaper choice");
        Check(await AppServices.Client.CallAsync<string>("get_setting", new { key = "ui_wallpaper_mode" }) == "material", "system material mode persists through the settings API");
        await Layout(window); Program.SaveSnapshot(window, Path.Combine(folder, "material-selector.png"));
        modes.SelectedIndex = 0; CustomizationPreview.Named(Dlg.Host!, "应用壁纸").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        deadline = DateTime.UtcNow.AddSeconds(10); while (!Wallpaper.Active && DateTime.UtcNow < deadline) await Task.Delay(100);
        Check(Wallpaper.CurrentSource == wallpaper, "switching back restores the same wallpaper without reselecting it");
        CustomizationPreview.Named(Dlg.Host!, "关闭").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static RenderTargetBitmap Render(FrameworkElement element, double scale)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth * scale), (int)Math.Ceiling(element.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen()) drawing.DrawRectangle(new VisualBrush(element) { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, element.ActualWidth, element.ActualHeight) }, null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        bitmap.Render(visual); return bitmap;
    }
    internal static double WallpaperContrast()
    {
        double minimum = double.MaxValue;
        foreach (var pair in from fill in new[] { "B.WallpaperPageWash", "B.Glass", "B.GlassStrong" }
                             from ink in new[] { "B.Ink", "B.InkSoft", "B.InkMuted", "B.InkFaint", "B.AccentText" }
                             select (fill, ink))
        {
            var fill = (SolidColorBrush)Ui.Res(pair.Item1); var ink = ((SolidColorBrush)Ui.Res(pair.Item2)).Color;
            foreach (byte backdrop in new byte[] { 0, 255 })
            {
                double alpha = fill.Opacity * fill.Color.A / 255;
                var paper = Color.FromRgb((byte)Math.Round(fill.Color.R * alpha + backdrop * (1 - alpha)), (byte)Math.Round(fill.Color.G * alpha + backdrop * (1 - alpha)), (byte)Math.Round(fill.Color.B * alpha + backdrop * (1 - alpha)));
                double a = Luminance(ink), b = Luminance(paper);
                minimum = Math.Min(minimum, (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05));
            }
        }
        Console.WriteLine("[WALLPAPER CONTRAST] " + minimum.ToString("0.00") + ":1"); return minimum;
    }
    private static double Luminance(Color color)
    {
        double Channel(byte value) { double part = value / 255d; return part <= .04045 ? part / 12.92 : Math.Pow((part + .055) / 1.055, 2.4); }
        return .2126 * Channel(color.R) + .7152 * Channel(color.G) + .0722 * Channel(color.B);
    }
    private static bool CornersCovered(BitmapSource bitmap)
    {
        byte[] pixel = new byte[4];
        int inset = (int)Math.Ceiling(2 * bitmap.DpiX / 96);
        // Subvisual rendering retains ancestor DPI guidelines. Sample fully covered pixel centers, not the fractional outer raster boundary.
        foreach (var point in new[] { new Point(inset, inset), new Point(bitmap.PixelWidth - 1 - inset, inset), new Point(inset, bitmap.PixelHeight - 1 - inset), new Point(bitmap.PixelWidth - 1 - inset, bitmap.PixelHeight - 1 - inset) })
        {
            bitmap.CopyPixels(new Int32Rect((int)point.X, (int)point.Y, 1, 1), pixel, 4, 0);
            if (pixel[3] < 60) return false;
        }
        return true;
    }
}
