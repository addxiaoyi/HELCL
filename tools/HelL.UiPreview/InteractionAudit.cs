using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HelL;
using HelL.Models;
using HelL.Pages;
using HelL.Services;

internal static class InteractionAudit
{
    private static int _checks, _failed;
    internal static void Check(bool valid, string message)
    {
        _checks++; if (!valid) _failed++;
        Console.WriteLine((valid ? "[PASS] " : "[FAIL] ") + message);
    }

    internal static async Task Run(MainWindow window, string folder)
    {
        Directory.CreateDirectory(folder); Motion.Enabled = false;
        window.Title = "HelL · 动态内容与安装验收";
        await Chat(window, folder);
        await InstallInteractionAudit.Run(window, folder);
        Console.WriteLine($"[INTERACTION AUDIT] {_checks - _failed}/{_checks} passed; failures={_failed}");
        if (_failed > 0) throw new InvalidOperationException($"{_failed} interaction checks failed.");
    }

    internal static bool Inside(FrameworkElement element, FrameworkElement surface)
    {
        if (!element.IsVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0) return false;
        Point point = element.TranslatePoint(new Point(), surface);
        return point.X >= -1 && point.Y >= -1 && point.X + element.ActualWidth <= surface.ActualWidth + 1
            && point.Y + element.ActualHeight <= surface.ActualHeight + 1;
    }

