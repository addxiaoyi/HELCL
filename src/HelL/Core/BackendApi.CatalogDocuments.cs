using System;
using System.Linq;
using System.Text.RegularExpressions;
using HelL.Core.Launcher;

namespace HelL.Core;

public partial class BackendApi
{
    public object save_catalog_translation(string key, int index, string original, string text, string expected, bool reset = false) =>
        PyDict.Of("text", DocumentCorrections.Save(key, index, original, text, expected, reset), "manual", !reset);

    public object catalog_document(string source, string slug = "", string id = "", string section = "description", string version = "", string game_version = "", string loader = "")
    {
        if (section is not ("description" or "changelog")) throw new ArgumentException("文档类型无效。");
        if (source == "curseforge")
        {
            if (!long.TryParse(id, out long project) || project <= 0) throw new ArgumentException("项目 ID 无效。");
            var dm = new DownloadManager(2L);
            string path = "/mods/" + project;
            if (section == "changelog")
            {
                if (version.Length == 0)
                {
                    var files = Mods.CfFiles(dm, project, Config.CurseforgeKey(), game_version.Length == 0 ? null : game_version, null, 50L).OfType<PyDict>();
                    var chosen = ProjectCompatibility.SelectCfFile(files, game_version, loader) ?? throw new InvalidOperationException("没有符合当前筛选条件的版本文件。");
                    version = Py.S(chosen.Get("id"));
                }
                if (!long.TryParse(version, out long file) || file <= 0) throw new ArgumentException("文件 ID 无效。");
                path += "/files/" + file + "/changelog";
            }
            else path += "/description";
            var response = Mods.CfFetch(dm, path, Config.CurseforgeKey()) as PyDict ?? throw new InvalidOperationException("上游未返回文档。");
            return CatalogDocument.Create(source, id, section, version, Py.S(response.Get("data")), html: true);
        }
        if (source != "modrinth" || !Regex.IsMatch(slug, @"\A[a-zA-Z0-9_.-]{1,120}\z")) throw new ArgumentException("资源来源或项目标识无效。");
        var manager = new DownloadManager(2L);
        if (section == "description")
        {
            var project = FetchModrinth(manager, "/project/" + Uri.EscapeDataString(slug)) as PyDict ?? throw new InvalidOperationException("上游未返回介绍。");
            return CatalogDocument.Create(source, Py.S(project.Get("id")), section, "", Py.S(project.Get("body")));
        }
        var releases = CatalogVersions.Modrinth(manager, slug, new PyDict()).OfType<PyDict>();
        var selected = version.Length == 0 ? ProjectCompatibility.SelectVersion(releases, game_version, loader) : releases.FirstOrDefault(row => Py.S(row.Get("id")) == version);
        if (selected == null) throw new InvalidOperationException("没有符合当前筛选条件的版本文件。");
        return CatalogDocument.Create(source, Py.S(selected.Get("project_id")), section, Py.S(selected.Get("id")), Py.S(selected.Get("changelog")));
    }

    public object translate_catalog_document(string key, int index, string original)
    {
        if (_ai_busy) throw new InvalidOperationException("AI 正在回复，请稍后重试翻译。");
        try { return PyDict.Of("text", CatalogDocument.TranslatePart(key, index, original, _settings_full())); }
        catch (Exception ex)
        {
            Utils.log.Warning("资源文档翻译失败: %s", ex.ToString());
            if (ex is System.Net.Http.HttpRequestException || ex is OperationCanceledException || ex is Launcher.Ai.AiRequestException)
                throw new InvalidOperationException("本地模型服务未响应，请确认服务正在运行后重试。", ex);
            throw;
        }
    }
}
