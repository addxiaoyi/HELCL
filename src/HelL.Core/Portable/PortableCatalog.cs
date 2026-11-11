#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using HelL.Core;
using HelL.Core.Launcher;

namespace HelL.Portable;

public sealed record CatalogProject(string Id, string Slug, string Source, string Kind, string Title,
    string OriginalTitle, string Author, string Summary, string OriginalSummary, string Icon, long Downloads)
{
    public string Byline => $"{Author} · {(Source == "modrinth" ? "Modrinth" : "CurseForge")} · {Downloads:N0} 次下载";
    public string PreviewSummary => Summary.Length > 0 ? Summary : OriginalSummary;
}

public sealed record CatalogRelease(string Id, string Title, string Filename, string[] GameVersions,
    string[] Loaders, string Date, int RequiredDependencies, PyDict File)
{
    public string Label => $"{Title} · {Date}";
    public string Compatibility => $"Minecraft {string.Join(" / ", GameVersions.Take(8))} · {string.Join(" / ", Loaders)}";
    public override string ToString() => Label;
}

/// <summary>Typed catalog boundary for the portable UI; downloads retain upstream checksums.</summary>
public sealed class PortableCatalog
{
    private static readonly Regex ProjectKey = new(@"\A[A-Za-z0-9_-]{1,128}\z");

    public CatalogTarget Target(string instance, string version)
    {
        var game = ExistingInstance(instance);
        if (!game.InstalledIds().Contains(version) || !File.Exists(Path.Combine(game.VersionsDir(), version, version + ".jar")) ||
            File.Exists(Path.Combine(game.VersionsDir(), version, ".portable-install-pending")))
            throw new InvalidOperationException("请先为目标实例安装完整的 Minecraft 版本。");
        var target = CatalogTarget.Inspect(game, version);
        if (target.Error.Length > 0 || target.GameVersion.Length == 0)
            throw new InvalidDataException("目标版本信息不可用，请修复游戏安装后重试。");
        return target;
    }

    public IReadOnlyList<CatalogProject> Search(string kind, string query, string source, CatalogTarget? target, CancellationToken token)
    {
        var filters = PyDict.Of("game_version", target?.GameVersion ?? "", "loader", target?.Loader ?? "");
        var rows = CatalogSearch.Search(kind, query, source, filters, request =>
        {
            token.ThrowIfCancellationRequested();
            var dm = Downloads(token);
            string loader = kind == "mod" ? CatalogLoader.Normalize(target?.Loader ?? "") : "";
            return request.Source == "modrinth"
                ? Mods.SearchModrinthProjects(dm, request.Query, request.Kind, 30, target?.GameVersion ?? "", new List<object>(), loader)
                : Mods.SearchCurseforge(dm, request.Query, 30, Config.CurseforgeKey(), CatalogFiles.KIND_CF[kind], "", target?.GameVersion ?? "", new List<object>(), loader);
        });
        token.ThrowIfCancellationRequested();
        return rows.OfType<PyDict>().Select(row =>
        {
            CatalogPresentation.Localize(row);
            var art = CatalogPresentation.Artwork(row);
            string original = Py.S(Py.Or(row.Get("title"), row.Get("name")));
            return new CatalogProject(Py.S(row.Get("id")), Py.S(row.Get("slug")), Py.S(row.Get("source")), kind,
                Py.S(Py.Or(row.Get("chinese_name"), original)), original, Py.S(Py.Or(row.Get("author"), "作者未提供")),
                Py.S(row.Get("description")), Py.S(row.Get("original_description")), Py.S(art.Get("icon_url")), Py.Int(row.Get("downloads")));
        }).ToArray();
    }

