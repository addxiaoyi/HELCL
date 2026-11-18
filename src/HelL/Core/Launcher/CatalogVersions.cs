using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace HelL.Core.Launcher;

public static class CatalogVersions
{
    public static List<object> Sort(IEnumerable<object> versions, string dateField) => versions.OfType<PyDict>()
        .OrderByDescending(row => Timestamp(Py.S(row.Get(dateField))))
        .ThenByDescending(row => Py.S(row.Get("id")), StringComparer.Ordinal).Cast<object>().ToList();

    public static List<object> Modrinth(DownloadManager manager, string project, PyDict parameters, Func<string, object>? fetch = null)
    {
        var origins = new[] { Mods.MODRINTH_API }.Concat(Source.ModrinthApiBases()).Distinct().ToArray();
        return Read(origins, "/project/" + Uri.EscapeDataString(project) + "/version", parameters, "date_published",
            url => fetch != null ? fetch(url) : manager.FetchJson(url, Mods.API_TIMEOUT, expand: false), false);
    }

    public static List<object> CurseForge(DownloadManager manager, string project, PyDict parameters, IDictionary<string, string> headers)
    {
        var origins = new[] { Source.CF_OFFICIAL }.Concat(Source.CfApiBases()).Distinct().ToArray();
        return Read(origins, "/mods/" + Uri.EscapeDataString(project) + "/files", parameters, "fileDate",
            url => manager.FetchJson(url, Mods.API_TIMEOUT, expand: false, headers: headers), true);
    }

    public static List<object> Read(IEnumerable<string> origins, string path, PyDict parameters, string dateField, Func<string, object> fetch, bool wrapped)
    {
        Exception? last = null; int attempt = 0;
        foreach (string origin in origins)
        {
            bool primary = attempt++ == 0;
            try
            {
                object payload = fetch(Mods.WithParams(origin + path, parameters));
                object? values = wrapped ? (payload as PyDict)?.Get("data") : payload;
                if (values is not List<object> rows || rows.Any(row => row is not PyDict)) throw new InvalidOperationException("版本列表格式无效。");
                var sorted = Sort(rows, dateField).OfType<PyDict>().Select(row => row.Copy()).ToList();
                foreach (var row in sorted)
                {
                    row["version_source"] = primary ? "官方实时列表" : "镜像回退列表";
                    row["version_warning"] = primary ? "" : "上游暂不可用，镜像可能尚未同步最新版本。";
                }
                return sorted.Cast<object>().ToList();
            }
            catch (Exception ex)
            {
                if (Downloader.IsCancel(ex)) throw;
                last = ex; Utils.log.Warning("资源版本来源不可用 %s: %s", origin, ex.Message);
            }
        }
        throw new InvalidOperationException("无法获取版本列表，请检查网络后重试。", last);
    }

    public static DateTimeOffset Timestamp(string value) => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time) ? time : DateTimeOffset.MinValue;
}
