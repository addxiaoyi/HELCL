using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HelL.Core.Launcher;

namespace HelL.Core;

public partial class BackendApi
{
    public object catalog_summaries(object projects)
    {
        var rows = Py.ListOr(projects).OfType<PyDict>().Take(12).Select(row => (object)row.Copy()).ToList();
        try
        {
            if (_ai_busy) throw new InvalidOperationException("AI 正在回复，稍后可重试翻译。");
            return PyDict.Of("rows", CatalogPresentation.Translate(rows, _settings_full()), "error", "");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning("Catalog summary: " + ex);
            foreach (var row in rows.OfType<PyDict>()) CatalogPresentation.Localize(row);
            return PyDict.Of("rows", rows, "error", "中文摘要暂不可用，可在详情中查看原文并重试。");
        }
    }

    public object catalog_details(string source, string slug = "", string id = "")
    {
        var dm = new DownloadManager(2L);
        PyDict row;
        if (source.StartsWith("curse", StringComparison.OrdinalIgnoreCase))
        {
            if (!long.TryParse(id, out long addon) || addon <= 0) throw new ArgumentException("项目 ID 无效。");
            var project = Mods.CfDetail(dm, addon);
            row = _content_row(project, "curseforge");
            row["name"] = project.Get("name");
            row["slug"] = project.Get("slug");
            row["description"] = project.Get("summary");
            row["original_description"] = project.Get("summary");
            row["author"] = string.Join(", ", Py.ListOr(project.Get("authors")).OfType<PyDict>().Select(author => Py.S(author.Get("name"))).Where(name => name.Length > 0));
            row["icon_url"] = CatalogPresentation.ImageUrl(Py.S((project.Get("logo") as PyDict)?.Get("url")));
            row["cover_url"] = Py.ListOr(project.Get("screenshots")).OfType<PyDict>().Select(image => CatalogPresentation.ImageUrl(Py.S(image.Get("url")))).FirstOrDefault(url => url.Length > 0) ?? "";
            row["project_url"] = Py.S((project.Get("links") as PyDict)?.Get("websiteUrl"));
            row["downloads"] = project.Get("downloadCount", 0L);
            row["gallery"] = ProjectCompatibility.Gallery(project, true);
            var supported = ProjectCompatibility.CfSupport(Py.ListOr(project.Get("latestFiles")).OfType<PyDict>());
            row["game_versions"] = supported.Get("game_versions");
            row["loaders"] = supported.Get("loaders");
            row["kind"] = CatalogFiles.KIND_CF.FirstOrDefault(pair => pair.Value == Py.Int(project.Get("classId"))).Key ?? "mod";
        }
        else
        {
            if (!Regex.IsMatch(slug, @"\A[a-zA-Z0-9_.-]{1,120}\z")) throw new ArgumentException("项目名称无效。");
            var project = FetchModrinth(dm, "/project/" + Uri.EscapeDataString(slug)) as PyDict ?? throw new InvalidOperationException("项目详情无效。");
            row = _content_row(project, "modrinth");
            row["original_description"] = News.PlainText(Py.S(project.Get("description")), 600);
            row["cover_url"] = CatalogPresentation.Cover(project);
            string kind = Py.S(project.Get("project_type", "mod"));
            if (kind is not ("mod" or "modpack" or "shader" or "resourcepack" or "datapack")) kind = "mod";
            row["kind"] = kind;
            row["project_url"] = "https://modrinth.com/" + kind + "/" + Uri.EscapeDataString(Py.S(project.Get("slug", slug)));
            row["updated"] = Py.S(project.Get("updated")).Split('T')[0];
            row["gallery"] = ProjectCompatibility.Gallery(project);
            row["loaders"] = project.Get("loaders", new List<object>());
            row["game_versions"] = project.Get("game_versions", new List<object>());
            try
            {
                row["author"] = CatalogPresentation.ModrinthAuthors(dm, project.Get("id") ?? slug);
            }
            catch (Exception ex) { row["author"] = ""; row["author_error"] = "作者信息暂不可用"; System.Diagnostics.Trace.TraceWarning("Catalog authors: " + ex.Message); }
        }
        CatalogPresentation.Localize(row);
        return row;
    }