    public IReadOnlyList<CatalogRelease> Releases(CatalogProject project, CatalogTarget? target, CancellationToken token)
    {
        ValidateProject(project);
        token.ThrowIfCancellationRequested();
        var dm = Downloads(token);
        IEnumerable<CatalogRelease> files;
        if (project.Source == "modrinth")
        {
            var loaders = project.Kind == "mod" && !string.IsNullOrEmpty(target?.Loader) ? Py.L(target!.Loader) : new List<object>();
            files = Mods.ListVersions(dm, project.Slug, target?.GameVersion ?? "", loaders).OfType<PyDict>().Select(row =>
            {
                var file = Mods._primary_file(row) as PyDict ?? new PyDict();
                return new CatalogRelease(Py.S(row.Get("id")), Py.S(Py.Or(row.Get("name"), row.Get("version_number"))),
                    Py.S(file.Get("filename")), Strings(row.Get("game_versions")), Strings(row.Get("loaders")),
                    Py.S(row.Get("date_published")).Split('T')[0],
                    Py.ListOr(row.Get("dependencies")).OfType<PyDict>().Count(item => Py.S(item.Get("dependency_type")) == "required"), file.Copy());
            });
        }
        else
        {
            files = Mods.CfFiles(dm, project.Id, Config.CurseforgeKey(), target?.GameVersion ?? "", "", 50).OfType<PyDict>().Select(file =>
            {
                var compatibility = CatalogFiles.SplitCfGameVersions(file.Get("gameVersions"));
                return new CatalogRelease(Py.S(file.Get("id")), Py.S(file.Get("displayName")), Py.S(file.Get("fileName")),
                    compatibility[0].Select(Py.S).ToArray(), compatibility[1].Select(Py.S).ToArray(), Py.S(file.Get("fileDate")).Split('T')[0],
                    Py.ListOr(file.Get("dependencies")).OfType<PyDict>().Count(item => Py.Int(item.Get("relationType")) == 3), file.Copy());
            });
        }
        var releases = files.Where(file => file.Filename.Length > 0 && (target == null || Compatible(project.Kind, file, target)))
            .OrderByDescending(file => file.Date, StringComparer.Ordinal).ToArray();
        token.ThrowIfCancellationRequested();
        return releases;
    }

    public static bool Compatible(string kind, CatalogRelease release, CatalogTarget target) =>
        release.GameVersions.Contains(target.GameVersion, StringComparer.Ordinal) &&
        (kind != "mod" || target.Loader.Length > 0 && release.Loaders.Contains(target.Loader, StringComparer.OrdinalIgnoreCase));

    public string Download(CatalogProject project, CatalogRelease selected, string instance, string version,
        IProgress<InstallProgress> progress, CancellationToken token, string world = "")
    {
        token.ThrowIfCancellationRequested();
        var game = ExistingInstance(instance);
        var target = project.Kind == "modpack" ? null : Target(instance, version);
        // Requery the project so a stale or unrelated file cannot be installed from a detached UI selection.
        var release = Releases(project, target, token).FirstOrDefault(file => file.Id == selected.Id)
            ?? throw new InvalidOperationException("所选文件已不可用或不兼容，请刷新版本列表后重试。");
        if (project.Source == "curseforge" && !Py.ListOr(release.File.Get("hashes")).OfType<PyDict>()
            .Any(hash => Py.Int(hash.Get("algo")) == 1 && Regex.IsMatch(Py.S(hash.Get("value")), @"\A[0-9a-fA-F]{40}\z")))
            throw new InvalidDataException("上游没有提供可用的 SHA-1 校验值，请选择其他文件或来源。");
        string folder = Destination(project.Kind, game, version, world);
        string destination = SafeDestination(folder, release.Filename);
        string extension = Path.GetExtension(release.Filename).ToLowerInvariant();
        if (project.Kind == "mod" ? extension != ".jar" : project.Kind == "modpack" ? extension is not (".zip" or ".mrpack") : extension != ".zip")
            throw new InvalidDataException("上游文件扩展名与资源类型不匹配，未写入文件。");
        RequireNoConflict(destination, release.File, project.Source);
        var dm = Downloads(token, progress);
        if (project.Source == "modrinth")
            CatalogDownload.Modrinth(dm, Mods.ModrinthDownloadUrls(Py.S(release.File.Get("url"))).Cast<object>(), destination, release.File);
        else
            CatalogDownload.CurseForge(dm, Mods.CfModDownloadUrls(project.Id, release.Id, release.Filename, release.File.Get("downloadUrl")), destination, release.File);
        token.ThrowIfCancellationRequested();
        if (project.Kind == "world")
        {
            Worlds.InstallWorld(dm, PyDict.Of("path", destination), game, version_id: version);
            return "世界已导入。进入游戏后可在单人游戏中选择。";
        }
        if (project.Kind == "modpack") return "整合包已保存到实例 downloads 目录。加载器安装与整合包启动仍待迁移。";
        if (project.Kind == "datapack") return $"数据包已安装到存档「{world}」，重新进入世界后生效。";
        if (project.Kind == "mod") return $"模组文件已保存。此版本含 {release.RequiredDependencies} 项必需依赖，请另行安装；加载器启动请使用完整版。";
        return "下载校验完成。请进入游戏，在资源包或光影设置中启用。";
    }

