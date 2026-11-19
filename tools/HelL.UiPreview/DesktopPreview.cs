using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using HelL;
using HelL.Services;

internal static class DesktopPreview
{
    internal static async Task Capture(MainWindow window, string folder)
    {
        Directory.CreateDirectory(folder);
        Motion.Enabled = false;
        int checkedPanes = 0;
        int checkedRows = 0;
        var failures = new List<string>();
        foreach (bool dark in new[] { false, true })
        {
            App.ApplyTheme(dark);
            foreach (var size in new[] { (Width: 1180, Height: 740), (Width: 900, Height: 580) })
            {
                window.Width = size.Width;
                window.Height = size.Height;
                string suffix = $"{(dark ? "dark" : "light")}-{size.Width}";
                window.Navigate("launch", instant: true);
                await Program.WaitForPage(window, "launch");
                Program.SaveSnapshot(window, Path.Combine(folder, "launch-" + suffix + ".png"));
                window.Navigate("settings", instant: true);
                await Program.WaitForPage(window, "settings");
                var page = (ContentControl)window.FindName("PageHost");
                var rail = Descendants(page).OfType<Rail>().Single();
                foreach (string category in new[] { "look", "download", "game", "ai", "launcher" })
                {
                    rail.Select(category);
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    if (category == "game" || category == "ai")
                    {
                        var settings = (HelL.Pages.SettingsPage)page.Content;
                        string fieldName = category == "game" ? "_jvm" : "_aiModel";
                        var editor = (Control)typeof(HelL.Pages.SettingsPage).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(settings)!;
                        string previous = editor is TextBox input ? input.Text : ((ComboBox)editor).Text;
                        string longValue = category == "game"
                            ? string.Join(" ", Enumerable.Repeat("-XX:MaxGCPauseMillis=200", 12))
                            : "model-with-a-long-provider-name-and-version-2026-11";
                        if (editor is TextBox textBox) textBox.Text = longValue;
                        else ((ComboBox)editor).Text = longValue;
                        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                        CheckBounds(editor, fieldName);
                        string[] actions = category == "game"
                            ? new[] { "Aikar 参数", "连接", "扫描局域网", "USB 自动连接" }
                            : new[] { "刷新模型", "重新检测", "复制地址" };
                        foreach (string action in actions)
                        {
                            var button = Descendants(page).OfType<Button>().Single(item => AutomationProperties.GetName(item) == action);
                            CheckBounds(button, action);
                        }
                        if (category == "ai")
                        {
                            foreach (string label in new[] { "识图", "MCP Server" })
                            {
                                var field = Descendants(page).OfType<FrameworkElement>()
                                    .Single(item => (AutomationProperties.GetLabeledBy(item) as TextBlock)?.Text == label);
                                TextBlock status = Descendants(field).OfType<TextBlock>().Last();
                                string originalStatus = status.Text;
                                status.Text = string.Join(" ", Enumerable.Repeat("Service unavailable; retry when the connection recovers.", 4));
                                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                                CheckBounds(status, label + " status");
                                status.Text = originalStatus;
                            }
                        }
                        if (editor is TextBox restoredInput) restoredInput.Text = previous;
                        else ((ComboBox)editor).Text = previous;
                        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    }
                    var scroll = Descendants(page).OfType<SmoothScroll>().Single();
                    scroll.ScrollToTop();
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    Program.SaveSnapshot(window, Path.Combine(folder, $"settings-{category}-{suffix}.png"));
                    scroll.ScrollToBottom();
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    var save = Descendants(page).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "保存设置");
                    Point corner = save.TranslatePoint(new Point(), window);
                    if (!save.IsVisible || corner.X < 0 || corner.Y < 0 ||
                        corner.X + save.ActualWidth > window.ActualWidth || corner.Y + save.ActualHeight > window.ActualHeight)
                        throw new InvalidOperationException("Settings save action escaped the viewport: " + category + " / " + suffix);
                    Program.SaveSnapshot(window, Path.Combine(folder, $"settings-{category}-bottom-{suffix}.png"));
                    checkedPanes++;

                    void CheckBounds(FrameworkElement control, string name)
                    {
                        var viewport = Descendants(page).OfType<SmoothScroll>().Single();
                        Point origin = control.TranslatePoint(new Point(), viewport);
                        bool fits = control.IsVisible && control.ActualWidth > 0 && origin.X >= -1 &&
                            origin.X + control.ActualWidth <= viewport.ViewportWidth + 1;
                        string message = $"Settings {name} fits {suffix} with a long field value";
                        Console.WriteLine((fits ? "[PASS] " : "[FAIL] ") + message);
                        if (fits) checkedRows++;
                        else failures.Add(message + $" (x={origin.X:F1}, width={control.ActualWidth:F1}, viewport={viewport.ViewportWidth:F1})");
                    }
                }
            }
        }
        Console.WriteLine($"[DESKTOP PASS] {checkedPanes} settings panes retain a visible save action after scrolling");
        if (failures.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
        Console.WriteLine($"[SETTINGS PASS] {checkedRows} field and action bounds checks");
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (DependencyObject descendant in Descendants(child)) yield return descendant;
        }
    }
}
