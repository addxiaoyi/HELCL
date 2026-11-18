using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace HelL.Core.Launcher;

public static class News
{
    public static readonly string[] URLS = { "https://launchercontent.mojang.com/v2/javaPatchNotes.json", "https://launchercontent.mojang.com/javaPatchNotes.json" };
    public static string CACHE => PPath.Join(Utils.ROOT, "cache", "news.json");
    private static readonly object Gate = new object();
    public static string LastError { get; private set; } = "";
    public static bool IsCached { get; private set; }

    public static List<object> _rows_from(object payload)
    {
        object entries = payload is PyDict feed ? feed.Get("entries") ?? feed.Get("patchNotes") : payload;
        if (!(entries is List<object> rows)) return new List<object>();
        var parsed = new List<object>();
        foreach (var entry in rows.OfType<PyDict>().Take(12))
        {
            string title = PlainText(Py.S(entry.Get("title") ?? entry.Get("version") ?? entry.Get("id")), 200);
            if (title.Length == 0) continue;
            string version = Py.S(entry.Get("version"));
            object image = entry.Get("image") ?? entry.Get("newsPageImage") ?? entry.Get("cardBackground");
            string cover = image is PyDict picture ? Py.S(picture.Get("url")) : Py.S(image);
            string article = OfficialUrl(Py.S(entry.Get("readMoreLink") ?? entry.Get("url")), false);
            if (article.Length == 0 && Regex.IsMatch(version, @"\A[0-9][a-zA-Z0-9.\-]*\z"))
            {
                string slug = Regex.Replace(title.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
                article = "https://www.minecraft.net/en-us/article/" + (slug.StartsWith("minecraft-", StringComparison.Ordinal) ? slug : "minecraft-" + version.Replace('.', '-').ToLowerInvariant());
            }
            parsed.Add(PyDict.Of("title", title, "body", PlainText(Py.S(entry.Get("shortText") ?? entry.Get("text") ?? entry.Get("body") ?? entry.Get("subtitle")), 650),
                "version", version, "image", OfficialUrl(cover, true), "url", article,
                "type", Py.S(entry.Get("type", "news")), "date", DateOnly(Py.S(entry.Get("date") ?? entry.Get("updated_at"))),
                "id", Py.S(entry.Get("id") ?? entry.Get("version") ?? title), "source", "Minecraft 官方"));
        }
        return parsed;
    }

    public static string OfficialUrl(string value, bool image)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        if (!Uri.TryCreate(new Uri("https://launchercontent.mojang.com/"), value.Trim(), out var uri) ||
            uri.Scheme != "https" || uri.UserInfo.Length != 0 || !uri.IsDefaultPort) return "";
        bool host = image ? uri.Host == "launchercontent.mojang.com" || uri.Host == "www.minecraft.net" || uri.Host == "minecraft.net"
            : uri.Host == "www.minecraft.net" || uri.Host == "minecraft.net";
        return host ? uri.AbsoluteUri : "";
    }

    public static string PlainText(string html, int limit)
    {
        string text = Regex.Replace(html ?? "", @"<(script|style)\b[^>]*>[\s\S]*?</\1>", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(text, "<[^>]*>", " ")), @"\s+", " ").Trim();
        return text.Length <= limit ? text : text.Substring(0, limit).TrimEnd() + "…";
    }

    private static string DateOnly(string value) => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ? date.ToString("yyyy-MM-dd") : "";

    public static List<object> Fetch(DownloadManager dm = null)
    {
        lock (Gate)
        {
            dm ??= new DownloadManager(2L);
            Exception failure = null;
            foreach (string url in URLS)
            {
                try
                {
                    var rows = _rows_from(dm.FetchJson(url, 12L));
                    if (rows.Count == 0) throw new InvalidOperationException("官方新闻返回了空列表。");
                    Utils.WriteJson(CACHE, rows);
                    LastError = ""; IsCached = false;
                    return rows;
                }
                catch (Exception ex) { failure = ex; }
            }
            LastError = "暂时无法连接 Minecraft 新闻，已保留上次内容。";
            IsCached = true;
            Trace.TraceWarning("News.Fetch: " + failure);
            return LoadCached();
        }
    }

    public static List<object> LoadCached()
    {
        object cached = Utils.ReadJson(CACHE);
        if (cached is PyDict) return _rows_from(cached);
        if (!(cached is List<object> entries)) return new List<object>();
        // Upgrade old cache rows so relative covers work immediately after an update.
        return _rows_from(entries);
    }
}
