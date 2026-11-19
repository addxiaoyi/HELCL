using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HelL.Models;
using HelL.Services;

namespace HelL.Pages;

public sealed class ResourceDocumentPanel : ContentControl
{
    private sealed class Document
    {
        public string Key { get; set; } = "";
        public string Original { get; set; } = "";
        public string Version { get; set; } = "";
        public List<string> Parts { get; set; } = new();
        public List<string> Translations { get; set; } = new();
        public List<bool> Manual { get; set; } = new();
    }
    private sealed class Translation { public string Text { get; set; } = ""; }
    private readonly CatalogItem _project;
    private readonly string _section;
    private readonly string _version;
    private readonly Func<(string Game, string Loader)> _filters;
    private readonly Ai.MdView _text = new();
    private readonly TextBlock _status = Ui.Small("").Wrap();
    private readonly Button _translate, _toggle, _reload, _stop, _compare;
    private readonly SPanel _comparison = Ui.V(20);
    private bool _comparing;
    private readonly Expander _expand;
    private Document? _document;
    private bool _original, _busy, _loading, _stopRequested, _closed;
    private int _generation;

    public ResourceDocumentPanel(CatalogItem project, string section, Func<(string Game, string Loader)>? filters = null, string version = "")
    {
        _project = project; _section = section; _version = version; _filters = filters ?? (() => ("", ""));
        _translate = Ui.Btn(I18n.L("翻译全文"), BtnKind.Chip, (_, _) => PageBase.Run(Translate));
        _toggle = Ui.Btn(I18n.L("原文"), BtnKind.Ghost, (_, _) => { _original = !_original; Render(); });
        _reload = Ui.Btn(I18n.L("重新加载"), BtnKind.Ghost, (_, _) => PageBase.Run(Load));
        _compare = Ui.Btn(I18n.L("中英对照与修订"), BtnKind.Ghost, (_, _) => { _comparing = !_comparing; Render(); });
        _stop = Ui.Btn(I18n.L("停止翻译"), BtnKind.Ghost, (_, _) => { _stopRequested = true; _status.Text = I18n.L("当前段落完成后停止，已翻译内容会保留。"); });
        _toggle.Visibility = _stop.Visibility = _translate.Visibility = Visibility.Collapsed;
        var actions = Ui.H(6, _translate, _stop, _toggle, _compare, _reload); actions.Wrap = true;
        _compare.Visibility = Visibility.Collapsed; _comparison.Visibility = Visibility.Collapsed;
        _expand = new Expander { Header = I18n.L(section == "description" ? "完整介绍" : "更新日志"), Content = Ui.V(8, actions, _status, _text, _comparison) };
        _expand.Expanded += (_, _) => { if (_document == null && !_loading) PageBase.Run(Load); };
        Content = _expand;
        Unloaded += (_, _) => { _closed = true; _stopRequested = true; ++_generation; };
        Loaded += (_, _) => _closed = false;
    }

    public void ResetFilters()
    {
        ++_generation; _stopRequested = true; _document = null; _text.SetMarkdown("");
        _comparison.Children.Clear(); _compare.Visibility = Visibility.Collapsed;
        _status.Text = I18n.L("筛选已改变，展开或重新加载查看对应日志。");
        _translate.Visibility = _toggle.Visibility = Visibility.Collapsed;
        if (_expand.IsExpanded && !_busy) PageBase.Run(Load);
    }

    private async Task Load()
    {
        if (_loading || _busy) return;
        int generation = ++_generation; _loading = true; _reload.IsEnabled = false;
        _status.Text = I18n.L("正在读取完整文档…");
        try
        {
            var filter = _filters();
            var document = await AppServices.Client.CallAsync<Document>("catalog_document", new { source = _project.Source,
                slug = _project.Slug ?? "", id = _project.IdValue?.ToString() ?? "", section = _section, version = _version,
                game_version = filter.Game, loader = filter.Loader }) ?? throw new InvalidOperationException(I18n.L("上游未返回文档。"));
            if (_closed || generation != _generation) return;
            _document = document; _original = !document.Translations.Any(text => text.Length > 0); Render();
            _status.Text = document.Original.Length == 0 ? I18n.L("作者未提供此文档。") : ProgressText();
        }
        catch (Exception ex) { if (!_closed && generation == _generation) _status.Text = I18n.L("文档加载失败：{0}", ex.Message); }
        finally
        {
            _loading = false; _reload.IsEnabled = true;
            if (!_closed && generation != _generation && _expand.IsExpanded) PageBase.Run(Load);
        }
    }