    private static async Task Chat(MainWindow window, string folder)
    {
        window.Navigate("ai", instant: true); await Program.WaitForPage(window, "ai");
        var page = RichUiAudit.Find<AiPage>(window).Single();
        var transcript = new AiStoreDto { ActiveId = "flow", Chats = new() { new AiChatDto { Id = "flow", Title = "流式排版验收" } } };
        foreach (bool dark in new[] { false, true })
        {
            App.ApplyTheme(dark); window.Width = 800; window.Height = 620;
            RichUiAudit.Invoke(page, "SelfTestLoad", transcript);
            RichUiAudit.Invoke(page, "SelfTestUser", "请检查这个世界的性能，并解释配置改动。");
            var chunks = new[] { "## 正在分析\n\n中文和 English 混排，保持正在输出的位置。\n\n```json\n{\n",
                "  \"世界\": \"青山生存\",\n  \"renderDistance\": 12\n}",
                "\n```\n\n| 检查项 | 建议 |\n|---|---|\n| Java | 使用 21 |\n| 光影 | 检查兼容性 |\n\n" };
            foreach (string chunk in chunks)
            {
                page.OnEvent(new BridgeEvent { Event = "ai.delta", Text = chunk });
                await MaterialControlsPreview.Layout(window);
            }
            RichUiAudit.Shot(window, folder, "ai-stream-" + dark);
            Check(Inside(RichUiAudit.Field<TextBox>(page, "_input"), page), "streaming keeps composer visible");
            var scroll = RichUiAudit.Field<SmoothScroll>(page, "_scroll");
            for (int i = 0; i < 14; i++) page.OnEvent(new BridgeEvent { Event = "ai.delta", Text = $"\n\n第 {i + 1} 条建议：先备份，再检查模组兼容性与日志。" });
            await MaterialControlsPreview.Layout(window);
            scroll.ScrollToTop(); await MaterialControlsPreview.Layout(window);
            double before = scroll.VerticalOffset;
            page.OnEvent(new BridgeEvent { Event = "ai.delta", Text = "\n\n这段新内容不能把正在阅读历史的用户强行拉到底部。" });
            await MaterialControlsPreview.Layout(window);
            Check(Math.Abs(scroll.VerticalOffset - before) < 2, "streaming respects a reader scrolled to earlier text");
            scroll.ScrollToEnd(); await MaterialControlsPreview.Layout(window);
            page.OnEvent(new BridgeEvent { Event = "ai.delta", Text = "\n\n回到底部后，新回复应该继续跟随显示。" });
            await MaterialControlsPreview.Layout(window);
            Check(scroll.ScrollableHeight - scroll.VerticalOffset < 2, "streaming resumes following after returning to bottom");
            page.OnEvent(new BridgeEvent { Event = "ai.done" });
            await MaterialControlsPreview.Layout(window);
            RichUiAudit.Invoke(page, "SelfTestScrollEnd");
            RichUiAudit.Shot(window, folder, "ai-stream-done-" + dark);
            RichUiAudit.Invoke(page, "SelfTestUser", "请展示完整修改预览，等待我确认。");
            using var payload = JsonDocument.Parse(JsonSerializer.Serialize(new { allow_always = true,
                reason = string.Concat(Enumerable.Repeat("将检查当前实例的配置，保留存档并在执行前创建检查点。", 8)),
                preview = new { head = "D:\\Minecraft\\中文长名称实例\\config\\performance.json",
                    lines = Enumerable.Range(1, 36).Select(i => "+ 配置字段_" + i + ": " + new string('中', 40)).ToArray() } }));
            page.OnEvent(new BridgeEvent { Event = "ai.confirm", Name = "write_mod_config", Label = "更新当前实例的中文长名称性能配置文件", Payload = payload.RootElement.Clone() });
            await MaterialControlsPreview.Layout(window);
            RichUiAudit.Shot(window, folder, "ai-confirm-" + dark);
            var dock = RichUiAudit.Field<SPanel>(page, "_dock");
            foreach (var button in RichUiAudit.Find<Button>(dock).Where(button => button.IsVisible))
                Check(Inside(button, page), "confirmation action remains visible · " + button.Content);
            Check(Inside(RichUiAudit.Field<TextBox>(page, "_input"), page), "long confirmation keeps input within viewport");
            CustomizationPreview.Named(dock, "拒绝").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(dock.Children.Count == 0, "rejecting the tool request releases the confirmation dock");
            page.OnEvent(new BridgeEvent { Event = "ai.done" });
            var paths = new[] { "chat-code-False-800.png", "resource-detail-False.png", "saves-True.png", "font-default-125.png" }
                .Select(name => Path.GetFullPath(Path.Combine(".design/rich-ui-2026-11-10", name))).ToArray();
            foreach (string path in paths) RichUiAudit.Invoke(page, "AddImage", path);
            await MaterialControlsPreview.Until(() => !RichUiAudit.Field<bool>(page, "_visionChecking"), "vision status settles once for attachment batch");
            await MaterialControlsPreview.Layout(window);
            RichUiAudit.Shot(window, folder, "ai-attachments-" + dark);
            Check(RichUiAudit.Find<LocalPicture>(page).Count(picture => picture.IsVisible && picture.Ready) == 4, "four attachments have actual local thumbnail previews");
            var attachmentButtons = RichUiAudit.Find<Button>(page).Where(button => System.Windows.Automation.AutomationProperties.GetName(button).StartsWith("预览附件 ")).ToArray();
            Check(attachmentButtons.Length == 4, "each attachment has a keyboard-accessible preview");
            if (attachmentButtons.Length > 0)
            {
                attachmentButtons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await MaterialControlsPreview.Layout(window);
                await MaterialControlsPreview.Until(() => RichUiAudit.Find<LocalPicture>(Dlg.Host!).Any(picture => picture.Ready), "attachment opens a real full-image preview");
                RichUiAudit.Shot(window, folder, "ai-attachment-expanded-" + dark);
                CustomizationPreview.Named(Dlg.Host!, "关闭").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await MaterialControlsPreview.Layout(window);
                var remove = RichUiAudit.Find<Button>(page).First(button => System.Windows.Automation.AutomationProperties.GetName(button).StartsWith("移除附件 "));
                remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(RichUiAudit.Field<List<string>>(page, "_pendingImages").Count == 3 && paths.All(File.Exists), "removing an attachment preserves the source image files");
            }
            RichUiAudit.Field<List<string>>(page, "_pendingImages").Clear();
            RichUiAudit.Invoke(page, "SyncImageChip");
        }
    }
}
