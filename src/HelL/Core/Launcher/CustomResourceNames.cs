using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HelL.Core.Launcher;

public sealed class CustomResourceName
{
    public string Kind { get; set; } = "";
    public string Source { get; set; } = "";
    public string Id { get; set; } = "";
    public string Slug { get; set; } = "";
    public string English { get; set; } = "";
    public string Chinese { get; set; } = "";
    public string[] Aliases { get; set; } = Array.Empty<string>();
    internal string Key => Source + ":" + (Source == "curseforge" ? Id : Slug.Length > 0 ? Slug : Id);
    internal ResourceName Mapping => new() { Kind = Kind, English = English, Chinese = Chinese, Aliases = Aliases,
        Modrinth = Source == "modrinth" ? Slug : "", Curseforge = Source == "curseforge" ? Slug : "",
        CurseforgeId = Source == "curseforge" ? Id : "", Attribution = CustomResourceNames.Attribution };
}

public static partial class CustomResourceNames
{
    public const string Attribution = "自定义名称";
    private const int MaxEntries = 2000;
    private static readonly object Gate = new();
    private static string _file = "";
    private static DateTime _written;
    private static CustomResourceName[] _rows = Array.Empty<CustomResourceName>();
    public static string LastError { get; private set; } = "";
    private static string FilePath => Path.Combine(Utils.ROOT, "resource-names.json");

    public static IReadOnlyList<CustomResourceName> Entries
    {
        get { lock (Gate) { Load(); return _rows.Select(Clone).ToArray(); } }
    }

    public static CustomResourceName? Find(string source, string slug, string id) => Entries.FirstOrDefault(row =>
        row.Source.Equals(source, StringComparison.OrdinalIgnoreCase) &&
        (slug.Length > 0 && row.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase) || id.Length > 0 && row.Id == id));

    public static void Save(CustomResourceName entry)
    {
        Validate(entry);
        lock (Gate)
        {
            Load();
            if (LastError.Length > 0) throw new InvalidDataException(LastError);
            var rows = _rows.Where(row => row.Key != entry.Key && !(row.Source == entry.Source && entry.Id.Length > 0 && row.Id == entry.Id)).ToList();
            if (entry.Chinese.Length > 0 || entry.Aliases.Length > 0) rows.Add(entry);
            if (rows.Count > MaxEntries) throw new InvalidOperationException("自定义名称已达到 2000 项，请先移除不用的记录。");
            WriteRows(rows);
        }
    }

    private static void WriteRows(IEnumerable<CustomResourceName> rows, bool backup = false)
    {
        string path = FilePath;
        var entries = rows.Select(Clone).ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string staged = path + "." + Guid.NewGuid().ToString("N") + ".new";
        try
        {
            File.WriteAllText(staged, JsonSerializer.Serialize(entries));
            if (File.Exists(path)) File.Replace(staged, path, backup ? path + ".import-backup" : null);
            else File.Move(staged, path);
        }
        finally { if (File.Exists(staged)) File.Delete(staged); }
        _rows = entries; _file = path; _written = File.GetLastWriteTimeUtc(path);
    }

    private static CustomResourceName Clone(CustomResourceName row) => new() { Kind = row.Kind, Source = row.Source, Id = row.Id,
        Slug = row.Slug, English = row.English, Chinese = row.Chinese, Aliases = row.Aliases.ToArray() };

    private static void Validate(CustomResourceName entry)
    {
        if (entry == null || !CatalogFiles.KIND_CF.ContainsKey(entry.Kind ?? "")) throw new ArgumentException("资源类型无效。");
        if (entry.Source is not ("modrinth" or "curseforge")) throw new ArgumentException("资源来源无效。");
        entry.Id = entry.Id?.Trim() ?? ""; entry.Slug = entry.Slug?.Trim() ?? "";
        bool validId = entry.Source == "curseforge" ? long.TryParse(entry.Id, out long id) && id > 0 :
            Regex.IsMatch(entry.Slug.Length > 0 ? entry.Slug : entry.Id, @"\A[a-zA-Z0-9_.-]{1,120}\z");
        if (!validId) throw new ArgumentException("资源标识无效，请重新打开项目详情。");
        entry.English = Label(entry.English, 240);
        entry.Chinese = Label(entry.Chinese, 120);
        if (entry.Chinese.Length > 0 && !ResourceNames.ContainsChinese(entry.Chinese)) throw new ArgumentException("中文名称应包含中文；英文简称可填入搜索别名。");
        entry.Aliases = (entry.Aliases ?? Array.Empty<string>()).Select(alias => Label(alias, 80)).Where(alias => alias.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (entry.Aliases.Length > 20) throw new ArgumentException("每个资源最多保存 20 个搜索别名。");
    }

    private static string Label(string text, int limit)
    {
        text = (text ?? "").Trim();
        if (text.Length > limit || text.Any(char.IsControl) || text.IndexOfAny(new[] { '<', '>' }) >= 0)
            throw new ArgumentException("名称或别名过长，或含有无效字符。");
        return text;
    }

    private static void Load()
    {
        string path = FilePath;
        DateTime written = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        if (path == _file && written == _written) return;
        _file = path; _written = written; _rows = Array.Empty<CustomResourceName>(); LastError = "";
        if (!File.Exists(path)) return;
        try
        {
            if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("自定义名称文件过大。");
            var rows = JsonSerializer.Deserialize<CustomResourceName[]>(File.ReadAllText(path)) ?? throw new InvalidDataException("自定义名称文件为空。");
            if (rows.Length > MaxEntries) throw new InvalidDataException("自定义名称条数超出限制。");
            foreach (var row in rows) Validate(row);
            _rows = rows;
        }
        catch (Exception ex)
        {
            LastError = "自定义名称文件无法读取，原文件已保留。请修复 resource-names.json 后重试。";
            System.Diagnostics.Trace.TraceWarning("CustomResourceNames.Load: " + ex);
        }
    }
}
