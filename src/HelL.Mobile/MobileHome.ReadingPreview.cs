using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HelL.Mobile.Ai;
using HelL.Mobile.Controls;

namespace HelL.Mobile;

public sealed partial class MobileHome
{
    private async Task CheckMobileReadingAsync()
    {
        var failures = new List<string>();
        var checks = new List<string>();
        var samples = new List<object>();
        void Check(bool passed, string label) { checks.Add(label); if (!passed) failures.Add(label); }
        int theme = ThemePicker.SelectedIndex;
        string page = currentPage;
        string[] history = pageHistory.ToArray();
        var messages = AiMessages.ItemsSource;
        bool settings = AiSettingsPanel.IsVisible;
        bool empty = AiEmpty.IsVisible;
        bool undo = AiUndoClearButton.IsVisible;
        string? notice = AiChatNotice.Text;
        Vector offset = AiMessagesScroll.Offset;
        const string prose = "先确认实例的 Minecraft 版本与加载器，再安装兼容的模组。";
        const string command = "# 这是中文注释：保留命令与原始缩进\njava -Xmx4G -jar minecraft-server-with-a-long-file-name.jar --nogui\n";
        var fixture = new[]
        {
            new ChatEntry("user", "如何排查启动失败，并保留当前存档？", "complete"),
            new ChatEntry("assistant", "## 排查步骤\n" + prose + "\n\n- 先备份存档\n- 再核对日志\n\n```shell\n" + command + "```\n\n收到报错后保留完整文本，再检查版本与运行环境。", "complete")
        };
        try
        {
            foreach (var (index, name) in new[] { (0, "dark"), (1, "light") })
            {
                ThemePicker.SelectedIndex = index;
                ShowPage("ai", remember: false);
                AiSettingsPanel.IsVisible = false;
                AiEmpty.IsVisible = false;
                AiUndoClearButton.IsVisible = false;
                AiChatNotice.Text = "";
                AiMessages.ItemsSource = fixture;
                AiMessagesScroll.Offset = Vector.Zero;
                await SettleReadingAsync();
                await Capture("reading-chat-" + name);
                var content = AiMessages.GetVisualDescendants().OfType<ChatContent>().Last();
                var body = content.GetVisualDescendants().OfType<SelectableTextBlock>().Single(text => text.Text == prose);
                var code = content.GetVisualDescendants().OfType<SelectableTextBlock>().Single(text => text.Text == command);
                var copy = content.GetVisualDescendants().OfType<Button>().Single(button => button.Content?.ToString() == "复制代码");
                var role = AiMessages.GetVisualDescendants().OfType<TextBlock>().First(text => text.Text == "AI");
                Check(body.FontSize >= 15 * EffectiveTextScale - 0.1, name + " newly generated prose uses the active readable text size");
                Check(body.LineHeight >= body.FontSize * 1.45, name + " prose line height grows with the active text size");
                Check(body.FontSize > role.FontSize, name + " message content is more prominent than its role label");
                Check(copy.Bounds.Height >= 44 && copy.Bounds.Width >= 44, name + " code copying retains a complete touch target");
                Check(code.Text == command, name + " code rendering retains the original command and indentation");
                Check(code.FontSize >= 13 * EffectiveTextScale - 0.1 && code.LineHeight >= code.FontSize * 1.4,
                    name + " newly generated code respects text scale and readable leading");
                var codeScroll = code.GetVisualAncestors().OfType<ScrollViewer>().First();
                Check(codeScroll.Viewport.Width <= content.Bounds.Width + 1 && codeScroll.Extent.Width > codeScroll.Viewport.Width,
                    name + " long commands scroll locally without widening the message");
                ushort[] glyphs = code.TextLayout.TextLines.SelectMany(line => line.TextRuns).OfType<ShapedTextRun>()
                    .SelectMany(run => run.GlyphRun.GlyphInfos.Select(info => info.GlyphIndex)).ToArray();
                Check(glyphs.Length > 10 && glyphs.All(glyph => glyph != 0), name + " Chinese code text resolves real glyphs without missing-character boxes");
                var latinRun = code.TextLayout.TextLines.SelectMany(line => line.TextRuns).OfType<ShapedTextRun>()
                    .First(run => run.Text.Span.IndexOf('j') >= 0);
                GlyphTypeface latinFont = latinRun.GlyphRun.GlyphTypeface;
                ushort narrow = latinFont.CharacterToGlyphMap.GetGlyph('i');
                ushort broad = latinFont.CharacterToGlyphMap.GetGlyph('W');
                bool fixedAdvance = latinFont.TryGetHorizontalGlyphAdvance(narrow, out ushort narrowAdvance)
                    && latinFont.TryGetHorizontalGlyphAdvance(broad, out ushort broadAdvance) && narrowAdvance == broadAdvance;
                Check(fixedAdvance, name + " Latin command characters retain equal monospace advances");
                for (int pass = 0; pass < 3; pass++) ScaleChatText();
                Check(body.FontSize == 15 * EffectiveTextScale && code.FontSize == 13 * EffectiveTextScale,
                    name + " repeated generated-message sizing keeps stable font baselines");
                copy.BringIntoView();
                await SettleReadingAsync();
                Point copyPosition = copy.TranslatePoint(default, AiMessagesScroll) ?? default;
                Check(copyPosition.Y >= -1 && copyPosition.Y + copy.Bounds.Height <= AiMessagesScroll.Bounds.Height + 1,
                    name + " the code action can be reached above the fixed composer");
                await Capture("reading-code-" + name);
                string complete = content.Text;
                content.Text = "```text\n  保留未结束代码的尾随空白  \n";
                await SettleReadingAsync();
                var unfinished = content.GetVisualDescendants().OfType<SelectableTextBlock>().Single();
                Check(unfinished.Text == "  保留未结束代码的尾随空白  \n" && unfinished.FontSize == 13 * EffectiveTextScale,
                    name + " streaming unfinished fences preserve code bytes and active text size");
                content.Text = complete;
                await SettleReadingAsync();
                samples.Add(new { theme = name, proseSize = body.FontSize, proseLeading = double.IsFinite(body.LineHeight) ? body.LineHeight : 0,
                    roleSize = role.FontSize, codeSize = code.FontSize, codeLeading = double.IsFinite(code.LineHeight) ? code.LineHeight : 0,
                    codeGlyphs = glyphs.Length, latinFont = latinFont.FamilyName, fixedAdvance,
                    copy = new { copy.Bounds.Width, copy.Bounds.Height } });

                ShowPage("tasks", remember: false);
                await SettleReadingAsync();
                await Capture("reading-tasks-" + name);
                var rows = localTasks.Children.OfType<Border>().ToArray();
                Check(rows.Length > 0, name + " the task reading fixture contains real task entries");
                Check(rows.All(row => row.Bounds.Width <= localTasks.Bounds.Width + 1), name + " task content stays inside its reading column");
                Check(rows.SelectMany(row => row.GetVisualDescendants().OfType<TextBlock>()).All(label => label.FontSize >= 12 * EffectiveTextScale - 0.1),
                    name + " generated task text follows the active text scale");
                Check(rows.SelectMany(row => row.GetVisualDescendants().OfType<TextBlock>()).Where(label => label.Classes.Contains("task-state"))
                    .All(label => label.TextLayout.TextLines.Count == 1), name + " task state labels remain a compact single line");
            }
        }
        finally
        {
            ThemePicker.SelectedIndex = theme;
            AiMessages.ItemsSource = messages;
            AiSettingsPanel.IsVisible = settings;
            AiEmpty.IsVisible = empty;
            AiUndoClearButton.IsVisible = undo;
            AiChatNotice.Text = notice;
            ShowPage(page, remember: false);
            pageHistory.Clear();
            foreach (string entry in history.Reverse()) pageHistory.Push(entry);
            await SettleReadingAsync();
            AiMessagesScroll.Offset = offset;
        }
        File.WriteAllText(Path.Combine(EvidenceRoot, "mobile-reading.json"), JsonSerializer.Serialize(new
        {
            ok = failures.Count == 0, failures, checks, samples, systemTextScale = MobileRuntime.SystemTextScale,
            desktopHosted = !OperatingSystem.IsAndroid(), physicalDeviceVerified = false, talkBackVerified = false
        }));
        if (failures.Count > 0) throw new IOException("Mobile reading refinement failed: " + string.Join("; ", failures));
    }

    private static async Task SettleReadingAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        await Task.Delay(100);
    }
}
