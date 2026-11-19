using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HelL;
using HelL.Pages;
using HelL.Services;

internal static class AppearanceAudit
{
    internal static async Task Run(MainWindow window, string folder, string[]? pages = null)
    {
        Directory.CreateDirectory(folder); Motion.Enabled = false;
        window.Title = "HelL · 当前外观验收";
        await Wallpaper.ReloadAsync();
        Console.WriteLine($"[APPEARANCE] font={Typography.Family}; dpi={VisualTreeHelper.GetDpi(window).PixelsPerInchX}; wallpaper={Wallpaper.Active}");
        foreach (double width in new[] { 1180d, 800d })
        {
            window.Width = width; window.Height = 740;
            foreach (string key in pages ?? TypographyAudit.Pages)
            {
                window.Navigate(key, instant: true); await Program.WaitForPage(window, key);
                if (key == "launch")
                    await MaterialControlsPreview.Until(() => RichUiAudit.Find<NewsPanel>(window).Any(news => news.LoadedImages > 0), "home news includes loaded photographs", 40);
                if (new[] { "mod", "modpack", "datapack", "resourcepack", "shader", "world" }.Contains(key))
                    await MaterialControlsPreview.Until(() => ArtworkSettled(window), "visible resource icons settled · " + key, 40);
                await Task.Delay(250); await MaterialControlsPreview.Layout(window);
                RichUiAudit.Shot(window, folder, key + "-" + width);
            }
        }
        window.Navigate("settings", instant: true); await Program.WaitForPage(window, "settings");
        RichUiAudit.Find<Rail>(window).Single().Select("look");
        window.Width = 1180; window.Height = 740;
        await MaterialControlsPreview.Layout(window);
        Console.WriteLine($"[APPEARANCE] Captured {(pages ?? TypographyAudit.Pages).Length} pages at 1180/800 DIP with current visual preferences.");
    }

    private static bool ArtworkSettled(MainWindow window)
    {
        var covers = RichUiAudit.Find<ProjectCover>(window).Where(cover => cover.IsVisible).ToArray();
        return covers.Any(cover => cover.Ready) && covers.All(cover => cover.Ready ||
            RichUiAudit.Field<TextBlock>(cover, "_fallback").Text is not ("加载中" or "正在加载封面…"));
    }
}
