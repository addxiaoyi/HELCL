using System;
using HelL.Core.Launcher;

namespace HelL.Core;

public partial class BackendApi
{
    public object news_feed(bool refresh = false)
    {
        var rows = refresh ? News.Fetch() : News.LoadCached();
        return PyDict.Of("rows", NewsLocalization.Display(rows, I18nCore.CurrentLanguage()), "cached", !refresh || News.IsCached,
            "error", refresh ? News.LastError : "", "source", "Minecraft 官方");
    }

    public object translate_news()
    {
        if (_ai_busy) return PyDict.Of("ok", false, "message", "AI 助手正在回复，新闻翻译可以稍后再试。");
        try
        {
            string message = NewsLocalization.TranslatePending(News.LoadCached(), _settings_full());
            return PyDict.Of("ok", true, "message", message, "rows", NewsLocalization.Display(News.LoadCached(), I18nCore.CurrentLanguage()));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning("News translation: " + ex);
            return PyDict.Of("ok", false, "message", "暂时无法生成中文摘要。已保留中文导读，可查看官方原文。");
        }
    }
}
