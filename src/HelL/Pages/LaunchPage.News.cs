using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HelL.Services;

namespace HelL.Pages;

public sealed partial class LaunchPage
{
    private readonly SPanel _newsHost = Ui.V(6);
    private readonly NewsPanel _news = new NewsPanel();
    private bool _newsLoading;
    private bool _newsTranslating;
    private int _newsGeneration;

    private async Task LoadNewsAsync()
    {
        if (_newsLoading) return;
        _newsLoading = true;
        int generation = ++_newsGeneration;
        _news.RefreshRequested -= RefreshNews;
        _news.RefreshRequested += RefreshNews;
        _news.TranslationRequested -= TranslateNews;
        _news.TranslationRequested += TranslateNews;
        try
        {
            string mode = await Api.TryCallAsync("get_setting", new { key = "homepage_mode", @default = "news" }, "news") ?? "news";
            if (mode == "blank")
            {
                _newsHost.Children.Clear();
                _newsHost.Children.Add(Ui.Muted(I18n.L("主页已设为空白")));
                return;
            }
            if (mode == "custom")
            {
                await LoadCustomHome();
                return;
            }
            _newsHost.Children.Clear();
            _newsHost.Children.Add(_news);
            _news.Busy(true);
            var cached = await Api.CallAsync<NewsFeed>("news_feed");
            if (cached != null && cached.Rows.Count > 0) _news.Show(cached);
            var feed = await Api.CallAsync<NewsFeed>("news_feed", new { refresh = true }) ?? throw new InvalidOperationException("新闻服务没有返回内容。");
            if (generation != _newsGeneration) return;
            _news.Show(feed);
            if (I18n.Current == "zh_CN" && feed.Rows.Any(row => row.Localization == "版本导读")) _ = TranslateNewsAsync(generation);
        }
        catch (Exception ex) { _news.Error(I18n.L("新闻暂时不可用，点击刷新重试。")); Smoke.Note("news", ex.Message); }
        finally { _newsLoading = false; _news.Busy(false); }
    }

    private async Task LoadCustomHome()
    {
        string? path = await Api.TryCallAsync("get_setting", new { key = "custom_homepage", @default = "" }, "");
        _newsHost.Children.Clear();
        try
        {
            string text = string.IsNullOrWhiteSpace(path) || !File.Exists(path)
                ? I18n.L("未设置自定义主页。到设置 → 启动页主页 填本地 HTML 路径。")
                : await Task.Run(() => File.ReadAllText(path));
            _newsHost.Children.Add(Ui.Muted(text.Length > 2000 ? text.Substring(0, 2000) + "…" : text));
        }
        catch (Exception ex) { _newsHost.Children.Add(Ui.Muted(I18n.L("读不了自定义主页：") + ex.Message)); }
    }

    private void RefreshNews() => Run(LoadNewsAsync);
    private void TranslateNews() => Run(() => TranslateNewsAsync(_newsGeneration));

    private async Task TranslateNewsAsync(int generation)
    {
        if (_newsTranslating) return;
        _newsTranslating = true;
        _news.TranslationState(true, I18n.L("正在准备中文摘要…"));
        try
        {
            var translated = await Api.CallAsync<NewsTranslation>("translate_news");
            if (generation != _newsGeneration) return;
            if (translated != null && translated.Ok && translated.Rows.Count > 0) _news.Show(new NewsFeed { Rows = translated.Rows });
            _news.TranslationState(false, translated?.Message ?? I18n.L("中文摘要暂不可用，可查看官方原文。"));
        }
        catch (Exception ex) { _news.TranslationState(false, I18n.L("中文摘要暂不可用，可查看官方原文。")); Smoke.Note("news-translation", ex.Message); }
        finally { _newsTranslating = false; _news.TranslationFinished(); }
    }
}
