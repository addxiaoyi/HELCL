using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using HelL;
using HelL.Core.Launcher;
using HelL.Models;
using HelL.Pages;
using HelL.Services;

internal static class MaterialControlsPreview
{
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
    private static int _checks;
    internal static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++; Console.WriteLine("[PASS] " + message);
    }
    internal static async Task Until(Func<bool> ready, string message, int seconds = 12)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!ready() && DateTime.UtcNow < deadline) await Task.Delay(80);
        Check(ready(), message);
    }
    internal static async Task Layout(MainWindow window) { window.UpdateLayout(); await Task.Delay(180); window.UpdateLayout(); }
    internal static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    internal static void Skin(MainWindow window, string skin)
    {
        typeof(SettingsPage).GetField("_visualSkin", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, skin);
        Glass.Use(skin); typeof(MainWindow).GetMethod("ApplySkinNow", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
    }
    private static DependencyObject Top => Dlg.Host!.Children[Dlg.Host.Children.Count - 1];
    private static Slider Slider(string name) => CustomizationPreview.Walk(Top).OfType<Slider>().Single(slider => AutomationProperties.GetName(slider) == name);
    private static void Click(string name) => CustomizationPreview.Named(Top, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static Task Save(int transparency, int frost) => AppServices.Client.CallAsync("save_settings", new { data = new Dictionary<string, object> { ["ui_glass_transparency"] = transparency, ["ui_glass_frost"] = frost } });
    private static IntPtr Pack(Point point) => new(unchecked((int)(((uint)(ushort)(int)point.Y << 16) | (ushort)(int)point.X)));

    internal static async Task Run(MainWindow window, string folder)
    {
        Directory.CreateDirectory(folder); Motion.Enabled = false; window.Activate();
        string config = WallpaperEngine.DiscoverConfigs().FirstOrDefault() ?? throw new IOException("A real wallpaper is required for this visual acceptance check.");
        string wallpaper = WallpaperEngine.Read(config).First(paper => File.Exists(paper.Preview)).Preview;
        await AppServices.Client.CallAsync("save_settings", new { data = new Dictionary<string, object> { ["ui_wallpaper_mode"] = "local", ["ui_background"] = wallpaper, ["ui_background_folder"] = "" } });
        await Save(38, 30); await Wallpaper.ReloadAsync();
        Check(MaterialPreview.WallpaperContrast() >= 4.5, "loading wallpaper refreshes readable text before any theme change");
        window.Navigate("settings", instant: true); await Program.WaitForPage(window, "settings");
        foreach (bool dark in new[] { false, true })
        {
            App.ApplyTheme(dark); Skin(window, "macos"); await Layout(window);
            CustomizationPreview.Named(window, "实时调整玻璃…").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Layout(window);
            Click("清透玻璃");
            await Until(() => Glass.Transparency == 82 && Glass.CardTransparency == 68 && Glass.ControlTransparency == 42 && Glass.PopupTransparency == 10 && Glass.Frost == 18, "clear preset updates each layer without saving");
            Click("柔和磨砂");
            await Until(() => Glass.Transparency == 38 && Glass.Frost == 30, "soft preset updates both sliders without saving");
            Click("纯色外观");
            await Until(() => Glass.Transparency == 0 && Glass.Frost == 0, "solid preset disables transparency and blur");
            Slider("窗口透明度").Value = 0; Slider("壁纸磨砂强度").Value = 0;
            await Until(() => Glass.Transparency == 0 && Glass.Frost == 0, "sliders apply without saving · " + dark);
            Check(((SolidColorBrush)Ui.Res("B.WallpaperPageWash")).Opacity == 1, "zero transparency is opaque");
            Check(CustomizationPreview.Walk(window).OfType<System.Windows.Shapes.Rectangle>().All(rectangle => rectangle.Effect is not BlurEffect), "zero frost removes all wallpaper material effects");
            Check(await AppServices.Client.CallAsync<int>("get_setting", new { key = "ui_glass_transparency" }) == 38, "preview does not persist before save");
            Slider("窗口透明度").Value = 63; Slider("壁纸磨砂强度").Value = 46;
            await Until(() => Glass.Frost == 46, "frost slider updates the live material");
            Check(CustomizationPreview.Walk(window).OfType<System.Windows.Shapes.Rectangle>().Any(rectangle => rectangle.Effect is BlurEffect effect && effect.Radius == 46), "rendered frost uses the requested radius");
            Program.SaveSnapshot(window, Path.Combine(folder, dark ? "preview-dark.png" : "preview-light.png"));
            Click("取消"); await Layout(window);
            Check(Glass.Transparency == 38 && Glass.Frost == 30, "cancel restores both material values");
            foreach (int amount in new[] { 0, 38, 100 })
            {
                Glass.SetMaterial(amount, 30);
                if (amount <= 38) Check(MaterialPreview.WallpaperContrast() >= 4.5, "reading preset retains contrast at transparency " + amount);
                if (amount == 100)
                {
                    var card = Ui.Card();
                    card.ApplyTemplate();
                    double canvasTransmission = 1 - ((SolidColorBrush)Ui.Res("B.WallpaperPageWash")).Opacity;
                    double cardTransmission = 1 - ((SolidColorBrush)card.Background).Opacity;
                    Check(canvasTransmission * cardTransmission == 1, "maximum transparency transmits the entire wallpaper through a real card");
                }
            }
            Glass.SetMaterial(38, 30);
            Program.SaveSnapshot(window, Path.Combine(folder, dark ? "settings-dark.png" : "settings-light.png"));
        }

        await MaterialDialog.Show(); await Layout(window);
        Slider("窗口透明度").Value = 42; Slider("壁纸磨砂强度").Value = 17; Click("保存材质");
        await Until(() => Dlg.Host!.Children.Count == 0, "save completes and closes preview");
        Glass.SetMaterial(100, 30); await Wallpaper.ReloadAsync();
        Check(Glass.Transparency == 42 && Glass.Frost == 17, "saved settings survive material reload");
        await MaterialDialog.Show(); await Layout(window);
        Check(Slider("窗口透明度").Value == 42 && Slider("壁纸磨砂强度").Value == 17, "reopened preview restores saved slider positions");
        Click("恢复默认"); await Until(() => Glass.Transparency == 38 && Glass.Frost == 30, "defaults preview immediately");
        ((UIElement)Top).RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        await Layout(window); Check(Glass.Transparency == 42 && Glass.Frost == 17, "Escape rolls back unsaved defaults");

        await WallpaperDialog.Show(); await Layout(window); var underlying = (UIElement)Top;
        Click("实时调整玻璃…"); await Layout(window);
        Check(underlying.Opacity == 0, "nested preview hides the previous dialog and its dimming mask");
        Click("取消"); await Layout(window); Check(underlying.Opacity == 1, "cancel restores the wallpaper editor without losing it"); Click("关闭");
        bool writing = true, dismissed = false;
        var guarded = Dlg.Panel("保存保护", Ui.Txt("检查"), onClose: () => dismissed = true, canDismiss: () => !writing);
        guarded.Dismiss(); Check(!dismissed, "pending settings write cannot be dismissed");
        writing = false; guarded.Dismiss(); Check(dismissed, "dialog can be dismissed again after write completes");

        Wallpaper.SetSource(""); await MaterialDialog.Show(); await Layout(window);
        Check(!Slider("壁纸磨砂强度").IsEnabled && Slider("壁纸磨砂强度").Opacity < .5, "system-managed blur is visibly disabled"); Click("取消");
        await Save(-5, 90); await Wallpaper.ReloadAsync(); Check(Glass.Transparency == 0 && Glass.Frost == 60, "settings API clamps material bounds");
        await Save(100, 30); await Wallpaper.ReloadAsync();
        await Caption(window, folder);
        await Details(window, folder);
        Console.WriteLine("[MATERIAL CONTROLS] " + _checks + " checks passed");
    }

    private static async Task Caption(MainWindow window, string folder)
    {
        Skin(window, "macos"); await Layout(window);
        var dpi = VisualTreeHelper.GetDpi(window);
        Console.WriteLine("[CAPTION DPI] " + dpi.PixelsPerInchX);
        foreach (string name in new[] { "MinBtn", "MaxBtn", "CloseBtn" })
        {
            var button = (Button)window.FindName(name);
            Check(button.ActualWidth >= 34 && button.ActualHeight >= 32, name + " has a full desktop hit target");
            double pixels = button.PointToScreen(new Point(button.ActualWidth, 0)).X - button.PointToScreen(new Point()).X;
            Check(Math.Abs(pixels - 34 * dpi.DpiScaleX) < 1, name + " scales its physical hit target with the display DPI");
            var hit = button.InputHitTest(new Point(2, 2)) as DependencyObject;
            while (hit != null && hit != button) hit = VisualTreeHelper.GetParent(hit);
            Check(hit == button, name + " receives clicks outside the visible dot");
        }
        var maximize = (Button)window.FindName("MaxBtn"); var handle = new WindowInteropHelper(window).Handle;
        Check(((Button)window.FindName("ThemeBtn")).Style == Ui.S("Btn.Caption"), "theme action is distinct from window controls");
        var green = ((SolidColorBrush)maximize.Background).Color;
        foreach (var state in new[] { WindowState.Maximized, WindowState.Normal })
        {
            var position = Pack(maximize.PointToScreen(new Point(2, 2)));
            Check(SendMessage(handle, 0x0084, IntPtr.Zero, position).ToInt32() == 9, "Snap recognizes the edge of the macOS-style maximize target");
            SendMessage(handle, 0x00A0, new IntPtr(9), position);
            Check(SnapCaption.GetHovered(maximize) && ((SolidColorBrush)maximize.Background).Color == green, "native hover preserves the green dot");
            SendMessage(handle, 0x00A1, new IntPtr(9), position); SendMessage(handle, 0x00A2, new IntPtr(9), position); await Layout(window);
            Check(window.WindowState == state, "edge click changes window state to " + state);
            Check(maximize.ToolTip?.ToString() == (state == WindowState.Normal ? "最大化" : "还原"), "maximize tooltip matches the current action");
        }
        Program.SaveSnapshot(window, Path.Combine(folder, "caption-macos.png"));
        Skin(window, "win11"); await Layout(window);
        Check(maximize.ActualHeight == 48, "switching skin restores Windows title bar dimensions");
    }

    private static async Task Details(MainWindow window, string folder)
    {
        var project = await AppServices.Client.CallAsync<CatalogItem>("catalog_details", new { source = "modrinth", slug = "iris" }) ?? throw new IOException("Missing real project details.");
        Check(project.Gallery.Count > 0 && project.Author.Length > 0, "resource preview uses upstream artwork and author");
        foreach (bool dark in new[] { false, true })
        {
            App.ApplyTheme(dark);
            var panel = new ProjectDetailsPanel(project, "1.21.1"); var layer = Dlg.Panel(project.Name, panel, 760);
            await panel.EnsureLoadedAsync(); await Layout(window);
            var dependencies = Field<SPanel>(panel, "_dependencies");
            Check(CustomizationPreview.Walk(dependencies).OfType<TextBlock>().Any(text => text.Text.StartsWith("适配文件：")), "real compatibility query succeeds · " + dark);
            Check(CustomizationPreview.Walk(panel).OfType<Border>().Any(border => ReferenceEquals(border.Background, Ui.Res("B.SurfaceInset"))), "compatibility controls share the inset material · " + dark);
            Program.SaveSnapshot(window, Path.Combine(folder, dark ? "resource-dark.png" : "resource-light.png")); layer.Dismiss(); await Layout(window);
        }
    }
}
