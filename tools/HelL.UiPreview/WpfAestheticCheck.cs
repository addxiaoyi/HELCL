using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using HelL;
using HelL.Models;
using HelL.Pages;
using HelL.Services;

internal static class WpfAestheticCheck
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static async Task Run(MainWindow window, string folder)
    {
        Directory.CreateDirectory(folder);
        var failures = new List<string>();
        var checks = new List<string>();
        var measurements = new List<object>();
        void Check(bool passed, string label)
        {
            checks.Add(label);
            if (!passed) failures.Add(label);
            Console.WriteLine((passed ? "[PASS] " : "[FAIL] ") + label);
        }

        foreach (double width in new[] { 400d, 900d })
        {
            var first = new Border { Height = 40 };
            var second = new Border { Height = 60 };
            var pair = new AdaptiveColumns(first, second, 180, 600, gap: 36);
            Layout(pair, width);
            Check(Math.Abs(pair.DesiredSize.Height - (width < 600 ? 136 : 60)) < 1,
                "paired content retains its stacked/wide layout at " + width);
            second.Visibility = Visibility.Collapsed;
            Layout(pair, width);
            Check(Math.Abs(pair.DesiredSize.Height - 40) < 1 && Math.Abs(first.ActualWidth - width) < 1,
                "a collapsed second pane releases its gap and column at " + width);
            first.Visibility = Visibility.Collapsed;
            second.Visibility = Visibility.Visible;
            Layout(pair, width);
            Check(Math.Abs(pair.DesiredSize.Height - 60) < 1 && Math.Abs(second.ActualWidth - width) < 1,
                "a collapsed first pane leaves the visible pane full width at " + width);
            first.Visibility = Visibility.Visible;
            Layout(pair, width);
            Check(Math.Abs(pair.DesiredSize.Height - (width < 600 ? 136 : 60)) < 1,
                "restoring a pane restores the original layout at " + width);
        }

        bool originalDark = App.IsDark;
        double originalWidth = window.Width, originalHeight = window.Height;
        window.Navigate("launch", true);
        await Program.WaitForPage(window, "launch");
        var launch = (LaunchPage)window.GetPage("launch")!;
        var bannerTitle = (TextBlock)launch.GetType().GetField("_bTitle", Hidden)!.GetValue(launch)!;
        string originalTitle = bannerTitle.Text;
        var status = (TextBlock)launch.GetType().GetField("_status", Hidden)!.GetValue(launch)!;
        var progress = (ProgressBar)launch.GetType().GetField("_prog", Hidden)!.GetValue(launch)!;
        string originalStatus = status.Text;
        Visibility originalProgress = progress.Visibility;
        status.Text = ""; progress.Visibility = Visibility.Collapsed;
        await Settle(window);
        var statusArea = (FrameworkElement)status.Parent;
        Check(statusArea.Visibility == Visibility.Collapsed, "an empty status area releases its vertical space");
        status.Text = "受控进度状态";
        await Settle(window);
        Check(statusArea.IsVisible && status.IsVisible, "a new status restores its visible area");
        status.Text = ""; progress.Visibility = Visibility.Visible;
        await Settle(window);
        Check(statusArea.IsVisible && progress.IsVisible, "a progress bar restores its area independently of status text");
        progress.Visibility = Visibility.Collapsed;
        var newsHost = (SPanel)launch.GetType().GetField("_newsHost", Hidden)!.GetValue(launch)!;
        UIElement[] originalNews = newsHost.Children.Cast<UIElement>().ToArray();
        var fixture = new NewsPanel { Compact = true };
        fixture.Show(new NewsFeed
        {
            Cached = true,
            Rows = new List<NewsRow>
            {
                new() { Title = "Minecraft 1.21.1 更新说明", Body = "维护现有世界前，先确认版本与加载器兼容，并保留最近的存档备份。", Date = "2026-11-17", Type = "release", Version = "1.21.1" },
                new() { Title = "一起探索新的建筑与冒险玩法", Body = "查看本次更新的主要变化，选择适合当前实例的资源与模组。", Date = "2026-11-10", Type = "snapshot", Version = "snapshot" },
                new() { Title = "长标题排版样例：保留现有游戏流程，让正文与辅助信息在窄窗口中也能自然换行", Body = "本段是隔离排版夹具，用于观察多行标题、正文和缩略图的对齐；没有请求外部文章。", Date = "2026-11-03", Type = "release", Version = "1.21" }
            }
        });
        newsHost.Children.Clear(); newsHost.Children.Add(fixture);
        try
        {
            foreach (bool dark in new[] { false, true })
            {
                App.ApplyTheme(dark);
                foreach (double width in new[] { 1180d, 900d })
                {
                    window.Width = width; window.Height = 740;
                    await Settle(window);
                    string suffix = (dark ? "dark" : "light") + "-" + width;
                    var title = (TextBlock)launch.GetType().GetField("_bTitle", Hidden)!.GetValue(launch)!;
                    var action = (Button)launch.GetType().GetField("_installBtn", Hidden)!.GetValue(launch)!;
                    Check(action.IsVisible && action.ActualHeight >= 44, "the primary action retains its target size at " + suffix);
                    Check(title.ActualWidth > 0 && title.ActualHeight > 0 && title.TextWrapping == TextWrapping.Wrap,
                        "the launch title remains measurable and can wrap at " + suffix);
                    foreach (NewsImage picture in CustomizationPreview.Walk(fixture).OfType<NewsImage>())
                    {
                        Point start = picture.TranslatePoint(default, fixture);
                        Check(start.X >= -1 && start.X + picture.ActualWidth <= fixture.ActualWidth + 1,
                            "news artwork stays inside its row at " + suffix);
                        Check(!picture.Ready
                            && CustomizationPreview.Walk(picture).OfType<VectorGlyph>().Any(glyph => glyph.Text == "\ue8a5" && glyph.IsVisible)
                            && CustomizationPreview.Walk(picture).OfType<TextBlock>().Any(text => text.Text == "暂无封面" && text.IsVisible),
                            "unavailable news artwork keeps a branded icon and readable label at " + suffix);
                    }
                    measurements.Add(new { view = suffix, installHeight = action.ActualHeight, headlineSize = title.FontSize, newsTop = fixture.TranslatePoint(default, window).Y });
                    Program.SaveSnapshot(window, Path.Combine(folder, "home-" + suffix + ".png"));
                }
            }
            window.Width = 900; window.Height = 740;
            bannerTitle.Text = "长实例名称 · 红石建筑与多人冒险工作区 · 保留存档与资源配置并准备下次联机 · Minecraft 1.21.1";
            await Settle(window);
            var primary = (Button)launch.GetType().GetField("_installBtn", Hidden)!.GetValue(launch)!;
            Check(bannerTitle.ActualHeight > bannerTitle.LineHeight && primary.TranslatePoint(default, window).Y + primary.ActualHeight < window.ActualHeight,
                "a long launch headline wraps while keeping its primary action in view");
            Program.SaveSnapshot(window, Path.Combine(folder, "home-long-title-900.png"));
            bannerTitle.Text = originalTitle;
            var pageHost = (ContentControl)window.FindName("PageHost");
            Transform originalScale = pageHost.LayoutTransform;
            try
            {
                pageHost.LayoutTransform = new ScaleTransform(2, 2);
                await Settle(window);
                Check(primary.IsVisible && primary.TranslatePoint(default, window).X + primary.ActualWidth * 2 <= window.ActualWidth + 1,
                    "the primary action remains horizontally inside the window at two-times page scale");
                Check(fixture.ActualWidth > 0 && CustomizationPreview.Walk(fixture).OfType<Button>().Any(button => button.IsVisible),
                    "news controls remain available with a two-times page scale");
                Program.SaveSnapshot(window, Path.Combine(folder, "home-scale-200.png"));
            }
            finally { pageHost.LayoutTransform = originalScale; }
        }
        finally
        {
            status.Text = originalStatus; progress.Visibility = originalProgress;
            bannerTitle.Text = originalTitle;
            newsHost.Children.Clear();
            foreach (UIElement child in originalNews) newsHost.Children.Add(child);
            App.ApplyTheme(originalDark);
            window.Width = originalWidth; window.Height = originalHeight;
        }
        await Settle(window);
        Program.SaveSnapshot(window, Path.Combine(folder, "home-restored-source.png"));
        File.WriteAllText(Path.Combine(folder, "wpf-aesthetics.json"), JsonSerializer.Serialize(new
        {
            ok = failures.Count == 0, failures, checks, measurements,
            fixtureNews = true, externalNewsRequestedByCheck = false, nativeScreenReaderVerified = false
        }));
        if (failures.Count > 0) throw new IOException(string.Join("; ", failures));
    }

    private static void Layout(FrameworkElement element, double width)
    {
        element.Measure(new Size(width, double.PositiveInfinity));
        element.Arrange(new Rect(0, 0, width, element.DesiredSize.Height));
        element.UpdateLayout();
    }

    private static async Task Settle(MainWindow window)
    {
        window.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
    }
}
