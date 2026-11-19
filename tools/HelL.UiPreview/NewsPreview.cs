using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HelL;
using HelL.Core.Launcher;
using HelL.Pages;
using HelL.Services;

internal static class NewsPreview
{
    private static int _checks;
    internal static async Task Capture(MainWindow window, string folder)
    {
        Directory.CreateDirectory(folder);
        window.Navigate("launch", instant: true);
        await Program.WaitForPage(window, "launch");
        var page = Walk(window).OfType<LaunchPage>().Single();
        await Until(() => !Read<bool>(page, "_newsLoading"), "Official feed settles", 500);
        var panel = Read<NewsPanel>(page, "_news");
        var feed = await AppServices.Client.CallAsync<NewsFeed>("news_feed") ?? throw new IOException("No news payload");
        Check(feed.Rows.Count > 0, "Official news is available");
        Check(feed.Rows.All(row => row.Image.StartsWith("https://launchercontent.mojang.com/", StringComparison.Ordinal)), "Official cover URLs are absolute");
        Check(feed.Rows.Take(6).All(row => NewsLocalization.ContainsChinese(row.Title) && NewsLocalization.ContainsChinese(row.Body)), "Chinese UI displays localized titles and summaries");
        await Until(() => panel.LoadedImages >= 3, "Three official covers finish decoding", 400);
        Check(panel.LoadedImages >= 3, "Actual cover downloads are rendered");
        var cache = await AppServices.Client.CallAsync<NewsFeed>("news_feed");
        Check(cache?.Cached == true && cache.Rows.Count == feed.Rows.Count, "Cached news can be read without fetching again");
        File.WriteAllText(Path.Combine(folder, "news-metadata.json"), System.Text.Json.JsonSerializer.Serialize(feed, BridgeClient.JsonOpt));

        foreach (bool dark in new[] { false, true })
        {
            App.ApplyTheme(dark);
            foreach (var size in new[] { (1180d, 740d), (900d, 580d) })
            {
                window.Width = size.Item1; window.Height = size.Item2;
                await Layout(window);
                Program.SaveSnapshot(window, Path.Combine(folder, "launch-" + (dark ? "dark" : "light") + "-" + size.Item1 + ".png"));
                foreach (var image in Walk(panel).OfType<NewsImage>().Where(image => image.IsVisible))
                {
                    Point start = image.TranslatePoint(new Point(), panel);
                    Check(start.X >= -1 && start.X + image.ActualWidth <= panel.ActualWidth + 1, "News image fits at " + size.Item1);
                }
                var firstCover = Walk(panel).OfType<NewsImage>().First();
                Point coverPosition = firstCover.TranslatePoint(new Point(), window);
                Check(coverPosition.Y >= 0 && coverPosition.Y + firstCover.ActualHeight <= window.ActualHeight,
                    "Featured cover is visible without scrolling at " + size.Item1);
                var headline = Walk(panel).OfType<TextBlock>().First(text => text.Text == feed.Rows[0].Title);
                Check(headline.TranslatePoint(new Point(), window).Y + headline.ActualHeight <= window.ActualHeight - 20,
                    "Chinese headline stays visible at " + size.Item1);
            }
        }
        Click(Named(panel, "正式版"));
        await Layout(window);
        Check(Walk(panel).OfType<TextBlock>().Any(text => text.Text.StartsWith("正式更新")), "Release filter has a meaningful result");
        Click(Named(panel, "全部"));
        await Layout(window);
        Click(Named(panel, feed.Rows[0].Title));
        await Layout(window);
        Check(Dlg.AnyOpen, "News article opens in an in-app reading panel");
        Check(Walk(Dlg.Host!).OfType<Button>().Any(button => AutomationProperties.GetName(button) == "打开官方文章"), "Official source link is available");
        var original = Walk(Dlg.Host!).OfType<Expander>().Single(expander => expander.Header as string == "查看英文原文");
        original.IsExpanded = true;
        await Layout(window);
        Check(Walk(original).OfType<TextBlock>().Any(text => text.Text == feed.Rows[0].OriginalBody), "Original summary is preserved beside the Chinese text");
        Program.SaveSnapshot(window, Path.Combine(folder, "article-dark.png"));
        Dlg.Host!.Children[Dlg.Host.Children.Count - 1].RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        await Layout(window);
        Check(!Dlg.AnyOpen, "Article panel closes with Escape");

        panel.Show(new NewsFeed { Rows = feed.Rows, Cached = true, Error = "fixture offline" });
        Check(Walk(panel).OfType<TextBlock>().Any(text => text.Text.Contains("显示上次内容")), "Offline state keeps cached stories and identifies the failure");
        panel.Show(new NewsFeed());
        Check(Walk(panel).OfType<TextBlock>().Any(text => text.Text.Contains("暂时没有新闻")), "Empty news has visible recovery context");
        var missing = new NewsImage("", "missing image fixture", 80);
        await missing.Loading;
        Check(!missing.Ready && Walk(missing).OfType<TextBlock>().Single().Text == "暂无封面", "Missing artwork does not produce a broken image");
        panel.Show(feed);
        string translated = await Task.Run(() => NewsLocalization.TranslatePending(News.LoadCached().Take(1).ToList(),
            HelL.Core.PyDict.Of("ai_local_url", "http://127.0.0.1:11434"), refresh: true));
        Check(translated.StartsWith("已生成", StringComparison.Ordinal), "A real local model can translate a new official summary");
        File.WriteAllText(Path.Combine(folder, "local-translation-status.txt"), translated);
        Console.WriteLine("[NEWS] " + _checks + " checks passed, using live official news and cover downloads.");
    }

    private static async Task Until(Func<bool> test, string message, int attempts)
    { for (int n = 0; n < attempts; n++) { if (test()) return; await Task.Delay(100); } throw new TimeoutException(message); }
    private static Task Layout(Window window) { window.UpdateLayout(); return Task.Delay(200); }
    private static T Read<T>(object target, string field) => (T)target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static Button Named(DependencyObject root, string name) => Walk(root).OfType<Button>().First(button => button.IsVisible && (AutomationProperties.GetName(button) == name || button.Content as string == name));
    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        for (int n = 0; n < VisualTreeHelper.GetChildrenCount(root); n++)
        { var child = VisualTreeHelper.GetChild(root, n); yield return child; foreach (var next in Walk(child)) yield return next; }
    }
    private static void Check(bool condition, string description)
    { if (!condition) throw new InvalidOperationException(description); _checks++; Console.WriteLine("[PASS] " + description); }
}
