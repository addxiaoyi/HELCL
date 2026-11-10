using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HelL.Core.Launcher;

public static class CommunityNames
{
    private const int MaxBytes = 6 * 1024 * 1024;
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static ResourceName[] _entries = Array.Empty<ResourceName>();
    private static string _loadedRoot = "";
    private static DateTime _nextCheck;
    public static IReadOnlyList<ResourceName> Entries => _entries;
    public static string LastError { get; private set; } = "";
    public static DateTime? Updated { get; private set; }
    public const string Attribution = "HMCL / MC 百科";
    private static string Folder => Path.Combine(Utils.ROOT, "cache", "resource-names");

    public static void Ensure(bool force = false, HttpMessageHandler? handler = null) => RefreshAsync(force, handler).GetAwaiter().GetResult();

    private static async Task RefreshAsync(bool force, HttpMessageHandler? handler)
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            string folder = Folder;
            if (_loadedRoot != folder)
            {
                LastError = ""; _entries = LoadCache(folder); _loadedRoot = folder; _nextCheck = DateTime.MinValue;
            }
            if (!force && DateTime.UtcNow < _nextCheck) return;
            _nextCheck = DateTime.UtcNow.AddMinutes(10);
            var files = new[] { "mod", "modpack" };
            bool fresh = files.All(kind => _entries.Any(row => row.Kind == kind) && File.Exists(Path.Combine(folder, kind + ".csv")) &&
                DateTime.UtcNow - File.GetLastWriteTimeUtc(Path.Combine(folder, kind + ".csv")) < TimeSpan.FromDays(7));
            if (!force && fresh) { _nextCheck = DateTime.UtcNow.AddHours(1); return; }
            Directory.CreateDirectory(folder);
            using var client = handler == null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
            client.Timeout = TimeSpan.FromSeconds(12);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("HelL/1.0");
            var downloads = await Task.WhenAll(files.Select(async kind =>
            {
                string url = "https://raw.githubusercontent.com/HMCL-dev/HMCL/main/HMCL/src/main/resources/assets/addon/" + kind + ".csv";
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
                using var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var bytes = new MemoryStream();
                byte[] buffer = new byte[16384]; int count;
                while ((count = await input.ReadAsync(buffer, 0, buffer.Length, timeout.Token).ConfigureAwait(false)) > 0)
                {
                    if (bytes.Length + count > MaxBytes) throw new InvalidDataException("资源中文索引超过大小限制。");
                    bytes.Write(buffer, 0, count);
                }
                string csv = new UTF8Encoding(false, true).GetString(bytes.ToArray());
                var parsed = Parse(csv, kind);
                if (parsed.Count < 100) throw new InvalidDataException("资源中文索引不完整，保留上一次缓存。");
                return (Kind: kind, Csv: csv, Rows: parsed);
            })).ConfigureAwait(false);
            foreach (var download in downloads)
            {
                string path = Path.Combine(folder, download.Kind + ".csv");
                File.WriteAllText(path + ".new", download.Csv, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(path + ".new", path, null);
                else File.Move(path + ".new", path);
            }
            _entries = downloads.SelectMany(download => download.Rows).ToArray();
            Updated = DateTime.UtcNow; LastError = ""; _nextCheck = DateTime.UtcNow.AddDays(1);
        }
        catch (Exception ex)
        {
            LastError = "社区映射更新失败，继续使用内置词表和已有缓存。";
            System.Diagnostics.Trace.TraceWarning("CommunityNames.Refresh: " + ex.Message);
            Utils.log.Warning("社区中文映射更新失败: %s", Py.ExcStr(ex));
        }
        finally { Gate.Release(); }
    }

    private static ResourceName[] LoadCache(string folder)
    {
        var rows = new List<ResourceName>(); Updated = null;
        foreach (string kind in new[] { "mod", "modpack" })
        {
            string path = Path.Combine(folder, kind + ".csv");
            if (!File.Exists(path)) continue;
            try
            {
                if (new FileInfo(path).Length > MaxBytes) throw new InvalidDataException("中文映射缓存过大。");
                rows.AddRange(Parse(File.ReadAllText(path, Encoding.UTF8), kind));
                DateTime written = File.GetLastWriteTimeUtc(path);
                Updated = Updated.HasValue && Updated.Value < written ? Updated : written;
            }
            catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("CommunityNames.LoadCache: " + ex.Message); }
        }
        return rows.ToArray();
    }

    public static IReadOnlyList<ResourceName> Parse(string csv, string kind)
    {
        var entries = new List<ResourceName>();
        using var reader = new StringReader(csv);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.Length == 0 || line.TrimStart('\uFEFF').StartsWith("#")) continue;
            var cells = CsvRow(line);
            if (cells.Count < 6) throw new InvalidDataException("中文映射 CSV 列数无效。");
            if (!ResourceNames.ContainsChinese(cells[3])) continue;
            string[] mr = cells[2].Split(';'), cf = cells[1].Split(';');
            var english = cells[4].Length > 0 ? cells[4] : mr.FirstOrDefault(slug => slug.Length > 0) ?? cf[0];
            if (english.Length == 0) continue;
            entries.Add(new ResourceName { Kind = kind, Chinese = cells[3], English = english, Modrinth = mr[0], Curseforge = cf[0],
                Aliases = cells.Skip(5).SelectMany(value => value.Split(';')).Concat(mr.Skip(1)).Concat(cf.Skip(1)).Where(value => value.Length > 0).ToArray(),
                Attribution = Attribution });
        }
        return entries;
    }

    private static List<string> CsvRow(string line)
    {
        var cells = new List<string>(); var cell = new StringBuilder(); bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (ch == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { cell.Append('"'); i++; }
                else if (quoted) quoted = false;
                else if (cell.Length == 0) quoted = true;
                else cell.Append(ch);
            }
            else if (ch == ',' && !quoted) { cells.Add(cell.ToString()); cell.Clear(); }
            else cell.Append(ch);
        }
        if (quoted) throw new InvalidDataException("中文映射 CSV 引号未闭合。");
        cells.Add(cell.ToString()); return cells;
    }
}
