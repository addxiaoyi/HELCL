using System;
using System.Collections;
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
using HelL.Pages;
using HelL.Services;

internal static class WpfReadingCheck
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object Field(object owner, string name) => owner.GetType().GetField(name, Hidden)!.GetValue(owner)!;
    private static object? Call(object owner, string name, params object[] arguments) => owner.GetType().GetMethods(Hidden | BindingFlags.DeclaredOnly)
        .Single(method => method.Name == name && method.GetParameters().Length == arguments.Length).Invoke(owner, arguments);

    internal static async Task Run(MainWindow window, string folder)
    {
        Directory.CreateDirectory(folder);
        var failures = new List<string>();
        var checks = new List<string>();
        var samples = new List<object>();
        void Check(bool passed, string label)
        {
            checks.Add(label);
            if (!passed) failures.Add(label);
            Console.WriteLine((passed ? "[PASS] " : "[FAIL] ") + label);
        }

        bool darkBefore = App.IsDark;
        double widthBefore = window.Width, heightBefore = window.Height;
        window.Navigate("version", true);
        await Program.WaitForPage(window, "version");
        var version = (VersionPage)window.GetPage("version")!;
        Call(version, "CancelFetch");
        object rowsBefore = Field(version, "_all");
        var versions = (ListBox)Field(version, "_list");
        var instances = (ComboBox)Field(version, "_inst");
        object[] instancesBefore = instances.Items.Cast<object>().ToArray();
        object? selectedBefore = instances.SelectedItem;
        var changed = (SelectionChangedEventHandler)Delegate.CreateDelegate(typeof(SelectionChangedEventHandler), version,
            typeof(VersionPage).GetMethod("InstChanged", Hidden)!);
        instances.SelectionChanged -= changed;
        var rowType = typeof(VersionPage).GetNestedType("Row", BindingFlags.NonPublic)!;
        var rows = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(rowType))!;
        foreach (var entry in new[] { ("1.21.4", "release", "2024-12-03"), ("1.21.1", "release", "2024-08-08"), ("1.20.4", "release", "2023-12-07"), ("1.20.1", "release", "2023-06-12") })
        {
            object row = Activator.CreateInstance(rowType, true)!;
            rowType.GetProperty("Version")!.SetValue(row, entry.Item1);
            rowType.GetProperty("Type")!.SetValue(row, entry.Item2);
            rowType.GetProperty("Date")!.SetValue(row, entry.Item3);
            rows.Add(row);
        }
        typeof(VersionPage).GetField("_all", Hidden)!.SetValue(version, rows);
        instances.Fill(new[] { "中文生存世界 · 长期建筑与好友联机工作区" });
        Call(version, "ApplyFilter");
        var install = (Button)Field(version, "_install");
        var summary = (TextBlock)Field(version, "_selectedVersion");
        var hint = (TextBlock)Field(version, "_versionHint");
        string id = "reading-check-" + Guid.NewGuid().ToString("N");
        TaskStore.Handle(new BridgeEvent { Event = "task_added", TaskId = id, Title = "安装 Minecraft 1.21.1 · 中文生存世界的资源与运行环境" });
        TaskRow task = TaskStore.Get(id)!;
        const string longFailure = "下载未完成。目标目录：D:\\Games\\Minecraft\\中文生存世界\\versions\\1.21.1-Fabric\\libraries\\net\\minecraft\\client.jar。请检查网络连接后重试，已有文件与游戏存档保留。";
        task.Status = longFailure; task.Finished = true; task.Success = false; task.Indeterminate = false;
        task.Append("[12:34:56] 校验资源：D:\\Games\\Minecraft\\中文生存世界\\versions\\1.21.1\\client.jar");
        task.Append("[12:34:57] 网络连接暂不可用。已有文件已保留，重新执行会继续校验。");
        task.Append("[12:34:58] 这是隔离的阅读排版夹具，没有下载、安装或修改真实游戏文件。");
        TaskStore.Notify(id);
        try
        {
            foreach (bool dark in new[] { false, true })
            {
                App.ApplyTheme(dark);
                foreach (double width in new[] { 900d, 1180d })
                {
                    window.Width = width; window.Height = 800;
                    window.Navigate("version", true);
                    await Settle(window);
                    ((Panel)window.FindName("ToastHost")).Children.Clear();
                    string suffix = (dark ? "dark" : "light") + "-" + width;
                    Check(install.IsVisible && install.ActualHeight >= 44, "installation keeps a full primary target at " + suffix);
                    Check(summary.FontSize <= 22 && summary.LineHeight >= summary.FontSize + 5 && hint.FontSize >= 13 && hint.LineHeight >= 20,
                        "selected-version typography separates identity and supporting text at " + suffix);
                    var dates = CustomizationPreview.Walk(versions).OfType<TextBlock>().Where(text => text.Text == "2024-12-03").ToArray();
                    Check(dates.Length > 0 && dates.All(text => text.FontSize >= 12), "release dates stay readable at " + suffix);
                    Check(CustomizationPreview.Walk(version).OfType<TextBlock>().Any(text => text.Text == "1 · 选择版本")
                        && CustomizationPreview.Walk(version).OfType<TextBlock>().Any(text => text.Text == "2 · 确认安装"),
                        "the installation choices have a clear reading order at " + suffix);
                    Check(install.TranslatePoint(default, window).X >= 195 && install.TranslatePoint(new Point(install.ActualWidth, 0), window).X <= window.ActualWidth,
                        "the installation action stays within the content viewport at " + suffix);
                    Program.SaveSnapshot(window, Path.Combine(folder, "install-" + suffix + ".png"));

                    window.Navigate("tasks", true);
                    await Program.WaitForPage(window, "tasks");
                    var tasks = (TasksPage)window.GetPage("tasks")!;
                    Call(tasks, "Rebuild");
                    await Settle(window);
                    var cards = (IDictionary)Field(tasks, "_cards");
                    object card = cards[id]!;
                    T Part<T>(string name) => (T)card.GetType().GetProperty(name)!.GetValue(card)!;
                    var title = Part<TextBlock>("Title");
                    var status = Part<TextBlock>("Status");
                    var log = Part<TextBox>("Log");
                    var toggle = Part<Button>("Toggle");
                    if (Part<Border>("LogWrap").Visibility != Visibility.Visible) toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Settle(window);
                    ((Panel)window.FindName("ToastHost")).Children.Clear();
                    Check(status.Text.Contains("client.jar") && status.TextWrapping == TextWrapping.Wrap && status.FontSize >= 13 && status.LineHeight >= 20,
                        "long task errors keep readable text and their complete path at " + suffix);
                    Check(log.IsReadOnly && log.ActualHeight >= 200 && log.FontSize >= 12.5 && log.Text.Contains("中文生存世界"),
                        "expanded logs keep room for reading and preserve selectable content at " + suffix);
                    Check(CustomizationPreview.Walk(Part<Border>("Root")).OfType<TextBlock>().Any(text => text.Text == "未完成"),
                        "task outcome has a text marker separate from the error explanation at " + suffix);
                    log.Select(0, Math.Min(12, log.Text.Length));
                    Call(tasks, "Sync", task);
                    Check(log.SelectionLength > 0 && log.IsReadOnly, "task refresh retains its log selection at " + suffix);
                    Check(toggle.TranslatePoint(new Point(toggle.ActualWidth, 0), window).X <= window.ActualWidth,
                        "task actions remain inside the content viewport at " + suffix);
                    Program.SaveSnapshot(window, Path.Combine(folder, "tasks-" + suffix + ".png"));
                    task.Finished = false;
                    Call(tasks, "Sync", task);
                    await Settle(window);
                    bool runningProgress = Part<ProgressBar>("Bar").IsVisible;
                    task.Finished = true;
                    Call(tasks, "Sync", task);
                    await Settle(window);
                    Check(runningProgress && !Part<ProgressBar>("Bar").IsVisible,
                        "progress remains available while running and releases space after completion at " + suffix);
                    samples.Add(new { view = suffix, installHeight = install.ActualHeight, selectedSize = summary.FontSize,
                        hintSize = hint.FontSize, logHeight = log.ActualHeight, logSize = log.FontSize, statusSize = status.FontSize });
                }
            }
            window.Navigate("version", true); window.Width = 900;
            summary.Text = "Minecraft 1.21.1-Fabric · 中文生存世界与长期联机工作区";
            await Settle(window);
            Check(summary.ActualHeight > summary.LineHeight && summary.TranslatePoint(new Point(summary.ActualWidth, 0), window).X <= window.ActualWidth,
                "a long version identity wraps inside its installation panel");
            Program.SaveSnapshot(window, Path.Combine(folder, "install-long-name-900.png"));
            var host = (ContentControl)window.FindName("PageHost");
            Transform scaleBefore = host.LayoutTransform;
            try
            {
                host.LayoutTransform = new ScaleTransform(2, 2);
                await Settle(window);
                var pageScroll = (SmoothScroll)version.Content;
                pageScroll.ScrollToVerticalOffset(pageScroll.ScrollableHeight);
                await Settle(window);
                Check(install.IsVisible && install.TranslatePoint(new Point(install.ActualWidth, 0), window).X <= window.ActualWidth + 1,
                    "the installation action remains horizontally available at two-times page layout");
                Check(install.TranslatePoint(default, window).Y >= 48 && install.TranslatePoint(new Point(0, install.ActualHeight), window).Y <= window.ActualHeight,
                    "the installation action can be scrolled fully into view at two-times page layout");
                Program.SaveSnapshot(window, Path.Combine(folder, "install-scale-200.png"));
                window.Navigate("tasks", true);
                await Settle(window);
                var tasks = (TasksPage)window.GetPage("tasks")!;
                var card = ((IDictionary)Field(tasks, "_cards"))[id]!;
                var log = (TextBox)card.GetType().GetProperty("Log")!.GetValue(card)!;
                var root = (Border)card.GetType().GetProperty("Root")!.GetValue(card)!;
                ((Border)card.GetType().GetProperty("LogWrap")!.GetValue(card)!).BringIntoView();
                await Settle(window);
                Check(log.IsVisible && log.IsReadOnly && root.TranslatePoint(new Point(root.ActualWidth, 0), window).X <= window.ActualWidth + 1,
                    "task text and logs stay horizontally contained at two-times page layout");
                Check(log.TranslatePoint(default, window).Y >= 48 && log.TranslatePoint(new Point(0, log.ActualHeight), window).Y <= window.ActualHeight,
                    "the complete log reading area can be scrolled into view at two-times page layout");
                Program.SaveSnapshot(window, Path.Combine(folder, "tasks-scale-200.png"));
            }
            finally { host.LayoutTransform = scaleBefore; }
        }
        finally
        {
            TaskStore.Remove(id);
            instances.SelectionChanged -= changed;
            instances.Items.Clear(); foreach (object item in instancesBefore) instances.Items.Add(item);
            instances.SelectedItem = selectedBefore;
            instances.SelectionChanged -= changed; instances.SelectionChanged += changed;
            typeof(VersionPage).GetField("_all", Hidden)!.SetValue(version, rowsBefore);
            Call(version, "ApplyFilter");
            App.ApplyTheme(darkBefore); window.Width = widthBefore; window.Height = heightBefore;
            window.Navigate("launch", true);
            await Settle(window);
        }
        File.WriteAllText(Path.Combine(folder, "wpf-reading.json"), JsonSerializer.Serialize(new
        { ok = failures.Count == 0, failures, checks, samples, fixtureManifest = true, realInstallation = false, nativeScreenReaderVerified = false }));
        if (failures.Count > 0) throw new IOException(string.Join("; ", failures));
    }

    private static async Task Settle(MainWindow window)
    {
        window.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Task.Delay(80);
        window.UpdateLayout();
    }
}
