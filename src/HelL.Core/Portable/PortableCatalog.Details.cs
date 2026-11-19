using System;
using System.IO;
using System.Linq;
using System.Threading;
using HelL.Core;
using HelL.Core.Launcher;

namespace HelL.Portable;

public sealed record CatalogProjectDetails(string[] Gallery, string Body, string Changelog, string ProjectUrl);

public sealed partial class PortableCatalog
{
    public CatalogProjectDetails Details(CatalogProject project, CatalogRelease? release, CancellationToken token)
    {
        ValidateProject(project);
        token.ThrowIfCancellationRequested();
        var dm = Downloads(token);
        PyDict metadata;
        string body, changelog = "", projectUrl;
        if (project.Source == "modrinth")
        {
            metadata = ModrinthDocument(dm, "/project/" + Uri.EscapeDataString(project.Slug));
            body = CatalogDocument.Readable(Py.S(metadata.Get("body")), html: false);
            projectUrl = "https://modrinth.com/" + project.Kind + "/" + Uri.EscapeDataString(project.Slug);
            if (release is not null)
            {
                if (!ProjectKey.IsMatch(release.Id)) throw new ArgumentException("资源版本标识无效。");
                PyDict version = ModrinthDocument(dm, "/version/" + Uri.EscapeDataString(release.Id));
                if (Py.S(version.Get("project_id")) != Py.S(metadata.Get("id")))
                    throw new InvalidDataException("更新日志与当前项目不匹配，请重新选择版本。");
                changelog = CatalogDocument.Readable(Py.S(version.Get("changelog")), html: false);
            }
        }
        else
        {
            if (!long.TryParse(project.Id, out long id) || id <= 0) throw new ArgumentException("资源项目标识无效。");
            metadata = Mods.CfDetail(dm, id, Config.CurseforgeKey());
            var description = Mods.CfFetch(dm, "/mods/" + id + "/description", Config.CurseforgeKey()) as PyDict;
            body = CatalogDocument.Readable(Py.S(description?.Get("data")), html: true);
            projectUrl = SafeCurseForgeUrl(Py.S((metadata.Get("links") as PyDict)?.Get("websiteUrl")));
            if (release is not null)
            {
                if (!long.TryParse(release.Id, out long file) || file <= 0) throw new ArgumentException("资源版本标识无效。");
                var note = Mods.CfFetch(dm, "/mods/" + id + "/files/" + file + "/changelog", Config.CurseforgeKey()) as PyDict;
                changelog = CatalogDocument.Readable(Py.S(note?.Get("data")), html: true);
            }
        }
        token.ThrowIfCancellationRequested();
        string[] gallery = ProjectCompatibility.Gallery(metadata, project.Source == "curseforge")
            .OfType<PyDict>().Select(image => Py.S(image.Get("url"))).Where(url => url.Length > 0).ToArray();
        return new(gallery, body, changelog, projectUrl);
    }

    private static PyDict ModrinthDocument(DownloadManager dm, string path)
    {
        Exception? failure = null;
        foreach (string origin in Source.ModrinthApiBases())
        {
            dm.Token.ThrowIfCancellationRequested();
            try { return dm.FetchJson(origin + path, 12L, expand: false) as PyDict ?? throw new InvalidDataException("项目介绍响应无效。"); }
            catch (Exception error) when (error is not OperationCanceledException and not TaskCancelled)
            { failure = error; }
        }
        throw new IOException("无法读取项目介绍，请检查网络后重试。", failure);
    }

    private static string SafeCurseForgeUrl(string address) => Uri.TryCreate(address, UriKind.Absolute, out Uri? uri)
        && uri.Scheme == "https" && uri.UserInfo.Length == 0 && uri.IsDefaultPort
        && uri.Host is "www.curseforge.com" or "curseforge.com" ? uri.AbsoluteUri : "";
}
