using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Input;
using HelL;
using HelL.Services;

internal static class TypographyChecks
{
    private static readonly List<string> Failures = new();
    private static int _checks;

    internal static async Task Run(MainWindow window)
    {
        Motion.Enabled = false;
        foreach (bool dark in new[] { false, true })
        {
            App.ApplyTheme(dark);
            var glyphs = new[] { "\ue713", "\ue896", "\ue768", "\ue711", "\ue80f" }
                .SelectMany(code => new[] { 12d, 14d, 20d }.Select(size => Ui.Glyph(code, size))).ToArray();
            var plain = Ui.Btn("按钮 ABC", BtnKind.Chip);
            var icon = Ui.Btn("按钮 ABC", BtnKind.Chip, glyph: "\ue896");
            var input = Ui.Input("输入名称 ABC 123", "输入名称 ABC 123");
            var editable = Ui.Combo(new[] { "输入名称 ABC 123" }, editable: true);
            var combo = Ui.Combo(new[] { "输入名称 ABC 123" });
            var body = Ui.V(8, Ui.H(12, glyphs), Ui.H(8, plain, icon), input, combo, editable);
            var layer = Dlg.Panel("排版回归", body, 640);
            await MaterialControlsPreview.Layout(window);
            Check(TextOptions.GetTextFormattingMode(input) == TextFormattingMode.Display, "input uses display metrics");
            foreach (var glyph in glyphs)
            {
                Rect ink = TypographyAudit.Ink(glyph);
                double offset = ink.Top + ink.Height / 2 - glyph.ActualHeight / 2;
                Check(Math.Abs(offset) <= 1, $"icon U+{(int)glyph.Text[0]:X4} at {glyph.FontSize} centered: {offset:F2} DIP");
                Check(glyph.FontWeight == FontWeights.Normal, "icon strokes stay regular");
            }
            foreach (Button button in new[] { plain, icon })
            {
                button.FontSize = 17;
                await MaterialControlsPreview.Layout(window);
                var label = CustomizationPreview.Walk(button).OfType<TextBlock>().Single(text => text.Text == "按钮 ABC");
                Check(label.FontSize == 17, "button label follows changed size: " + (button == plain ? "plain" : "icon"));
            }
            var populated = GlyphBounds(input, input);
            input.Clear();
            await MaterialControlsPreview.Layout(window);
            var placeholder = (TextBlock)input.Template.FindName("Ph", input);
            var empty = GlyphBounds(placeholder, input);
            Check(Math.Abs(populated.Left - empty.Left) <= 2, $"placeholder/input horizontal jump: {populated.Left - empty.Left:F2} DIP");
            Check(Math.Abs(populated.Top - empty.Top) <= 1, $"placeholder/input vertical jump: {populated.Top - empty.Top:F2} DIP");
            foreach (ComboBox selector in new[] { combo, editable })
            {
                selector.FontSize = 17;
                await MaterialControlsPreview.Layout(window);
                if (selector.IsEditable)
                    Check(((TextBox)selector.Template.FindName("PART_EditableTextBox", selector)).FontSize == 17, "editable selection follows size");
                else
                    Check(CustomizationPreview.Walk(selector).OfType<TextBlock>().Single(text => text.Text == "输入名称 ABC 123").FontSize == 17, "selection follows size");
                selector.IsDropDownOpen = true;
                await MaterialControlsPreview.Layout(window);
                var popup = (Popup)selector.Template.FindName("PART_Popup", selector);
                Check(!HasTextUnderEffect(popup.Child), "dropdown text avoids effect composition");
                ((ComboBoxItem)selector.ItemContainerGenerator.ContainerFromIndex(0)).Focus();
                layer.Root.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                Check(!selector.IsDropDownOpen && Dlg.Host!.Children.Contains(layer.Root), "Escape closes selector before dialog");
            }
            Check(!HasTextUnderEffect(layer.Root), "dialog text avoids effect composition");
            var menu = new ContextMenu { PlacementTarget = plain };
            menu.Items.Add(new MenuItem { Header = "打开目录", Icon = Ui.Glyph("\ue8b7") });
            menu.IsOpen = true;
            await MaterialControlsPreview.Layout(window);
            Check(!HasTextUnderEffect(menu), "menu text avoids effect composition");
            menu.IsOpen = false;
            var tip = new ToolTip { Content = "中文提示 ABC", PlacementTarget = plain, IsOpen = true };
            await MaterialControlsPreview.Layout(window);
            Check(!HasTextUnderEffect(tip), "tooltip text avoids effect composition");
            tip.IsOpen = false;
            layer.Dismiss();
            await MaterialControlsPreview.Layout(window);
            await RowChecks(window);
            window.Toast("下载已完成", "资源文件已通过校验", ToastKind.Success);
            await MaterialControlsPreview.Layout(window);
            Check(!HasTextUnderEffect((DependencyObject)window.FindName("ToastHost")), "toast text avoids effect composition");
            Check(!HasTextUnderEffect((DependencyObject)window.FindName("DockHost")), "download dock text avoids effect composition");
            ((Panel)window.FindName("ToastHost")).Children.Clear();
        }
        Console.WriteLine($"[TYPE CHECK] {_checks - Failures.Count}/{_checks} passed");
        if (Failures.Count > 0) throw new InvalidOperationException(string.Join("\n", Failures));
    }

