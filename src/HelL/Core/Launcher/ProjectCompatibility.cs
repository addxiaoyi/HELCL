using System;
using System.Collections.Generic;
using System.Linq;

namespace HelL.Core.Launcher;

public static class ProjectCompatibility
{
    public static PyDict? SelectVersion(IEnumerable<PyDict> versions, string gameVersion, string loader)
    {
        return versions.Where(version =>
                (gameVersion.Length == 0 || Py.ListOr(version.Get("game_versions")).Any(value => Py.S(value) == gameVersion)) &&
                (loader.Length == 0 || Py.ListOr(version.Get("loaders")).Any(value => string.Equals(Py.S(value), loader, StringComparison.OrdinalIgnoreCase))))
            .OrderByDescending(version => Py.S(version.Get("date_published")), StringComparer.Ordinal).FirstOrDefault();
    }

    public static PyDict? SelectCfFile(IEnumerable<PyDict> files, string gameVersion, string loader)
    {
        return files.Where(file =>
        {
            var supported = CatalogFiles.SplitCfGameVersions(file.Get("gameVersions"));
            return (gameVersion.Length == 0 || supported[0].Any(value => Py.S(value) == gameVersion)) &&
                (loader.Length == 0 || supported[1].Any(value => string.Equals(Py.S(value), loader, StringComparison.OrdinalIgnoreCase)));
        }).OrderByDescending(file => Py.S(file.Get("fileDate")), StringComparer.Ordinal).FirstOrDefault();
    }

    public static PyDict CfSupport(IEnumerable<PyDict> files)
    {
        var supported = files.Select(file => CatalogFiles.SplitCfGameVersions(file.Get("gameVersions"))).ToList();
        return PyDict.Of("game_versions", supported.SelectMany(parts => parts[0]).Distinct().ToList(),
            "loaders", supported.SelectMany(parts => parts[1]).Distinct().ToList());
    }

    public static string CfDependencyKind(object relation) => Py.S(relation) switch
    {
        "3" => "required", "2" => "optional", "5" => "incompatible", "1" => "embedded", _ => "unknown"
    };

    public static List<object> Gallery(PyDict project, bool curseforge = false)
    {
        var images = Py.ListOr(project.Get(curseforge ? "screenshots" : "gallery")).Select(image => image as PyDict ?? PyDict.Of("url", Py.S(image)));
        return images.OrderByDescending(image => Py.Truthy(image.Get("featured"))).Take(30)
            .Select(image => PyDict.Of("url", CatalogPresentation.ImageUrl(Py.S(image.Get("raw_url") ?? image.Get("url"))), "title", News.PlainText(Py.S(image.Get("title")), 160)))
            .Where(image => Py.S(image.Get("url")).Length > 0).Cast<object>().ToList();
    }

    public static string DependencyKind(string kind) => kind switch
    {
        "required" => "必需", "optional" => "可选", "incompatible" => "不兼容", "embedded" => "已内嵌", _ => "未注明"
    };
}
