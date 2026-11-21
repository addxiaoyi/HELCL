using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HelL.Desktop.Controls;
using HelL.Desktop.Models;
using HelL.Desktop.Services;

namespace HelL.Desktop;

public sealed partial class MainWindow
{
    private async Task CheckDesktopReadingAsync()
    {
        string folder = Path.Combine(Program.DataRoot, "reading");
        Directory.CreateDirectory(folder);
        const string code = "# 中文日志：保留空行和缩进\njava -Xmx4G -jar server.jar\n\n    保留四个空格的中文说明\nlong_argument=abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyz";
        const string content = "## 先核对实例与加载器\n保留当前存档，确认 Minecraft 版本和加载器后再安装资源。\n\n### 可复制的启动示例\n```shell\n" + code + "\n```\n日志末尾的中文说明也应保持清楚可读。";
        string theme = CurrentTheme, page = currentPage;
        double width = Width, height = Height;
        var messages = AiView.FindControl<ItemsControl>("MessageItems")!;
        var starters = AiView.FindControl<StackPanel>("StarterPanel")!;
        var originalMessages = messages.ItemsSource;
        bool originalStarters = starters.IsVisible;
        var taskList = TasksView.FindControl<ListBox>("TaskList")!;
        var originalTasks = taskList.ItemsSource;
        object? originalSelected = taskList.SelectedItem;
        var count = TasksView.FindControl<TextBlock>("CountLabel")!;
        string? originalCount = count.Text;
        var task = new DesktopTask("下载并校验一个名称很长的 Minecraft 整合包 · 中文日志排版验收", new DateTimeOffset(2026, 11, 20, 12, 30, 0, TimeSpan.Zero));
        task.Finish(DesktopTaskState.Failed, "网络连接暂时中断。已下载的文件仍保留，可重新执行原操作。");
        task.Log.Clear();
        for (int line = 1; line <= 16; line++) task.Log.Add($"12:30:{line:00}  校验资源片段 {line:00}：中文、English 与较长的错误恢复说明应自然换行并保持行距。");
        var failures = new List<string>();
        var samples = new List<object>();
        int checkedConditions = 0;
        void Check(bool passed, string label) { checkedConditions++; if (!passed) failures.Add(label); }
        try
        {
            foreach (string mode in new[] { "dark", "light" })
            {
                SelectTheme(mode, persist: false);
                foreach (int viewport in new[] { 800, 1180 })
                {
                    Width = viewport; Height = 760;
                    ShowPage("ai");
                    messages.ItemsSource = new[] { new DesktopAiMessage("user", "如何安全地排查整合包启动失败？", "complete"), new DesktopAiMessage("assistant", content, "complete") };
                    starters.IsVisible = false;
                    await SettleReading();
                    SaveIconWindow(Path.Combine(folder, $"ai-{mode}-{viewport}.png"));
                    MessageDocument document = messages.GetVisualDescendants().OfType<MessageDocument>().Last();
                    SelectableTextBlock codeText = document.GetLogicalDescendants().OfType<SelectableTextBlock>().Single(text => text.Text == code);
                    var prose = document.GetLogicalDescendants().OfType<SelectableTextBlock>().Single(text => text.Text == "保留当前存档，确认 Minecraft 版本和加载器后再安装资源。");
                    Button copy = document.GetLogicalDescendants().OfType<Button>().Single(button => button.Content?.ToString() == "复制代码");
                    document.ClipboardWriter = copied => copied == code ? Task.CompletedTask
                        : Task.FromException(new IOException("The code-copy preview received unexpected text."));
                    copy.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    Check(Avalonia.Automation.AutomationProperties.GetName(copy) == "代码已复制"
                        && Avalonia.Automation.AutomationProperties.GetLiveSetting(copy) == Avalonia.Automation.AutomationLiveSetting.Polite,
                        mode + " successful code copy exposes an accessible result");
                    copy = document.GetLogicalDescendants().OfType<Button>().Single(button => button.Content?.ToString() == "已复制代码");
                    document.ClipboardWriter = _ => Task.FromException(new IOException("Controlled clipboard rejection."));
                    copy.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    Check(Avalonia.Automation.AutomationProperties.GetName(copy) == "代码复制失败，请重试"
                        && Avalonia.Automation.AutomationProperties.GetLiveSetting(copy) == Avalonia.Automation.AutomationLiveSetting.Polite,
                        mode + " failed code copy exposes retry guidance to assistive technology");
                    document.ClipboardWriter = null;
                    copy.Content = "复制代码";
                    Avalonia.Automation.AutomationProperties.SetName(copy, "复制此代码块的原始内容");
                    double copyTop = (copy.TranslatePoint(default, document) ?? default).Y;
                    double codeTop = (codeText.TranslatePoint(default, document) ?? default).Y;
                    Check(prose.FontSize >= 14 && prose.LineHeight >= 22, mode + " assistant prose is too small or tightly spaced at " + viewport);
                    Check(codeText.FontSize >= 13 && codeText.LineHeight >= 20, mode + " code is too small or tightly spaced at " + viewport);
                    Check(copyTop + copy.Bounds.Height <= codeTop, mode + " code copying is below the long code block at " + viewport);
                    Check(codeText.Text == code, "code rendering changed whitespace or command text");
                    var shaped = codeText.TextLayout.TextLines.SelectMany(line => line.TextRuns).OfType<ShapedTextRun>().ToArray();
                    var glyphs = shaped.SelectMany(run => run.GlyphRun.GlyphInfos).Select(glyph => glyph.GlyphIndex).ToArray();
                    Check(glyphs.Length > 0 && glyphs.All(glyph => glyph != 0), mode + " code contains missing rendered glyphs at " + viewport);
                    string[] fonts = shaped.Select(run => run.ShapedBuffer.GlyphTypeface.FamilyName).Distinct().ToArray();
                    var chineseRuns = shaped.Where(run => run.Text.Span.Contains('中')).ToArray();
                    var latinRuns = shaped.Where(run => run.Text.ToString().Contains("java", StringComparison.Ordinal)).ToArray();
                    Check(chineseRuns.Length > 0 && chineseRuns.All(run => run.ShapedBuffer.GlyphTypeface.FamilyName == "Noto Sans SC"),
                        mode + " Chinese code did not use its embedded fallback at " + viewport);
                    Check(latinRuns.Length > 0 && latinRuns.All(run => run.ShapedBuffer.GlyphTypeface.FamilyName.Contains("Mono", StringComparison.OrdinalIgnoreCase)
                            || run.ShapedBuffer.GlyphTypeface.FamilyName == "Consolas"),
                        mode + " Latin code did not use a system monospace face at " + viewport);
                    ShowPage("tasks");
                    taskList.ItemsSource = new[] { task };
                    taskList.SelectedItem = task;
                    count.Text = "1 / 1 个任务 · 隔离排版验收";
                    await SettleReading();
                    SaveIconWindow(Path.Combine(folder, $"tasks-{mode}-{viewport}.png"));
                    var log = TasksView.FindControl<SelectableTextBlock>("LogText")!;
                    var detail = TasksView.FindControl<TextBlock>("DetailTitle")!;
                    Check(log.FontSize >= 13 && log.LineHeight >= 20, mode + " task log text lacks a readable size and line height at " + viewport);
                    Check(detail.Bounds.Width >= 200, mode + " the task title is squeezed by inline action buttons at " + viewport);
                    var details = TasksView.FindControl<Border>("LogDetails")!;
                    double listBottom = (taskList.TranslatePoint(new Point(0, taskList.Bounds.Height), TasksView) ?? default).Y;
                    var onlyRow = taskList.GetVisualDescendants().OfType<ListBoxItem>().Single();
                    double rowBottom = (onlyRow.TranslatePoint(new Point(0, onlyRow.Bounds.Height), TasksView) ?? default).Y;
                    double detailsTop = (details.TranslatePoint(default, TasksView) ?? default).Y;
                    Check(detailsTop - listBottom is >= 0 and <= 20, mode + " sparse task results leave a large blank gap before the log at " + viewport);
                    Check(detailsTop - rowBottom < 32, mode + " a single task row retains a tall empty list surface at " + viewport);
                    Check(log.Text!.Contains("中文、English"), "the long task log lost its content");
                    samples.Add(new { mode, viewport, proseSize = prose.FontSize, codeSize = codeText.FontSize, codeLineHeight = double.IsFinite(codeText.LineHeight) ? codeText.LineHeight : 0,
                        glyphs = glyphs.Length, missingGlyphs = glyphs.Count(glyph => glyph == 0), fonts,
                        copyBeforeCode = copyTop + copy.Bounds.Height <= codeTop, taskTitleWidth = detail.Bounds.Width, logSize = log.FontSize,
                        sparseDetailGap = detailsTop - listBottom, sparseRowToLogGap = detailsTop - rowBottom,
                        proseWeight = prose.FontWeight.ToString(), codeWeight = codeText.FontWeight.ToString(), logWeight = log.FontWeight.ToString(),
                        logLineHeight = double.IsFinite(log.LineHeight) ? log.LineHeight : 0, effectiveCodeLineHeight = codeText.TextLayout.TextLines[0].Height });
                }
            }
            Width = 800; Height = 600;
            ShowPage("tasks");
            taskList.ItemsSource = Enumerable.Range(1, 20).Select(index => new DesktopTask("受控任务 " + index)).ToArray();
            taskList.SelectedIndex = 0;
            count.Text = "20 / 20 个任务 · 隔离排版验收";
            await SettleReading();
            var logPanel = TasksView.FindControl<Border>("LogDetails")!;
            var copyLog = logPanel.GetLogicalDescendants().OfType<Button>().Single(button => button.Content?.ToString() is "复制日志" or "复制");
            Check(taskList.Bounds.Height <= taskList.MaxHeight + 1, "many tasks bypass the bounded list viewport");
            Check(copyLog.IsEffectivelyVisible && (copyLog.TranslatePoint(new Point(0, copyLog.Bounds.Height), TasksView) ?? default).Y < TasksView.Bounds.Height,
                "task-list growth hides the log actions in a short window");
            var taskScroll = taskList.GetVisualDescendants().OfType<ScrollViewer>().Single();
            Check(taskScroll.Extent.Height > taskScroll.Viewport.Height, "many task rows have no scrollable viewport");
            Button expand = logPanel.GetLogicalDescendants().OfType<Button>().Single(button => button.Content?.ToString() is "展开日志" or "收起日志");
            expand.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await SettleReading();
            var logScroll = logPanel.GetLogicalDescendants().OfType<ScrollViewer>().Single();
            Check(double.IsPositiveInfinity(logPanel.MaxHeight) && logScroll.Viewport.Height > 0
                && (logPanel.TranslatePoint(new Point(0, logPanel.Bounds.Height), TasksView) ?? default).Y <= TasksView.Bounds.Height + 1,
                "expanded logs lose their viewport or overflow the short task page");
            expand.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await SettleReading();
            SaveIconWindow(Path.Combine(folder, "tasks-many-800x600.png"));

            var unclosed = new MessageDocument { Text = "```shell\n" + code };
            Check(unclosed.GetLogicalDescendants().OfType<SelectableTextBlock>().Single().Text == code,
                "an unfinished code fence changes the original copied text");
        }
        finally
        {
            messages.ItemsSource = originalMessages;
            starters.IsVisible = originalStarters;
            taskList.ItemsSource = originalTasks;
            taskList.SelectedItem = originalSelected;
            count.Text = originalCount;
            SelectTheme(theme, persist: false);
            Width = width; Height = height;
            ShowPage(page);
        }
        File.WriteAllText(Path.Combine(Program.DataRoot, "desktop-reading.json"), System.Text.Json.JsonSerializer.Serialize(new
        { ok = failures.Count == 0, failures, checks = checkedConditions, samples, realAiRequest = false, nativeScreenReaderVerified = false }));
        if (failures.Count > 0) throw new IOException("Desktop reading refinement: " + string.Join("; ", failures));
    }

    private static async Task SettleReading()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        await Task.Delay(100);
    }
}
