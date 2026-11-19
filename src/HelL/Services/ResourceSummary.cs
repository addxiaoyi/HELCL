using System;
using System.Windows;
using System.Windows.Controls;
using HelL.Models;

namespace HelL.Services;

public sealed class ResourceSummary : ContentControl
{
    private CatalogItem _item;
    private readonly TextBlock _text = Ui.Muted("").Wrap();
    private readonly TextBlock _state = Ui.Txt("", 11, false, "B.InkMuted");
    private readonly Button _retry;
    private readonly Button _original;
    private readonly ProgressBar _progress = new ProgressBar { Height = 2, IsIndeterminate = true, Visibility = Visibility.Collapsed };
    private bool _showOriginal;
    public string State => _state.Text;
    public bool ShowingOriginal => _showOriginal;

    public ResourceSummary(CatalogItem item, Action retry, bool compact = true)
    {
        _item = item;
        _retry = Ui.Btn(I18n.L("重试翻译"), BtnKind.Chip, (_, _) => retry());
        _original = Ui.Btn(I18n.L("原文"), BtnKind.Chip, (_, _) => { _showOriginal = !_showOriginal; Render(); });
        _retry.FontSize = _original.FontSize = 11;
        _retry.MinHeight = _original.MinHeight = 22;
        _retry.Padding = _original.Padding = new Thickness(7, 1.5, 7, 2);
        if (compact) { _text.MaxHeight = 42; _text.TextTrimming = TextTrimming.CharacterEllipsis; }
        var actions = Ui.H(6, _state.VCenter(), _original, _retry); actions.Wrap = true;
        Content = Ui.V(4, _text, _progress, actions);
        Apply(item);
    }

    public void Begin()
    {
        _progress.Visibility = Visibility.Visible;
        _retry.Visibility = Visibility.Collapsed;
        _state.Text = I18n.L("本地翻译中…");
    }

    public void Apply(CatalogItem item, string error = "")
    {
        _item = item;
        _progress.Visibility = Visibility.Collapsed;
        bool pending = item.Localization == "英文简介待翻译" || item.Description.Length == 0 && item.OriginalDescription.Length > 0
            || item.ChineseName.Length == 0 && !HelL.Core.Launcher.ResourceNames.ContainsChinese(item.Name);
        _retry.Visibility = pending && I18n.Current == "zh_CN" ? Visibility.Visible : Visibility.Collapsed;
        _state.Text = error.Length > 0 && pending ? I18n.L("翻译未完成") : I18n.L(item.Localization);
        _state.ToolTip = error;
        Render();
    }

    private void Render()
    {
        bool hasOriginal = _item.OriginalDescription.Length > 0 && _item.OriginalDescription != _item.Description;
        _original.Visibility = hasOriginal ? Visibility.Visible : Visibility.Collapsed;
        _original.Content = I18n.L(_showOriginal ? "中文" : "原文");
        System.Windows.Automation.AutomationProperties.SetName(_original, I18n.L(_showOriginal ? "中文" : "原文"));
        _text.Text = _showOriginal && hasOriginal ? _item.OriginalDescription : _item.Description.Length > 0 ? _item.Description :
            _item.OriginalDescription.Length > 0 ? _item.OriginalDescription : I18n.L("作者暂未提供简介");
    }
}
