using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HelL.Services;

namespace HelL.Pages;

internal sealed class DocumentComparison : ContentControl
{
    private sealed class Reply { public string Text { get; set; } = ""; public bool Manual { get; set; } }
    private readonly string _key, _original;
    private readonly int _index;
    private string _translation;
    private readonly Action<string, bool> _saved;
    private readonly Ai.MdView _chinese = new();
    private readonly TextBlock _label = Ui.Small("");
    private readonly Button _reset;

    internal DocumentComparison(string key, int index, string original, string translation, bool manual, Action<string, bool> saved)
    {
        _key = key; _index = index; _original = original; _translation = translation; _saved = saved;
        var source = new Ai.MdView(); source.SetMarkdown(original);
        var edit = Ui.Btn(I18n.L("修正本段"), BtnKind.Ghost, (_, _) => PageBase.Run(Edit));
        _reset = Ui.Btn(I18n.L("恢复自动译文"), BtnKind.Ghost, (_, _) => PageBase.Run(Reset));
        var actions = Ui.H(6, edit, _reset); actions.Wrap = true;
        var columns = new AdaptiveColumns(Ui.V(6, Ui.Small(I18n.L("原文")), source), Ui.V(6, _label, _chinese, actions),
            fixedWidth: 1000, breakpoint: 680, gap: 20, fixedRatio: 0.48);
        Content = Ui.V(8, Ui.Txt(I18n.L("第 {0} 段", index + 1), 13, true), columns);
        Apply(translation, manual);
    }

    private void Apply(string text, bool manual)
    {
        _translation = text; _chinese.SetMarkdown(text.Length == 0 ? I18n.L("本段尚未翻译，可手动填写。") : text);
        _label.Text = I18n.L(manual ? "中文 · 手动修订" : "中文");
        _reset.Visibility = manual ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task Edit()
    {
        string expected = _translation;
        var input = Ui.Input("", _translation); input.AcceptsReturn = true; input.TextWrapping = TextWrapping.Wrap;
        input.MinHeight = 160; input.MaxHeight = 260; input.MaxLength = 12000; input.VerticalContentAlignment = VerticalAlignment.Top;
        input.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        System.Windows.Automation.AutomationProperties.SetName(input, "段落中文译文");
        var original = Ui.Muted(_original).Wrap();
        var source = Ui.Scroll(original); source.MaxHeight = 150;
        await Dlg.EditAsync(I18n.L("修正第 {0} 段译文", _index + 1), Ui.V(10, Ui.Small(I18n.L("原文")), source,
            Ui.Small(I18n.L("中文译文")), input, Ui.Small(I18n.L("手动修订独立保存，重新翻译或清理缓存不会覆盖。" )).Wrap()), async () =>
        {
            var reply = await AppServices.Client.CallAsync<Reply>("save_catalog_translation", new { key = _key, index = _index,
                original = _original, text = input.Text, expected }) ?? throw new InvalidOperationException(I18n.L("没有收到保存结果。"));
            Apply(reply.Text, reply.Manual); _saved(reply.Text, reply.Manual);
        }, width: 720);
    }

    private async Task Reset()
    {
        if (!await Dlg.Confirm(I18n.L("恢复自动译文"), I18n.L("将移除此段的手动修订，使用已有机器译文；没有缓存时显示未翻译。"))) return;
        var reply = await AppServices.Client.CallAsync<Reply>("save_catalog_translation", new { key = _key, index = _index,
            original = _original, text = "", expected = _translation, reset = true }) ?? throw new InvalidOperationException(I18n.L("没有收到保存结果。"));
        Apply(reply.Text, reply.Manual); _saved(reply.Text, reply.Manual);
    }
}
