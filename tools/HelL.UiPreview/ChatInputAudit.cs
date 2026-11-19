using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HelL;
using HelL.Pages;
using HelL.Services;

internal static class ChatInputAudit
{
    private static TextBox NewEditor() => (TextBox)Activator.CreateInstance(
        typeof(AiPage).Assembly.GetType("HelL.Pages.ChatInput", true)!)!;

    private static void OnSubmit(TextBox editor, Action<string> callback) =>
        editor.GetType().GetEvent("Submitted")!.AddEventHandler(editor, callback);

    internal static async Task Run(MainWindow window, string folder)
    {
        var editor = NewEditor();
        var layer = Dlg.Panel("中文输入与换行", editor, 520);
        await MaterialControlsPreview.Layout(window);
        editor.Text = string.Join("\n", Enumerable.Range(1, 30).Select(i => $"第 {i} 行：中文与 English 混排"));
        editor.CaretIndex = editor.Text.Length;
        await MaterialControlsPreview.Layout(window);
        RichUiAudit.Check(Math.Abs(editor.ActualHeight - 140) < 1 && editor.ExtentHeight > editor.ViewportHeight,
            "long input reaches height limit and scrolls");
        editor.Clear();
        await MaterialControlsPreview.Layout(window);
        RichUiAudit.Check(Math.Abs(editor.ActualHeight - 48) < 1, "clearing long input restores compact height");
        editor.Text = "第一行\n第二行\n";
        await MaterialControlsPreview.Layout(window);
        var lastLine = editor.GetRectFromCharacterIndex(editor.Text.Length, true);
        RichUiAudit.Check(!lastLine.IsEmpty && lastLine.Bottom <= editor.ActualHeight - editor.Padding.Bottom + 1,
            "trailing empty line remains visible");
        double normalHeight = editor.ActualHeight;
        editor.FontSize = 22;
        await MaterialControlsPreview.Layout(window);
        RichUiAudit.Check(editor.ActualHeight > normalHeight, "input remeasures after font size changes");
        RichUiAudit.Shot(window, folder, "chat-large-input");
        int submitted = 0;
        OnSubmit(editor, _ => submitted++);
        Compose(editor, TextCompositionManager.TextInputStartEvent);
        Compose(editor, TextCompositionManager.TextInputEvent, handled: true);
        editor.RaiseEvent(Key(editor, System.Windows.Input.Key.Return));
        RichUiAudit.Check(submitted == 1, "handled IME completion restores Enter submission");
        layer.Dismiss();
        await MaterialControlsPreview.Layout(window);
        await EditMessage(window, folder);
    }

    private static async Task EditMessage(MainWindow window, string folder)
    {
        var bubbleType = typeof(AiPage).Assembly.GetType("HelL.Pages.Bubble", true)!;
        var bubble = (FrameworkElement)Activator.CreateInstance(bubbleType, "user",
            "中文长消息编辑检查。" + new string('中', 80), new Action<string>(_ => { }))!;
        int submitted = 0;
        string received = "";
        Action<object, string> edited = (_, text) => { submitted++; received = text; };
        bubbleType.GetProperty("Edit", RichUiAudit.Hidden)!.SetValue(bubble, edited);
        var layer = Dlg.Panel("编辑消息", bubble, 450);
        await MaterialControlsPreview.Layout(window);
        RichUiAudit.Invoke(bubble, "BeginEdit");
        await MaterialControlsPreview.Layout(window);
        var editor = RichUiAudit.Field<TextBox>(bubble, "_editBox");
        RichUiAudit.Check(editor.GetType().Name == "ChatInput", "message editor uses guarded multiline input");
        RichUiAudit.Check(editor.ActualHeight > 48 && editor.ActualWidth <= layer.Card.ActualWidth - 48,
            "long message editor wraps within narrow dialog");
        RichUiAudit.Shot(window, folder, "chat-edit-message");
        foreach (var button in RichUiAudit.Find<Button>(bubble).Where(button => button.IsVisible))
        {
            Point point = button.TranslatePoint(new Point(), bubble);
            RichUiAudit.Check(point.X >= -1 && point.X + button.ActualWidth <= bubble.ActualWidth + 1,
                "message edit action fits narrow width");
        }
        editor.Text = "  修改后的中文消息  ";
        Compose(editor, TextCompositionManager.TextInputStartEvent);
        editor.RaiseEvent(Key(editor, System.Windows.Input.Key.Return));
        RichUiAudit.Check(submitted == 0 && editor.IsVisible, "IME candidate Enter does not submit edited message");
        Compose(editor, TextCompositionManager.TextInputEvent);
        editor.RaiseEvent(Key(editor, System.Windows.Input.Key.Return));
        RichUiAudit.Check(submitted == 1 && received == "修改后的中文消息" && !editor.IsVisible,
            "completed edit submits once and closes editor");
        RichUiAudit.Invoke(bubble, "BeginEdit");
        editor.Text = "   ";
        editor.RaiseEvent(Key(editor, System.Windows.Input.Key.Return));
        RichUiAudit.Check(submitted == 1 && editor.IsVisible, "empty edited message stays editable");
        RichUiAudit.Invoke(bubble, "CancelEdit");
        RichUiAudit.Check(!editor.IsVisible, "cancel restores message view");
        layer.Dismiss();
        await MaterialControlsPreview.Layout(window);
    }

    private static KeyEventArgs Key(TextBox editor, Key key) =>
        new(Keyboard.PrimaryDevice, PresentationSource.FromVisual(editor), 0, key)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent };

    private static void Compose(TextBox editor, RoutedEvent routedEvent, bool handled = false) =>
        editor.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice,
            new TextComposition(InputManager.Current, editor, "")) { RoutedEvent = routedEvent, Handled = handled });

    internal static void ShowProbe()
    {
        var editor = NewEditor();
        editor.Tag = "在此输入中文；Enter 只计数，不连接模型";
        var count = Ui.Txt("本地提交：0 次", 16, true);
        var preview = Ui.Muted("尚未提交").Wrap();
        int submitted = 0;
        OnSubmit(editor, text =>
        {
            count.Text = $"本地提交：{++submitted} 次";
            preview.Text = text;
            Console.WriteLine($"[INPUT PROBE] submitted={submitted} chars={text.Length} lines={text.Split('\n').Length}");
        });
        var layer = Dlg.Panel("输入验收 · 仅在本机计数", Ui.V(12,
            Ui.Muted("检查中文候选、Shift+Enter 换行、Enter 提交。这里不会调用 AI。"),
            editor, count, preview), 620);
        layer.Root.Loaded += (_, _) => editor.Focus();
    }
}
