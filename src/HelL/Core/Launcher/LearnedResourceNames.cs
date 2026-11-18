using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace HelL.Core.Launcher;

internal static class LearnedResourceNames
{
    private static readonly object Gate = new();
    private static string _path = "";
    private static ResourceName[] _rows = Array.Empty<ResourceName>();
    internal static IReadOnlyList<ResourceName> Rows
    {
        get
        {
            lock (Gate)
            {
                string path = Path.Combine(Utils.ROOT, "cache", "catalog-learned-names.json");
                if (_path == path) return _rows;
                _path = path; _rows = Array.Empty<ResourceName>();
                if (!File.Exists(path)) return _rows;
                try
                {
                    if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("本地译名缓存过大。");
                    _rows = (JsonSerializer.Deserialize<ResourceName[]>(File.ReadAllText(path)) ?? Array.Empty<ResourceName>())
                        .Where(row => CatalogFiles.KIND_CF.ContainsKey(row.Kind) && row.Chinese.Length <= 120 && ResourceNames.ContainsChinese(row.Chinese)).Take(4000).ToArray();
                }
                catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("LearnedResourceNames.Load: " + ex.Message); }
                return _rows;
            }
        }
    }

    internal static void Remember(IEnumerable<PyDict> projects)
    {
        lock (Gate)
        {
            var rows = Rows.ToList();
            foreach (var project in projects)
            {
                string kind = Py.S(project.Get("kind")), translated = Py.S(project.Get("chinese_name"));
                if (!CatalogFiles.KIND_CF.ContainsKey(kind) || Py.S(project.Get("name_localization")) != "本地译名" || !ResourceNames.ContainsChinese(translated)) continue;
                string slug = Py.S(project.Get("slug")), source = Py.S(project.Get("source")), name = Py.S(project.Get("name"));
                var entry = new ResourceName { Kind = kind, Chinese = translated, English = name, Attribution = "本地译名",
                    Modrinth = source == "modrinth" ? slug : "", Curseforge = source == "curseforge" ? slug : "",
                    CurseforgeId = source == "curseforge" ? Py.S(project.Get("id")) : "" };
                rows.RemoveAll(row => row.Kind == kind && row.English == name);
                rows.Add(entry);
            }
            _rows = rows.Skip(Math.Max(0, rows.Count - 4000)).ToArray();
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path + ".new", JsonSerializer.Serialize(_rows));
            if (File.Exists(_path)) File.Replace(_path + ".new", _path, null);
            else File.Move(_path + ".new", _path);
        }
    }
}
