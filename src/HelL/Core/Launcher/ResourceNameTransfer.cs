using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HelL.Core.Launcher;

public sealed class ResourceNameImportRow
{
    public string Key { get; set; } = "";
    public CustomResourceName Incoming { get; set; } = new();
    public CustomResourceName? Current { get; set; }
    public string Status { get; set; } = "new";
}

public sealed class ResourceNameImport
{
    public string Revision { get; set; } = "";
    public List<ResourceNameImportRow> Rows { get; set; } = new();
}

public static partial class CustomResourceNames
{
    private sealed class ExportFile
    {
        public string Format { get; set; } = "hell-resource-names";
        public int Version { get; set; } = 1;
        public CustomResourceName[] Entries { get; set; } = Array.Empty<CustomResourceName>();
    }

    public static int Export(string path)
    {
        path = JsonPath(path);
        lock (Gate)
        {
            Load(); RequireReadable();
            if (path.Equals(Path.GetFullPath(FilePath), StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("请选择单独的导出文件，不能覆盖正在使用的词表。");
            var payload = new ExportFile { Entries = _rows.Select(Clone).ToArray() };
            string staged = path + "." + Guid.NewGuid().ToString("N") + ".new";
            try
            {
                File.WriteAllText(staged, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(staged, path, null); else File.Move(staged, path);
            }
            finally { if (File.Exists(staged)) File.Delete(staged); }
            return payload.Entries.Length;
        }
    }

    public static ResourceNameImport PreviewImport(string path)
    {
        path = JsonPath(path);
        if (!File.Exists(path) || new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("词表文件不存在或超过 4 MiB。");
        var file = JsonSerializer.Deserialize<ExportFile>(File.ReadAllText(path, new UTF8Encoding(false, true)),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("词表文件为空。");
        if (file.Format != "hell-resource-names" || file.Version != 1 || file.Entries == null || file.Entries.Length > MaxEntries)
            throw new InvalidDataException("词表格式或版本不支持，最多导入 2000 项。");
        var incoming = new List<CustomResourceName>();
        foreach (var entry in file.Entries)
        {
            Validate(entry);
            if (entry.Chinese.Length == 0 && entry.Aliases.Length == 0) throw new InvalidDataException("导入词条缺少中文名称和别名。");
            if (incoming.Any(row => SameProject(row, entry))) throw new InvalidDataException("词表重复包含同一项目，请先合并重复记录。");
            incoming.Add(Clone(entry));
        }
        lock (Gate)
        {
            Load(); RequireReadable();
            return new ResourceNameImport { Revision = Revision(), Rows = incoming.Select(entry =>
            {
                var current = _rows.FirstOrDefault(row => SameProject(row, entry));
                return new ResourceNameImportRow { Key = entry.Key, Incoming = entry, Current = current == null ? null : Clone(current),
                    Status = current == null ? "new" : SameValue(current, entry) ? "same" : "conflict" };
            }).ToList() };
        }
    }

    public static int ApplyImport(ResourceNameImport preview, IReadOnlyDictionary<string, string> choices)
    {
        lock (Gate)
        {
            Load(); RequireReadable();
            if (preview.Revision != Revision()) throw new InvalidOperationException("本机词表已改变，请重新预览后导入。");
            var rows = _rows.Select(Clone).ToList(); int changed = 0;
            foreach (var item in preview.Rows)
            {
                var incoming = Clone(item.Incoming); Validate(incoming);
                var current = rows.FirstOrDefault(row => SameProject(row, incoming));
                if (current == null) { rows.Add(incoming); changed++; continue; }
                if (SameValue(current, incoming)) continue;
                string action = choices.TryGetValue(item.Key, out var selected) ? selected : "keep";
                if (action == "keep") continue;
                if (action == "merge")
                {
                    var merged = Clone(current);
                    merged.Aliases = current.Aliases.Concat(incoming.Aliases).Concat(new[] { incoming.Chinese })
                        .Where(alias => alias.Length > 0 && alias != current.Chinese).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    Validate(merged); rows[rows.IndexOf(current)] = merged;
                }
                else if (action == "replace") rows[rows.IndexOf(current)] = incoming;
                else throw new ArgumentException("冲突处理方式无效。");
                changed++;
            }
            if (rows.Count > MaxEntries) throw new InvalidOperationException("导入后超过 2000 项，请减少词条后重试。");
            if (changed > 0) WriteRows(rows, backup: true);
            return changed;
        }
    }

    private static bool SameProject(CustomResourceName a, CustomResourceName b) => a.Source == b.Source &&
        (a.Id.Length > 0 && b.Id.Length > 0 ? a.Id == b.Id : a.Slug.Length > 0 && a.Slug.Equals(b.Slug, StringComparison.OrdinalIgnoreCase));
    private static bool SameValue(CustomResourceName a, CustomResourceName b) => a.Chinese == b.Chinese && a.Aliases.SequenceEqual(b.Aliases, StringComparer.OrdinalIgnoreCase);
    private static void RequireReadable() { if (LastError.Length > 0) throw new InvalidDataException(LastError); }
    private static string JsonPath(string path)
    {
        path = Path.GetFullPath(path);
        if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("请选择 JSON 词表文件。");
        return path;
    }
    private static string Revision()
    {
        using var hash = SHA256.Create();
        return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(_rows))));
    }
}