    private static async Task RowChecks(MainWindow window)
    {
        var toggle = Ui.Switch(true);
        var field = Ui.Field("深色模式", toggle, "立即生效", 130);
        var mode = Ui.Combo(new[] { "自动检测" });
        var compound = Ui.Field("识图", Ui.V(8, mode, Ui.Small("此状态不参与标签对齐")), "检测模型能力", 130);
        var action = Ui.Btn("新建实例", BtnKind.Primary, glyph: "\ue710");
        var header = new PageHeader(Ui.V(4, Ui.H1("我的实例"), Ui.Muted("游戏、模组与世界")), Ui.H(8, action));
        var caption = Ui.Small("中文简介").VCenter();
        var link = Ui.Btn("原文", BtnKind.Ghost);
        var row = Ui.H(6, caption, link); row.Wrap = true;
        var selector = Ui.Combo(new[] { "默认实例" }, width: 160);
        var toolbar = Ui.H(8, selector, Ui.Muted("三行说明\n服务器地址\n局域网地址")); toolbar.Wrap = true;
        var layer = Dlg.Panel("行对齐", Ui.V(18, header, field, compound, row, toolbar), 650);
        layer.Card.MinWidth = 600;
        await MaterialControlsPreview.Layout(window);
        var label = CustomizationPreview.Walk(field).OfType<TextBlock>().Single(text => text.Text == "深色模式");
        double Center(FrameworkElement element, Visual parent) => element.TransformToAncestor(parent).Transform(new Point(0, element.ActualHeight / 2)).Y;
        Check(Math.Abs(Center(label, field) - Center(toggle, field)) <= 1, "field label aligns with switch, excluding hint");
        var compoundLabel = CustomizationPreview.Walk(compound).OfType<TextBlock>().Single(text => text.Text == "识图");
        Check(Math.Abs(Center(compoundLabel, compound) - Center(mode, compound)) <= 1, "compound field label aligns with first editor row");
        Check(action.ActualHeight <= action.MinHeight + 1, "header action retains control height beside two-line heading");
        Check(Math.Abs(Center(caption, row) - Center(link, row)) <= 1, "wrapped action row centers caption and button");
        Check(selector.ActualHeight <= selector.MinHeight + 1, "wrapped selector retains height beside multiline hint");
        compound.Width = 300;
        await MaterialControlsPreview.Layout(window);
        Check(mode.TranslatePoint(new Point(), compound).Y >= compoundLabel.ActualHeight, "narrow field stacks label above editor");
        layer.Dismiss();
        await MaterialControlsPreview.Layout(window);
    }

    private static void Check(bool condition, string message)
    {
        _checks++;
        Console.WriteLine((condition ? "[PASS] " : "[FAIL] ") + message);
        if (!condition) Failures.Add(message);
    }

    internal static Rect GlyphBounds(Visual visual, Visual relativeTo)
    {
        Rect bounds = Rect.Empty;
        foreach (var child in new[] { visual }.Concat(CustomizationPreview.Walk(visual).OfType<Visual>()))
        {
            Rect ink = TypographyAudit.Ink(child);
            if (!ink.IsEmpty) bounds.Union(child.TransformToAncestor(relativeTo).TransformBounds(ink));
        }
        return bounds;
    }

    private static bool HasTextUnderEffect(DependencyObject root) =>
        new[] { root }.Concat(CustomizationPreview.Walk(root)).OfType<UIElement>().Any(element => element.Effect != null &&
            CustomizationPreview.Walk(element).Any(child => child is TextBlock || child is TextBox));
}