    internal static string SafeDestination(string folder, string filename)
    {
        if (string.IsNullOrWhiteSpace(filename) || filename is "." or ".." || filename.IndexOfAny(new[] { '/', '\\', ':', '\0', '<', '>', '|', '?', '*' }) >= 0 || filename.Any(char.IsControl) ||
            filename.EndsWith('.') || filename.EndsWith(' ') || Regex.IsMatch(filename, @"\A(?:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase))
            throw new InvalidDataException("上游文件名无效，未写入任何文件。");
        string destination = Path.Combine(folder, filename);
        DependencyTarget.RequireLocalPath(destination);
        return destination;
    }

    private static void RequireNoConflict(string destination, PyDict file, string source)
    {
        if (!File.Exists(destination)) return;
        string expected, actual;
        using var stream = File.OpenRead(destination);
        if (source == "modrinth")
        {
            var hashes = file.Get("hashes") as PyDict ?? file;
            expected = Py.S(hashes.Get("sha512"));
            if (expected.Length > 0) actual = Convert.ToHexString(SHA512.HashData(stream));
            else { expected = Py.S(hashes.Get("sha1")); actual = Convert.ToHexString(SHA1.HashData(stream)); }
        }
        else
        {
            expected = Py.S(Py.ListOr(file.Get("hashes")).OfType<PyDict>().First(hash => Py.Int(hash.Get("algo")) == 1).Get("value"));
            actual = Convert.ToHexString(SHA1.HashData(stream));
        }
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            throw new IOException("目标目录已有同名但内容不同的文件。请先备份或重命名该文件，再重试下载。");
    }

    public string[] WorldsForTarget(string instance, string version)
    {
        Target(instance, version);
        var game = ExistingInstance(instance);
        string saves = Worlds.SavesRoot(game, version);
        DependencyTarget.RequireLocalPath(saves);
        return !Directory.Exists(saves) ? Array.Empty<string>() : Directory.EnumerateDirectories(saves)
            .Where(path => (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0 && File.Exists(Path.Combine(path, "level.dat")))
            .Select(path => Path.GetFileName(path)!).Order(StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private string Destination(string kind, Instance game, string version, string world)
    {
        if (kind == "datapack")
        {
            if (!WorldsForTarget(game.name, version).Contains(world, StringComparer.Ordinal))
                throw new ArgumentException("请选择此实例下已有的存档，未写入数据包。");
            return Path.Combine(Worlds.SavesRoot(game, version), world, "datapacks");
        }
        string folder = kind switch { "mod" => "mods", "resourcepack" => "resourcepacks", "shader" => "shaderpacks",
            "world" or "modpack" => "downloads", _ => throw new ArgumentException("资源类型无效。") };
        return Path.Combine(kind == "modpack" ? game.path : VersionSettings.GameDir(game, version), folder);
    }

    private static void ValidateProject(CatalogProject project)
    {
        if (project.Source is not ("modrinth" or "curseforge") || !CatalogFiles.KIND_CF.ContainsKey(project.Kind) ||
            !ProjectKey.IsMatch(project.Source == "modrinth" ? project.Slug : project.Id)) throw new ArgumentException("资源来源或项目标识无效。");
    }
    private static Instance ExistingInstance(string name) => Instances.ListLegacyInstances().Select(Py.S).Contains(name)
        ? new Instance(name) : throw new ArgumentException("目标实例已不存在，请重新选择。");
    private static string[] Strings(object? value) => value == null ? Array.Empty<string>() : Py.ListOr(value).Select(Py.S).ToArray();
    private static DownloadManager Downloads(CancellationToken token, IProgress<InstallProgress>? progress = null) =>
        new(2L, (message, done, total) => progress?.Report(new(message, done, total)), () => token.IsCancellationRequested, token: token);
}
