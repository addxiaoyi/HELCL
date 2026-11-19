using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HelL;
using HelL.Core.Launcher;
using HelL.Pages;
using HelL.Services;

internal static class CacheSettingsPreview
{
    private static int _checks;
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); _checks++; Console.WriteLine("[PASS] " + message); }
    private static async Task Ready(WallpaperCachePanel panel)
    {
        await MaterialControlsPreview.Until(() => !MaterialControlsPreview.Field<bool>(panel, "_busy") && MaterialControlsPreview.Field<TextBlock>(panel, "_usage").Text.Contains("512 MiB"), "cache inventory completes");
    }
    private static void Click(DependencyObject panel, string label) => CustomizationPreview.Named(panel, label).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static string FakeVideo(string folder, char key, int size)
    {
        string path = Path.Combine(folder, new string(key, 64) + ".mp4"); File.WriteAllBytes(path, new byte[size]); File.WriteAllText(path + ".size", size.ToString()); return path;
    }
    internal static async Task Run(MainWindow window, string folder, string seed)
    {
        Directory.CreateDirectory(folder); Motion.Enabled = false; window.Width = 800;
        await Layouts(window, folder);
        string cacheFolder = Path.Combine(Utils.ROOT, "cache", "wallpaper-video"); Directory.CreateDirectory(cacheFolder);
        string unused = FakeVideo(cacheFolder, 'a', 4096); string partial = Path.Combine(cacheFolder, new string('b', 64) + ".mp4." + Guid.NewGuid().ToString("N") + ".partial"); File.WriteAllBytes(partial, new byte[2048]);
        string unrelated = Path.Combine(cacheFolder, "personal.mp4"); File.WriteAllText(unrelated, "original must stay");
        await WallpaperDialog.Show(); await MaterialControlsPreview.Layout(window);
        var panel = CustomizationPreview.Walk(Dlg.Host!).OfType<WallpaperCachePanel>().Single(); await Ready(panel);
        var apply = CustomizationPreview.Named(Dlg.Host!, "应用壁纸"); var actionOrigin = apply.TranslatePoint(new Point(), window);
        Check(actionOrigin.Y >= 0 && actionOrigin.Y + apply.ActualHeight <= window.ActualHeight, "wallpaper apply action stays visible above the window edge");
        Check(MaterialControlsPreview.Field<TextBlock>(panel, "_usage").Text.Contains("6.0 KiB"), "cache UI reports encoded and partial bytes");
        panel.BringIntoView(); await MaterialControlsPreview.Layout(window); Program.SaveSnapshot(window, Path.Combine(folder, "cache-before.png"));
        Click(panel, "清理缓存"); await Ready(panel);
        Check(!File.Exists(unused) && !File.Exists(partial) && File.ReadAllText(unrelated) == "original must stay", "one click removes cache and retains unrelated files");
        Check(!CustomizationPreview.Named(panel, "清理缓存").IsEnabled, "empty cache disables cleanup");
        Check(MaterialControlsPreview.Field<TextBlock>(panel, "_status").Text.Contains("已释放 6.0 KiB"), "cleanup reports actual released bytes");
        Program.SaveSnapshot(window, Path.Combine(folder, "cache-empty.png")); Click(Dlg.Host!, "关闭");

        foreach (string file in Directory.GetFiles(seed).Where(file => file.EndsWith(".mp4") || file.EndsWith(".size"))) File.Copy(file, Path.Combine(cacheFolder, Path.GetFileName(file)), true);
        var papers = WallpaperEngine.Read(WallpaperEngine.DiscoverConfigs().First()); string source = papers.First(paper => paper.Kind == "video").File;
        await AppServices.Client.CallAsync("save_settings", new { data = new Dictionary<string, object> { ["ui_wallpaper_mode"] = "local", ["ui_background"] = source, ["ui_wallpaper_video_optimize"] = true } });
        await Wallpaper.ReloadAsync(); await MaterialControlsPreview.Until(() => Wallpaper.VideoReady && Wallpaper.PlaybackSource != source, "real cached video is playing", 20);
        string active = Wallpaper.PlaybackSource;
        unused = FakeVideo(cacheFolder, 'c', 4096);
        await WallpaperDialog.Show(); await MaterialControlsPreview.Layout(window); panel = CustomizationPreview.Walk(Dlg.Host!).OfType<WallpaperCachePanel>().Single(); await Ready(panel);
        var cache = new WallpaperVideoCache(cacheFolder);
        Check(cache.Inspect().InUseBytes == new FileInfo(active).Length, "active video holds a cross-process cleanup lease");
        Click(panel, "清理缓存"); await Ready(panel);
        Check(File.Exists(active) && !File.Exists(unused) && Wallpaper.PlaybackSource == active && Wallpaper.VideoReady, "cleanup preserves uninterrupted cached playback");
        Check(MaterialControlsPreview.Field<TextBlock>(panel, "_usage").Text.Contains("使用中"), "UI explains retained active bytes");
        panel.BringIntoView(); await MaterialControlsPreview.Layout(window); Program.SaveSnapshot(window, Path.Combine(folder, "cache-playing.png"));
        Wallpaper.SetSource(papers.First(paper => File.Exists(paper.Preview)).Preview);
        Click(panel, "刷新"); await Ready(panel); Check(cache.Inspect().InUseBytes == 0, "changing wallpaper releases its cache lease");
        Click(panel, "清理缓存"); await Ready(panel); Check(!File.Exists(active) && cache.Inspect().Bytes == 0, "previously active cache becomes reclaimable");
        Click(Dlg.Host!, "关闭");
        Console.WriteLine("[CACHE SETTINGS] " + _checks + " checks passed");
    }

    private static async Task Layouts(MainWindow window, string folder)
    {
        window.Navigate("settings", instant: true); await Program.WaitForPage(window, "settings");
        var settings = CustomizationPreview.Walk(window).OfType<SettingsPage>().Single();
        var rail = CustomizationPreview.Walk(settings).OfType<Rail>().Single();
        foreach (double width in new[] { 800d, 1180d })
        foreach (bool dark in new[] { false, true })
        {
            window.Width = width; window.Height = 620; App.ApplyTheme(dark); await MaterialControlsPreview.Layout(window);
            Check(rail.Orientation == (width == 800 ? Orientation.Horizontal : Orientation.Vertical), "settings categories adapt at " + width);
            foreach (string category in new[] { "look", "download", "game", "ai", "launcher" })
            {
                rail.Select(category); await MaterialControlsPreview.Layout(window);
                var scroll = CustomizationPreview.Walk(settings).OfType<SmoothScroll>().Single();
                if (category == "game" || category == "ai")
                {
                    var editor = category == "game" ? (Control)MaterialControlsPreview.Field<TextBox>(settings, "_jvm") : MaterialControlsPreview.Field<ComboBox>(settings, "_aiModel");
                    string longValue = new string('W', 170);
                    if (editor is TextBox text) text.Text = longValue; else ((ComboBox)editor).Text = longValue;
                    await MaterialControlsPreview.Layout(window); CheckHorizontal(editor, scroll);
                }
                scroll.ScrollToTop(); await MaterialControlsPreview.Layout(window);
                Check(scroll.ScrollableWidth == 0 && scroll.ViewportWidth >= (width == 800 ? 550 : 650), "fields retain usable width for " + category + " at " + width);
                foreach (var control in CustomizationPreview.Walk(scroll).OfType<Control>().Where(control => control.IsVisible && control.ActualWidth > 0 && (control is TextBox || control is ComboBox))) CheckHorizontal(control, scroll);
                if (width == 800) Program.SaveSnapshot(window, Path.Combine(folder, "settings-" + category + "-" + dark + ".png"));
                scroll.ScrollToBottom(); await MaterialControlsPreview.Layout(window);
                var save = CustomizationPreview.Named(settings, "保存设置"); var origin = save.TranslatePoint(new Point(), window);
                Check(origin.Y + save.ActualHeight <= window.ActualHeight && origin.X + save.ActualWidth <= window.ActualWidth, "save stays visible for " + category);
            }
        }
        rail.Select("look"); window.Width = 800; App.ApplyTheme(false);
    }
    private static void CheckHorizontal(FrameworkElement control, ScrollViewer scroll)
    {
        var origin = control.TranslatePoint(new Point(), scroll);
        Check(origin.X >= -1 && origin.X + control.ActualWidth <= scroll.ViewportWidth + 1, "field fits available width: " + control.GetType().Name);
    }
}
