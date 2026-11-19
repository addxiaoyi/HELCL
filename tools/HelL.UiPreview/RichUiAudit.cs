using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HelL;
using HelL.Models;
using HelL.Pages;
using HelL.Services;

internal static class RichUiAudit
{
    private static int _checks, _failed;
    internal const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    internal static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Hidden)!.GetValue(owner)!;
    internal static object? Invoke(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, Hidden)!.Invoke(owner, args);
    internal static IEnumerable<T> Find<T>(DependencyObject root) => CustomizationPreview.Walk(root).OfType<T>();
    internal static void Check(bool valid, string label)
    {
        _checks++; if (!valid) _failed++;
        Console.WriteLine((valid ? "[PASS] " : "[FAIL] ") + label);
    }

    internal static async Task Run(MainWindow window, string folder, bool live)
    {
        Directory.CreateDirectory(folder); Motion.Enabled = false;
        window.Title = "HelL · 长对话与弹窗检查";
        await Chat(window, folder);
        await ChatInputAudit.Run(window, folder);
        await FontsAtScale(window, folder);
        await RichDialogAudit.Run(window, folder);
        Typography.Apply("");
        Console.WriteLine($"[RICH UI] {_checks - _failed}/{_checks} passed; failures={_failed}");
        if (live) { window.Navigate("ai", instant: true); ChatInputAudit.ShowProbe(); window.Activate(); }
        else if (_failed > 0) throw new InvalidOperationException($"{_failed} rich UI checks failed; see [FAIL] lines.");
    }

    private static async Task Chat(MainWindow window, string folder)
    {
        window.Navigate("ai", instant: true); await Program.WaitForPage(window, "ai");
        var page = Find<AiPage>(window).Single();
        var messages = new List<AiMsgDto>();
        for (int i = 0; i < 20; i++) messages.Add(new AiMsgDto { Role = i % 2 == 0 ? "user" : "assistant", Content = $"第 {i + 1} 条排版检查：生存世界中启用光影后出现卡顿，请检查 Java、模组兼容性和内存。ABCxyz 0123456789。" });
        const string code = "java -Xmx4096M -Dfile.encoding=UTF-8 -Dlauncher.instance=\"青山生存_中文目录_";
        string command = code + new string('x', 220) + "\"\n// 中文注释：检查路径、缩进和最后一行\nSystem.out.println(\"完成 ✓\");";
        messages.Add(new AiMsgDto { Role = "user", Content = "请给我完整的排查步骤，并保留下面代码的缩进。" });
        messages.Add(new AiMsgDto { Role = "assistant", Content = "## 中文与 English 混排\n\n**先检查版本兼容性**。光影需要匹配的加载器和渲染模组，保留原有存档。\n\n```java\n" + command + "\n```\n\n| 项目 | 当前值 | 建议 |\n|---|---|---|\n| Java | 21.0.8 | 保留 |\n| 路径 | `D:\\Minecraft\\青山生存` | 检查权限 |\n\n1. 先备份实例。\n2. 再查看日志中的第一条异常。\n\n" + string.Concat(Enumerable.Repeat("正文换行检查：中文、English、数字 0123456789，标点（）【】。", 14)) });
        Invoke(page, "SelfTestLoad", new AiStoreDto { ActiveId = "layout", Chats = new() { new AiChatDto { Id = "layout", Title = "长对话排版检查", Messages = messages } } });
        var scroll = Field<SmoothScroll>(page, "_scroll");
        var input = Field<TextBox>(page, "_input");
        var placeholder = Field<TextBlock>(page, "_placeholder");
        foreach (bool dark in new[] { false, true })
        foreach (double width in new[] { 1180d, 800d })
        {
            App.ApplyTheme(dark); window.Width = width; window.Height = 740;
            await MaterialControlsPreview.Layout(window);
            var codeBox = Find<TextBox>(page).Single(box => box.Text == command);
            var codeScroll = (ScrollViewer)codeBox.Template.FindName("PART_ContentHost", codeBox);
            codeScroll.ScrollToLeftEnd(); codeBox.BringIntoView(); await MaterialControlsPreview.Layout(window);
            Shot(window, folder, $"chat-code-{dark}-{width}");
            Check(codeScroll.ScrollableWidth > 0 && codeScroll.ComputedHorizontalScrollBarVisibility == Visibility.Visible, "long code has horizontal scrolling · " + width);
            Check(codeBox.ActualWidth <= scroll.ViewportWidth, "code stays within conversation viewport · " + width);
            codeBox.SelectAll();
            Check(codeBox.SelectedText == command, "code selection preserves complete unwrapped text");
            codeBox.Select(0, 0);
            var md = Find<RichTextBox>(page).Last();
            Check(md.Document.FontFamily.Source == ((FontFamily)Application.Current.FindResource("F.Ui")).Source, "assistant document follows UI font");
            codeScroll.ScrollToRightEnd(); await MaterialControlsPreview.Layout(window);
            Shot(window, folder, $"chat-code-end-{dark}-{width}");
            scroll.ScrollToEnd(); await MaterialControlsPreview.Layout(window);
            Shot(window, folder, $"chat-bottom-{dark}-{width}");
            Check(input.ActualHeight >= 48 && input.ActualHeight <= 140, "composer keeps height bounds");
        }
        window.Width = 1180; await MaterialControlsPreview.Layout(window);
        input.Text = string.Concat(Enumerable.Repeat("中文输入宽度变化需要重新计算行高。", 7));
        await MaterialControlsPreview.Layout(window);
        double wideHeight = input.ActualHeight;
        window.Width = 800; await MaterialControlsPreview.Layout(window);
        Check(input.ActualHeight > wideHeight || wideHeight >= 139, $"composer grows after window narrows: {wideHeight:F1} -> {input.ActualHeight:F1}, lines={input.LineCount}");
        Shot(window, folder, "chat-chinese-input");
        input.Clear(); await MaterialControlsPreview.Layout(window);
        string hint = placeholder.Text;
        var empty = TypographyChecks.GlyphBounds(placeholder, page);
        input.Text = hint; await MaterialControlsPreview.Layout(window);
        var typed = TypographyChecks.GlyphBounds(input, page);
        Check(Math.Abs(empty.Top - typed.Top) <= 1, $"chat placeholder baseline: {typed.Top - empty.Top:F2} DIP");
        input.Clear();
        await CompositionCheck(window);
        Typography.Apply("Arial");
        var fontTask = Typography.Show();
        await MaterialControlsPreview.Layout(window);
        var sample = Find<TextBlock>(Dlg.Host!).Single(text => text.Text == "继续你的冒险");
        Check(sample.FontFamily.Source.StartsWith("Arial", StringComparison.Ordinal), "font preview starts with selected font");
        CustomizationPreview.Named(Dlg.Host!, "取消").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await fontTask;
        Typography.Apply("");
    }

    private static async Task CompositionCheck(MainWindow window)
    {
        var inputType = typeof(AiPage).Assembly.GetType("HelL.Pages.ChatInput", true)!;
        var editor = (TextBox)Activator.CreateInstance(inputType)!;
        int sent = 0;
        Action<string> submitted = _ => sent++;
        inputType.GetEvent("Submitted")!.AddEventHandler(editor, submitted);
        var layer = Dlg.Panel("中文输入保护检查", editor);
        await MaterialControlsPreview.Layout(window);
        editor.Text = "你好，世界";
        var composition = new TextComposition(InputManager.Current, editor, "");
        var textEvent = new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition);
        KeyEventArgs Enter() => new(Keyboard.PrimaryDevice, PresentationSource.FromVisual(editor), 0, Key.Return)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        Invoke(editor, "OnComposition", editor, textEvent);
        Invoke(editor, "OnPreviewKeyDown", Enter());
        Check(sent == 0, "empty IME composition cannot submit Chinese input");
        Invoke(editor, "OnCompositionEnd", editor, textEvent);
        Invoke(editor, "OnPreviewKeyDown", Enter());
        Check(sent == 1, "Enter submits after Chinese composition ends");
        layer.Dismiss(); await MaterialControlsPreview.Layout(window);
    }

    private static async Task FontsAtScale(MainWindow window, string folder)
    {
        var nativeDpi = VisualTreeHelper.GetDpi(window);
        var setDpi = typeof(VisualTreeHelper).GetMethod("SetRootDpi", BindingFlags.Public | BindingFlags.Static)
            ?? throw new MissingMethodException("WPF SetRootDpi is unavailable");
        Console.WriteLine($"[SCALE] Native display {nativeDpi.PixelsPerInchX} DPI. 120/192 below are isolated WPF layout/render overrides, not Windows display settings.");
        window.Navigate("settings", instant: true); await Program.WaitForPage(window, "settings");
        window.Width = 800; window.Height = 740;
        try
        {
            foreach (string font in new[] { "", "Arial", "Algerian", "Microsoft YaHei UI", "SimSun", "missing-font-for-audit" })
            foreach (double scale in new[] { 1.25, 2d })
            {
                Typography.Apply(font);
                setDpi.Invoke(null, new object[] { window, new DpiScale(scale, scale) });
                App.ApplyTheme(scale == 2);
                var label = Ui.Txt("中文回退：方块与世界 Agjp 0123", 14);
                var bold = Ui.Txt("粗体与标点：（你好）【Minecraft】", 18, true);
                var input = Ui.Input("输入名称", "中文输入 Agjp 0123");
                var combo = Ui.Combo(new[] { "中文版本 · Minecraft 1.21.1", "Fabric 生存世界" });
                var layer = Dlg.Panel("字体与缩放", Ui.V(12, Ui.Small(font.Length == 0 ? "默认字体" : font), label, bold, input, combo, Ui.H(8, Ui.Btn("确认安装", BtnKind.Primary, glyph: "\ue896"), Ui.Btn("取消"))), 640);
                await MaterialControlsPreview.Layout(window);
                Check(Math.Abs(VisualTreeHelper.GetDpi(label).DpiScaleX - scale) < .01, "layout uses requested scale " + scale);
                foreach (var text in new[] { label, bold })
                {
                    Rect ink = TypographyAudit.Ink(text);
                    Check(!ink.IsEmpty && ink.Top >= -1 && ink.Bottom <= text.ActualHeight + 1, $"glyph ink fits line box · {font} · {scale} · {text.FontSize}");
                    foreach (var run in Glyphs(text))
                    {
                        Check(run.GlyphIndices.All(index => index != 0), "no missing glyphs · " + font);
                        Console.WriteLine("[FONT] requested=" + font + " actual=" + string.Join("/", run.GlyphTypeface.FamilyNames.Values.Distinct()) + " bold=" + text.FontWeight);
                    }
                }
                string name = (font.Length == 0 ? "default" : font.Replace(' ', '-')) + "-" + (scale * 100).ToString("0");
                Shot(window, folder, "font-" + name);
                combo.IsDropDownOpen = true; await MaterialControlsPreview.Layout(window);
                var popup = (System.Windows.Controls.Primitives.Popup)combo.Template.FindName("PART_Popup", combo);
                var popupRoot = PresentationSource.FromVisual(popup.Child)?.RootVisual ?? throw new InvalidOperationException("Popup has no presentation root");
                setDpi.Invoke(null, new object[] { popupRoot, new DpiScale(scale, scale) });
                await MaterialControlsPreview.Layout(window);
                Check(Math.Abs(VisualTreeHelper.GetDpi(popup.Child).DpiScaleX - scale) < .01, "popup layout uses requested scale " + scale);
                TypographyAudit.Capture((FrameworkElement)popup.Child, Path.Combine(folder, "font-menu-" + name + ".png"));
                Console.WriteLine("[POPUP DPI] " + name + " actual=" + VisualTreeHelper.GetDpi(popup.Child).PixelsPerInchX);
                combo.IsDropDownOpen = false; layer.Dismiss(); await MaterialControlsPreview.Layout(window);
            }
        }
        finally { setDpi.Invoke(null, new object[] { window, nativeDpi }); Typography.Apply(""); }
    }

    private static IEnumerable<GlyphRun> Glyphs(Visual visual)
    {
        IEnumerable<GlyphRun> Walk(Drawing drawing)
        {
            if (drawing is GlyphRunDrawing glyph) yield return glyph.GlyphRun;
            if (drawing is DrawingGroup group) foreach (var child in group.Children) foreach (var run in Walk(child)) yield return run;
        }
        var drawing = VisualTreeHelper.GetDrawing(visual);
        return drawing == null ? Enumerable.Empty<GlyphRun>() : Walk(drawing);
    }

    internal static void Shot(MainWindow window, string folder, string name) => TypographyAudit.Capture(window, Path.Combine(folder, name + ".png"));
}
