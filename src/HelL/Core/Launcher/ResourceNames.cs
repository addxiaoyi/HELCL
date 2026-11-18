using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using HelL.Core;

namespace HelL.Core.Launcher;

public sealed class ResourceName
{
    public string Kind { get; set; } = "mod";
    public string Chinese { get; set; } = "";
    public string English { get; set; } = "";
    public string Modrinth { get; set; } = "";
    public string Curseforge { get; set; } = "";
    public string CurseforgeId { get; set; } = "";
    public string[] Aliases { get; set; } = Array.Empty<string>();
    public string Attribution { get; set; } = "内置中文名";
    internal IEnumerable<string> Names => new[] { Chinese, English, Modrinth, Curseforge }.Concat(Aliases);
    internal bool Supports(string kind) => Kind == kind || Kind == "mod" && kind == "datapack";
}

public static class ResourceNames
{
    private static readonly Lazy<ResourceName[]> Builtins = new(Load);
    private static readonly Regex Han = new(@"[\u3400-\u9fff]");
    public static bool ContainsChinese(string text) => Han.IsMatch(text ?? "");
    public static string Normalize(string text) => new((text ?? "").Normalize(NormalizationForm.FormKC)
        .ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    private static IEnumerable<ResourceName> Defaults => Builtins.Value.Concat(CommunityNames.Entries).Concat(LearnedResourceNames.Rows);
    public static IEnumerable<ResourceName> All => CustomResourceNames.Entries.Select(row => row.Mapping).Concat(Defaults);

    private static ResourceName[] Load()
    {
        string json = CoreResources.ReadText("catalog.names-zh.json") ?? throw new InvalidOperationException("内置资源中文映射缺失。");
        var rows = System.Text.Json.JsonSerializer.Deserialize<ResourceName[]>(json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("内置资源中文映射无效。");
        return rows.Concat(Catalog.ResourceAliases().Where(row => !rows.Any(saved => saved.Kind == row.Kind &&
            (saved.Modrinth.Length > 0 && saved.Modrinth == row.Modrinth || saved.English == row.English)))).ToArray();
    }

    public static ResourceName? Find(string kind, string source, string slug, string id, string name)
    {
        var custom = CustomResourceNames.Find(source, slug, id);
        if (custom != null && custom.Chinese.Length > 0) return custom.Mapping;
        var candidates = Defaults.Where(row => kind.Length == 0 || row.Supports(kind));
        bool cf = source.StartsWith("curse", StringComparison.OrdinalIgnoreCase);
        if (slug.Length > 0)
        {
            var exact = candidates.FirstOrDefault(row => string.Equals(cf ? row.Curseforge : row.Modrinth, slug, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;
        }
        if (cf && id.Length > 0)
        {
            var exact = candidates.FirstOrDefault(row => row.CurseforgeId == id);
            if (exact != null) return exact;
        }
        string normalized = Normalize(name);
        return normalized.Length == 0 ? null : candidates.FirstOrDefault(row => Normalize(row.English) == normalized || Normalize(row.Chinese) == normalized);
    }

    public static IReadOnlyList<ResourceName> Match(string kind, string query, int limit = 3)
    {
        string key = Normalize(query);
        if (key.Length == 0) return Array.Empty<ResourceName>();
        return All.Where(row => row.Supports(kind)).Select(row => new { Row = row, Score = row.Names.Select(name =>
        {
            string candidate = Normalize(name);
            if (candidate == key) return 10000;
            if (key.Length >= 2 && candidate.Contains(key)) return 5000 - candidate.Length;
            if (candidate.Length >= 2 && key.Contains(candidate)) return 1000 + candidate.Length;
            return 0;
        }).DefaultIfEmpty(0).Max() }).Where(match => match.Score > 0)
            .OrderByDescending(match => match.Score).Select(match => match.Row)
            .GroupBy(row => row.Kind + "|" + row.Modrinth + "|" + row.Curseforge + "|" + row.English)
            .Select(group => group.First()).Take(Math.Max(1, limit)).ToArray();
    }

    public static IReadOnlyList<string> Queries(string kind, string query)
    {
        string original = (query ?? "").Trim();
        if (!ContainsChinese(original) && !CustomResourceNames.Entries.Any(row => row.Kind == kind &&
            row.Aliases.Any(alias => Normalize(alias) == Normalize(original)))) return new[] { original };
        var queries = Match(kind, original).Select(row => row.English.Length > 0 && !ContainsChinese(row.English)
            ? row.English : row.Modrinth.Length > 0 ? row.Modrinth : row.Curseforge).Where(value => value.Length > 0).ToList();
        string keywords = ResourceTerms.TranslateQuery(kind, original);
        if (keywords.Length > 0 && !ContainsChinese(keywords)) queries = queries.Take(2).Concat(new[] { keywords }).ToList();
        // Reserve one request for the user's wording, even when several aliases match.
        return queries.Where(value => !value.Equals(original, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(3).Concat(new[] { original }).ToArray();
    }

    public static void Localize(PyDict project)
    {
        if (Py.S(project.Get("name_localization")) == CustomResourceNames.Attribution)
        { project.Remove("chinese_name"); project.Remove("name_localization"); project.Remove("search_aliases"); }
        string original = Py.S(Py.Or(project.Get("name"), project.Get("title"), ""));
        string kind = Py.S(Py.Or(project.Get("kind"), project.Get("project_type"), ""));
        var custom = CustomResourceNames.Find(Py.S(project.Get("source")), Py.S(project.Get("slug")), Py.S(project.Get("id")));
        var mapped = Find(kind, Py.S(project.Get("source")), Py.S(project.Get("slug")), Py.S(project.Get("id")), original);
        if (mapped == null || !ContainsChinese(mapped.Chinese))
        {
            string translated = ResourceTerms.TranslateTitle(original);
            if (translated != original && ContainsChinese(translated))
            { project["chinese_name"] = translated; project["name_localization"] = "关键词译名"; }
            if (custom != null) project["search_aliases"] = custom.Aliases.Cast<object>().ToList();
            return;
        }
        project["chinese_name"] = mapped.Chinese;
        project["name_localization"] = mapped.Attribution;
        project["search_aliases"] = mapped.Names.Concat(custom?.Aliases ?? Array.Empty<string>()).Where(name => name.Length > 0).Distinct().Cast<object>().ToList();
    }
}
