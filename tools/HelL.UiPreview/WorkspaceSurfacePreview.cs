using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using HelL;
using HelL.Core.Launcher.Plugins;
using HelL.Pages;
using HelL.Pages.Ai;
using HelL.Services;

internal static class WorkspaceSurfacePreview
{
    private static int _checks;
    private static void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); _checks++; Console.WriteLine("[PASS] " + message); }
    private static void Fits(FrameworkElement element, Window window)
    {
        Point origin = element.TranslatePoint(new Point(), window);
        Check(element.IsVisible && origin.X >= 0 && origin.Y >= 0 && origin.X + element.ActualWidth <= window.ActualWidth + 1 && origin.Y + element.ActualHeight <= window.ActualHeight + 1, "visible control stays within " + window.ActualWidth + " DIP window: " + element.GetType().Name);
    }
    internal static async Task Run(MainWindow window, string folder)
    {
        Directory.CreateDirectory(folder); Motion.Enabled = false;
        foreach (bool dark in new[] { false, true })
        foreach (double width in new[] { 800d, 1180d })
        {
            App.ApplyTheme(dark); window.Width = width; window.Height = 700;
            window.Navigate("ai", instant: true); await Program.WaitForPage(window, "ai");
            var ai = CustomizationPreview.Walk(window).OfType<AiPage>().Single();
            var composer = MaterialControlsPreview.Field<Border>(ai, "_composer");
            Check(ReferenceEquals(composer.Background, Ui.Res("B.Popup")), "AI writing surface shares the opaque popup material"); Fits(composer, window);
            Program.SaveSnapshot(window, Path.Combine(folder, "ai-" + dark + "-" + width + ".png"));
            CustomizationPreview.Named(ai, "拓展").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await MaterialControlsPreview.Layout(window);
            var extensions = CustomizationPreview.Walk(ai).OfType<ExtensionsPanel>().Single(); await extensions.EnsureLoadedAsync();
            Fits(CustomizationPreview.Named(extensions, "打开 AI 玩家"), window);
            Program.SaveSnapshot(window, Path.Combine(folder, "extensions-" + dark + "-" + width + ".png"));
            CustomizationPreview.Named(extensions, "返回对话").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await MaterialControlsPreview.Layout(window);
            window.Navigate("plugins", instant: true); await Program.WaitForPage(window, "plugins");
            var plugins = CustomizationPreview.Walk(window).OfType<PluginsPage>().Single();
            var tabs = CustomizationPreview.Walk(plugins).OfType<RadioButton>().ToList();
            Check(tabs.Count == 3 && tabs.Single(tab => tab.IsChecked == true).Content.ToString() == "发现", "market exposes a selected catalog filter");
            tabs.Single(tab => tab.Content.ToString() == "已安装").IsChecked = true; await MaterialControlsPreview.Layout(window);
            Check(tabs.Count(tab => tab.IsChecked == true) == 1, "market filter is exclusive and accessible");
            Check(CustomizationPreview.Walk(plugins).OfType<TextBlock>().Any(text => text.Text.Contains("还没有安装匹配")), "installed filter has a readable empty state");
            tabs.Single(tab => tab.Content.ToString() == "发现").IsChecked = true; await MaterialControlsPreview.Layout(window);
            Fits(CustomizationPreview.Walk(plugins).OfType<TextBox>().Single(), window);
            foreach (var button in CustomizationPreview.Walk(plugins).OfType<Button>().Where(button => button.IsVisible && button.ActualHeight > 0)) Fits(button, window);
            Program.SaveSnapshot(window, Path.Combine(folder, "plugins-" + dark + "-" + width + ".png"));
        }
        var listing = (await PluginCatalog.Fetch(PluginCatalog.DefaultUrl)).First(plugin => plugin.Manifest.Screenshots.Count > 0);
        foreach (bool dark in new[] { false, true })
        {
            App.ApplyTheme(dark); window.Width = 800;
            var detail = new PluginDetailsPanel(listing, () => Task.CompletedTask); var layer = Dlg.Panel(listing.Manifest.Name, detail, 700);
            await MaterialControlsPreview.Until(() => CustomizationPreview.Walk(detail).OfType<LocalPicture>().Any(picture => picture.Ready), "actual plugin screenshot loads", 30);
            await MaterialControlsPreview.Layout(window);
            Check(CustomizationPreview.Walk(detail).OfType<Border>().Any(border => ReferenceEquals(border.Background, Ui.Res("B.SurfaceInset"))), "plugin install area shares the inset material");
            Program.SaveSnapshot(window, Path.Combine(folder, "plugin-detail-" + dark + ".png")); layer.Dismiss();
        }
        Console.WriteLine("[WORKSPACE MATERIAL] " + _checks + " checks passed");
    }
}