    public object catalog_compatibility(string source, string slug = "", string id = "", string game_version = "", string loader = "")
    {
        var dm = new DownloadManager(2L);
        if (source.StartsWith("curse", StringComparison.OrdinalIgnoreCase))
        {
            if (!long.TryParse(id, out long addon) || addon <= 0) throw new ArgumentException("项目 ID 无效。");
            var files = Mods.CfFiles(dm, addon, null, game_version.Length == 0 ? null : game_version, null, 50L).OfType<PyDict>().ToList();
            var selected = ProjectCompatibility.SelectCfFile(files, game_version, loader);
            if (selected == null) return PyDict.Of("error", "没有符合当前版本和加载器的文件，请调整条件。");
            var supported = CatalogFiles.SplitCfGameVersions(selected.Get("gameVersions"));
            var dependencies = new List<object>();
            var declared = Py.ListOr(selected.Get("dependencies")).OfType<PyDict>().ToList();
            foreach (var dependency in declared.Take(30))
            {
                string name = "项目 " + Py.S(dependency.Get("modId")), url = "", kindOfProject = "";
                try { var detail = Mods.CfDetail(dm, dependency.Get("modId")); name = Py.S(detail.Get("name")); url = Py.S((detail.Get("links") as PyDict)?.Get("websiteUrl")); kindOfProject = Py.S(detail.Get("classId")) == "6" ? "mod" : ""; }
                catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("Dependency lookup: " + ex.Message); }
                string kind = ProjectCompatibility.CfDependencyKind(dependency.Get("relationType"));
                dependencies.Add(PyDict.Of("name", name, "kind", kind, "url", url, "source", "curseforge", "project_id", Py.S(dependency.Get("modId")), "version_id", Py.S(dependency.Get("fileId")), "project_type", kindOfProject));
            }
            LocalizeDependencies(dependencies);
            return PyDict.Of("version", selected.Get("displayName"), "loaders", supported[1], "game_versions", supported[0], "dependencies", dependencies, "dependency_count", declared.Count);
        }
        if (!Regex.IsMatch(slug, @"\A[a-zA-Z0-9_.-]{1,120}\z")) throw new ArgumentException("项目名称无效。");
        var filters = PyDict.Of("include_changelog", "false");
        if (game_version.Length > 0) filters["game_versions"] = PyJson.Dumps(Py.L(game_version));
        if (loader.Length > 0) filters["loaders"] = PyJson.Dumps(Py.L(loader));
        var versions = CatalogVersions.Modrinth(dm, slug, filters).OfType<PyDict>();
        var version = ProjectCompatibility.SelectVersion(versions, game_version, loader);
        if (version == null) return PyDict.Of("error", "没有符合当前版本和加载器的文件，请调整条件。");
        var needs = new List<object>();
        var declaredDependencies = Py.ListOr(version.Get("dependencies")).OfType<PyDict>().ToList();
        foreach (var dependency in declaredDependencies.Take(40))
        {
            string projectId = Py.S(dependency.Get("project_id")), versionId = Py.S(dependency.Get("version_id"));
            string name = Py.S(dependency.Get("file_name")), number = "", url = "", projectType = "";
            try
            {
                if (versionId.Length > 0)
                {
                    var detail = FetchModrinth(dm, "/version/" + Uri.EscapeDataString(versionId)) as PyDict;
                    projectId = Py.S(detail?.Get("project_id") ?? projectId); number = Py.S(detail?.Get("version_number"));
                }
                if (projectId.Length > 0)
                {
                    var project = FetchModrinth(dm, "/project/" + Uri.EscapeDataString(projectId)) as PyDict;
                    name = Py.S(project?.Get("title") ?? projectId);
                    projectType = Py.S(project?.Get("project_type"));
                    url = "https://modrinth.com/project/" + Uri.EscapeDataString(projectId);
                }
            }
            catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("Dependency lookup: " + ex.Message); }
            if (name.Length == 0) name = projectId.Length > 0 ? projectId : versionId;
            needs.Add(PyDict.Of("name", name, "kind", dependency.Get("dependency_type"), "version", number, "url", url,
                "source", "modrinth", "project_id", projectId, "version_id", versionId, "project_type", projectType));
        }
        LocalizeDependencies(needs);
        return PyDict.Of("version", version.Get("version_number"), "loaders", version.Get("loaders"), "game_versions", version.Get("game_versions"), "dependencies", needs, "dependency_count", declaredDependencies.Count);
    }

    private static void LocalizeDependencies(List<object> dependencies)
    {
        foreach (var row in dependencies.OfType<PyDict>())
        {
            var mapped = ResourceNames.Find(Py.S(row.Get("project_type")), Py.S(row.Get("source")), "", Py.S(row.Get("project_id")), Py.S(row.Get("name")));
            if (mapped != null) row["chinese_name"] = mapped.Chinese;
        }
    }

    private static object FetchModrinth(DownloadManager dm, string path)
    {
        Exception? last = null;
        foreach (string origin in Source.ModrinthApiBases())
            try { return dm.FetchJson(origin + path, 12L, expand: false); }
            catch (Exception ex) { last = ex; }
        throw new InvalidOperationException("无法读取项目资料，请检查网络后重试。", last);
    }
}
