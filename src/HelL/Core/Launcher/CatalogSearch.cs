using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HelL.Core.Launcher;

public sealed class CatalogSearchRequest
{
    public string Kind { get; init; } = "mod";
    public string Source { get; init; } = "modrinth";
    public string Query { get; init; } = "";
    public PyDict Filters { get; init; } = new();
}

public static class CatalogSearch
{
    public static List<object> Search(string kind, string query, string source, PyDict filters, Func<CatalogSearchRequest, List<object>>? fetch = null)
    {
        if (!CatalogFiles.KIND_CF.ContainsKey(kind)) throw new ArgumentException("资源类型无效。");
        string selected = (source ?? "all").ToLowerInvariant();
        if (selected is "" or "全部") selected = "all";
        if (selected.StartsWith("curse")) selected = "curseforge";
        if (selected is not ("modrinth" or "curseforge" or "all")) throw new ArgumentException("资源来源无效。");
        if (kind == "world" && selected == "modrinth") return new List<object>();
        var sources = selected == "all" ? kind == "world" ? new[] { "curseforge" } : new[] { "modrinth", "curseforge" } : new[] { selected };
        var queries = ResourceNames.Queries(kind, query);
        fetch ??= Fetch;
        var jobs = sources.SelectMany(provider => queries.Select(term => new CatalogSearchRequest
            { Kind = kind, Source = provider, Query = term, Filters = filters.Copy() })).ToArray();
        var responses = jobs.Select(request => Task.Run(() =>
        {
            try { return (Rows: fetch(request), Error: (Exception?)null); }
            catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("CatalogSearch " + request.Source + ": " + ex.Message); return (Rows: new List<object>(), Error: ex); }
        })).ToArray();
        Task.WaitAll(responses);
        if (responses.All(task => task.Result.Error != null)) throw new InvalidOperationException("资源搜索失败，请检查网络或切换来源后重试。", responses[0].Result.Error);
        var rows = responses.SelectMany(task => task.Result.Rows).OfType<PyDict>().Select(row => row.Copy()).ToList();
        if (rows.Count == 0 && responses.Any(task => task.Result.Error != null))
            throw new InvalidOperationException("部分资源搜索未完成，请检查网络或切换来源后重试。", responses.First(task => task.Result.Error != null).Result.Error);
        foreach (var row in rows) { row["kind"] = kind; ResourceNames.Localize(row); }
        string key = ResourceNames.Normalize(query);
        return Mods.RankHits(rows, query, kind).OfType<PyDict>()
            .GroupBy(row => Py.S(row.Get("source")) + ":" + Py.S(Py.Or(row.Get("id"), row.Get("slug"), row.Get("title"))))
            .Select(group => group.First()).OrderByDescending(row => MatchScore(row, key)).Take(40).Cast<object>().ToList();
    }

    private static int MatchScore(PyDict row, string query)
    {
        if (query.Length == 0) return 0;
        var names = new[] { Py.S(row.Get("chinese_name")), Py.S(row.Get("name")), Py.S(row.Get("title")), Py.S(row.Get("slug")) }
            .Concat(Py.ListOr(row.Get("search_aliases")).Select(Py.S)).Select(ResourceNames.Normalize);
        return names.Any(name => name == query) ? 2 : names.Any(name => name.Contains(query)) ? 1 : 0;
    }

    private static List<object> Fetch(CatalogSearchRequest request)
    {
        var dm = new DownloadManager(2L);
        string version = CatalogFiles._game_version(request.Filters);
        object category = Py.Or(request.Filters.Get("category"), request.Filters.Get("type"), "");
        string loader = request.Kind is "mod" or "modpack" ? CatalogLoader.Normalize(Py.S(request.Filters.Get("loader"))) : "";
        if (request.Source == "modrinth")
            return Mods.SearchModrinthProjects(dm, request.Query, request.Kind, 30, version, CatalogFiles.CategoryFacets(category), loader);
        return Mods.SearchCurseforge(dm, request.Query, 30, Config.CurseforgeKey(), CatalogFiles.KIND_CF[request.Kind], null, version, CatalogFiles.CfCategoryTokens(category), loader);
    }
}