    private string ProgressText()
    {
        var document = _document!;
        int complete = document.Translations.Count(text => text.Length > 0);
        if (complete == document.Parts.Count && complete > 0) return I18n.L("全文已处理 · {0} 段，可切换原文核对", complete);
        return complete == 0 ? I18n.L("完整原文 · 可使用本地模型翻译") :
            I18n.L("本地翻译 {0} / {1} 段 · 未完成的段落保留原文", complete, document.Parts.Count);
    }

    private void Render()
    {
        if (_document == null) return;
        var document = _document;
        _compare.Visibility = document.Parts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _compare.Content = I18n.L(_comparing ? "返回阅读全文" : "中英对照与修订");
        _text.Visibility = _comparing ? Visibility.Collapsed : Visibility.Visible;
        _comparison.Visibility = _comparing ? Visibility.Visible : Visibility.Collapsed;
        _comparison.Children.Clear();
        if (_comparing)
            for (int index = 0; index < document.Parts.Count; index++)
            {
                int paragraph = index;
                _comparison.Children.Add(new DocumentComparison(document.Key, index, document.Parts[index], document.Translations[index],
                    document.Manual.ElementAtOrDefault(index), (text, manual) =>
                    {
                        document.Translations[paragraph] = text;
                        while (document.Manual.Count < document.Parts.Count) document.Manual.Add(false);
                        document.Manual[paragraph] = manual;
                        _status.Text = ProgressText();
                        _translate.Visibility = document.Translations.Any(value => value.Length == 0) ? Visibility.Visible : Visibility.Collapsed;
                        _toggle.Visibility = !_comparing && document.Translations.Any(value => value.Length > 0) ? Visibility.Visible : Visibility.Collapsed;
                    }));
            }
        _text.SetMarkdown(_original ? document.Original : string.Concat(document.Parts.Select((part, index) =>
            document.Translations.ElementAtOrDefault(index) is string translated && translated.Length > 0 ? translated : part)));
        _translate.Visibility = document.Parts.Count > 0 && document.Translations.Any(text => text.Length == 0) ? Visibility.Visible : Visibility.Collapsed;
        _toggle.Visibility = !_comparing && document.Translations.Any(text => text.Length > 0) ? Visibility.Visible : Visibility.Collapsed;
        _toggle.Content = I18n.L(_original ? "中文" : "原文");
    }

    private async Task Translate()
    {
        if (_busy || _loading || _document == null) return;
        _busy = true; _stopRequested = false; int generation = _generation;
        _translate.IsEnabled = _reload.IsEnabled = false; _stop.Visibility = Visibility.Visible;
        var document = _document;
        try
        {
            for (int index = 0; index < document.Parts.Count; index++)
            {
                if (_closed || _stopRequested || generation != _generation) break;
                if (document.Translations[index].Length > 0) continue;
                _status.Text = I18n.L("正在本地翻译第 {0} / {1} 段…", index + 1, document.Parts.Count);
                var reply = await AppServices.Client.CallAsync<Translation>("translate_catalog_document", new { key = document.Key, index, original = document.Parts[index] })
                    ?? throw new InvalidOperationException(I18n.L("没有收到译文。"));
                if (_closed || generation != _generation) return;
                document.Translations[index] = reply.Text; _original = false; Render();
            }
            if (!_closed && generation == _generation) _status.Text = ProgressText();
        }
        catch (Exception ex)
        {
            Smoke.Note("catalog-document-translation", ex.ToString());
            if (!_closed && generation == _generation) _status.Text = I18n.L("翻译暂停：{0}。已完成段落保留，可继续重试。", ex.Message.TrimEnd('。', '.'));
        }
        finally
        {
            _busy = false; _translate.IsEnabled = _reload.IsEnabled = true; _stop.Visibility = Visibility.Collapsed;
            if (!_closed && generation != _generation && _expand.IsExpanded) PageBase.Run(Load);
        }
    }
}
