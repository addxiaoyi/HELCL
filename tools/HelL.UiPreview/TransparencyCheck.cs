using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using HelL;
using HelL.Core.Launcher;
using HelL.Pages;
using HelL.Services;
using Path = System.IO.Path;

internal static class TransparencyCheck
{
    private static int _checks;
    private static readonly string[] Surfaces = { "B.Glass", "B.GlassStrong", "B.NativeChrome", "B.PageWash", "B.WallpaperPageWash", "B.Card", "B.Paper", "B.Paper2", "B.ControlFill", "B.SurfaceInset", "B.Popup", "B.Mask" };
    private static void Check(bool ok, string label) { if (!ok) throw new InvalidOperationException(label); _checks++; Console.WriteLine("[PASS] " + label); }
    private static IEnumerable<T> Find<T>(DependencyObject root) => CustomizationPreview.Walk(root).OfType<T>();
    private static void Click(string label) => CustomizationPreview.Named(Dlg.Host!, label).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static Slider Slider(string label) => Find<Slider>(Dlg.Host!).Single(slider => AutomationProperties.GetName(slider) == label);
    private static double Alpha(string key) { var brush = (SolidColorBrush)Ui.Res(key); return brush.Opacity * brush.Color.A / 255d; }

    internal static async Task Run(MainWindow window, string folder)
    {
        Directory.CreateDirectory(folder); Motion.Enabled = false;
        string config = WallpaperEngine.DiscoverConfigs().First();
        string monitor = await AppServices.Client.CallAsync<string>("get_setting", new { key = "ui_wallpaper_monitor", @default = "" }) ?? "";
        var papers = WallpaperEngine.Read(config);
        string wallpaper = (papers.FirstOrDefault(paper => paper.Monitor == monitor && File.Exists(paper.Preview)) ?? papers.First(paper => File.Exists(paper.Preview))).Preview;
        await AppServices.Client.CallAsync("save_settings", new { data = new Dictionary<string, object> {
            ["ui_wallpaper_mode"] = "local", ["ui_background"] = wallpaper, ["ui_background_folder"] = "",
            ["ui_background_dim"] = 40, ["ui_background_blur"] = 12,
            ["ui_glass_transparency"] = 38, ["ui_glass_cards"] = 52, ["ui_glass_controls"] = 24, ["ui_glass_popups"] = 0, ["ui_glass_frost"] = 30 } });
        await Wallpaper.ReloadAsync();
        foreach (bool dark in new[] { false, true })
        {
            App.ApplyTheme(dark); await MaterialControlsPreview.Layout(window);
            await MaterialDialog.Show(); await MaterialControlsPreview.Layout(window);
            Click("完全透明");
            await MaterialControlsPreview.Until(() => Glass.ClearWallpaper && Glass.PopupTransparency == 100, "full preset disables all four fills and frost");
            foreach (string key in Surfaces) Check(Alpha(key) == 0, "100 percent removes the actual surface fill: " + key);
            var still = (Image)typeof(Wallpaper).GetField("_still", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            var dim = (Rectangle)typeof(Wallpaper).GetField("_dim", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            Check(still.Effect == null && dim.Fill.Opacity == 0, "full preset removes the existing wallpaper blur and dim layer");
            Check(((UIElement)window.FindName("PageHost")).Opacity == 0, "fully transparent dialog hides underlying page text without painting a background");
            Check(Find<Rectangle>(window).All(sample => sample.Effect is not BlurEffect), "full preset leaves no material blur effect");
            Check(await AppServices.Client.CallAsync<int>("get_setting", new { key = "ui_background_dim" }) == 40, "temporary full transparency does not overwrite wallpaper preferences");
            var card = Ui.Card(); card.Width = 40; card.Height = 40; card.Measure(new Size(40, 40)); card.Arrange(new Rect(0, 0, 40, 40));
            var bitmap = new RenderTargetBitmap(40, 40, 96, 96, PixelFormats.Pbgra32); bitmap.Render(card);
            var pixel = new byte[4]; bitmap.CopyPixels(new Int32Rect(20, 20, 1, 1), pixel, 4, 0);
            Check(pixel[3] == 0, "rendered card center has zero alpha, not just a transparent setting label");
            Click("取消"); await MaterialControlsPreview.Layout(window);
            Check(Glass.Transparency == 38 && Glass.CardTransparency == 52 && Glass.ControlTransparency == 24 && Glass.PopupTransparency == 0 && Glass.Frost == 30, "cancel restores all independent layers");
            Check(still.Effect is BlurEffect && dim.Fill.Opacity == .4, "cancel restores the original wallpaper blur and dim layer");
            Check(((UIElement)window.FindName("PageHost")).Opacity == 1, "closing the transparent dialog restores the page");
        }
        await MaterialDialog.Show(); await MaterialControlsPreview.Layout(window);
        Slider("窗口透明度").Value = 100; Slider("卡片透明度").Value = 77; Slider("控件透明度").Value = 43; Slider("弹窗透明度").Value = 12; Slider("壁纸磨砂强度").Value = 8;
        await Task.Delay(100); Click("保存材质");
        await MaterialControlsPreview.Until(() => Dlg.Host!.Children.Count == 0, "independent layer preferences save");
        Glass.SetMaterial(0, 0); await Wallpaper.ReloadAsync();
        Check(Glass.Transparency == 100 && Glass.CardTransparency == 77 && Glass.ControlTransparency == 43 && Glass.PopupTransparency == 12 && Glass.Frost == 8, "all five saved values survive a settings reload");
        Check(Alpha("B.PageWash") == 0 && Math.Abs(Alpha("B.Card") - .23) < .001 && Math.Abs(Alpha("B.ControlFill") - .57) < .001, "layer percentages map directly to rendered alpha without a minimum floor");
        await AppServices.Client.CallAsync("save_settings", new { data = new Dictionary<string, object> { ["ui_glass_cards"] = 200, ["ui_glass_controls"] = -5 } });
        Check(await AppServices.Client.CallAsync<int>("get_setting", new { key = "ui_glass_cards" }) == 100 && await AppServices.Client.CallAsync<int>("get_setting", new { key = "ui_glass_controls" }) == -1, "settings boundary clamps each layer and retains the inheritance sentinel");

        foreach (bool dark in new[] { false, true })
        foreach (string page in new[] { "launch", "instance", "resourcepack", "ai", "settings" })
        {
            App.ApplyTheme(dark); window.Navigate(page, instant: true); await Program.WaitForPage(window, page);
            foreach (bool full in new[] { false, true })
            {
                Glass.SetMaterial(full ? 100 : 82, full ? 0 : 18, full ? 100 : 68, full ? 100 : 42, full ? 100 : 10);
                foreach (int width in new[] { 1180, 800 })
                {
                    window.Width = width; await MaterialControlsPreview.Layout(window);
                    Check(Alpha("B.PageWash") < .19 && Alpha("B.Card") < .33, "page changes do not restore opaque material");
                    Program.SaveSnapshot(window, Path.Combine(folder, $"{page}-{dark}-{full}-{width}.png"));
                }
            }
        }
        Glass.SetMaterial(100, 0); window.Width = 1180;
        await MaterialControlsPreview.Layout(window);
        var background = Find<ComboBox>(window).First(combo => (AutomationProperties.GetLabeledBy(combo) as TextBlock)?.Text == "界面背景");
        background.IsDropDownOpen = true; await MaterialControlsPreview.Layout(window);
        Check(((UIElement)window.FindName("PageHost")).Opacity == 0, "transparent dropdown hides underlying page text");
        var popup = (Popup)background.Template.FindName("PART_Popup", background);
        var popupSurface = (Border)VisualTreeHelper.GetChild(popup.Child, 0);
        var popupInput = (SolidColorBrush)popupSurface.Background;
        Check(Math.Abs(popupInput.Opacity * popupInput.Color.A - 1) < .001, "transparent dropdown retains one alpha step for mouse input");
        Glass.SetMaterial(100, 0, 100, 100, 10); await MaterialControlsPreview.Layout(window);
        Check(((UIElement)window.FindName("PageHost")).Opacity == 1, "readable dropdown restores underlying page during live preview");
        Glass.SetMaterial(100, 0); await MaterialControlsPreview.Layout(window);
        background.IsDropDownOpen = false; await MaterialControlsPreview.Layout(window);
        Check(((UIElement)window.FindName("PageHost")).Opacity == 1, "closing a transparent dropdown restores the page");
        await MaterialDialog.Show(); await MaterialControlsPreview.Layout(window);
        Program.SaveSnapshot(window, Path.Combine(folder, "full-transparent-dialog.png"));
        Click("纯色外观"); await MaterialControlsPreview.Layout(window);
        Check(Surfaces.Where(key => key != "B.Mask").All(key => Alpha(key) == 1), "solid preset recovers readable surfaces immediately");
        Click("取消");
        var apply = typeof(Backdrop).GetMethod("Apply", BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(Window), typeof(bool) }, null)!;
        apply.Invoke(null, new object[] { window, false });
        Check(Surfaces.Where(key => key != "B.Mask").All(key => Alpha(key) == 1), "system-disabled effects retain the solid fallback");
        Backdrop.Refresh(window);
        await WallpaperDialog.Show(); await MaterialControlsPreview.Layout(window);
        var modes = Find<ComboBox>(Dlg.Host!).First(combo => combo.Items.Contains(I18n.L("直接透出桌面 / 其他窗口")));
        modes.IsDropDownOpen = true; await MaterialControlsPreview.Layout(window);
        modes.IsDropDownOpen = false; await MaterialControlsPreview.Layout(window);
        Check(((UIElement)window.FindName("PageHost")).Opacity == 0, "closing a nested selector keeps the transparent dialog readable");
        modes.SelectedIndex = 4; Click("应用材质");
        await MaterialControlsPreview.Until(() => Find<Button>(Dlg.Host!).Any(button => AutomationProperties.GetName(button) == I18n.L("重启应用外观") && button.IsVisible), "desktop mode provides an explicit restart action");
        Check(await AppServices.Client.CallAsync<string>("get_setting", new { key = "ui_wallpaper_mode" }) == "transparent", "desktop mode persists through the real wallpaper selector");
        Check(Wallpaper.CurrentSource == wallpaper, "pending desktop switch preserves the current wallpaper until restart");
        modes.SelectedIndex = 0; Click("应用壁纸");
        await MaterialControlsPreview.Until(() => !Backdrop.ClearDesktop, "switching back cancels the pending desktop mode");
        Click("关闭");
        Console.WriteLine("[TRANSPARENCY] " + _checks + " checks passed");
    }
}
