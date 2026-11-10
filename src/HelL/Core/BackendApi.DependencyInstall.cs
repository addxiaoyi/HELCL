using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using HelL.Core.Launcher;
using HelL.Models;

namespace HelL.Core;

public partial class BackendApi
{
    private sealed class PreparedDependency
    {
        internal string Instance = "", InstalledVersion = "", Url = "", Sha1 = "", Sha512 = "";
        internal DependencyPlan Plan = new DependencyPlan();
        internal DateTime Expires;
    }

    private readonly ConcurrentDictionary<string, PreparedDependency> _dependencyPlans = new ConcurrentDictionary<string, PreparedDependency>();

    public object dependency_targets()
    {
        var rows = new List<object>();
        foreach (string name in Instances.ListInstances().Select(Py.S))
        {
            var instance = new Instance(name);
            foreach (string version in instance.InstalledIds())
                rows.Add(PyDict.Of("instance", name, "version", version, "label", name + " · " + version));
        }
        return rows;
    }

    public object prepare_dependency(string source, string project, string pinned_version, string relation, string instance, string version)
    {
        DependencyTarget.RequireInstallable(relation);
        if (!Instances.ListInstances().Select(Py.S).Contains(instance)) throw new ArgumentException("目标实例已不存在。");
        var target = DependencyTarget.Resolve(new Instance(instance), version);
        var dm = new DownloadManager(2L);
        string title, number, filename, url, sha1, sha512 = "";
        long bytes;
        if (source == "curseforge")
        {
            if (!long.TryParse(project, out long addon) || addon <= 0) throw new ArgumentException("依赖项目 ID 无效。");
            var detail = Mods.CfDetail(dm, addon);
            if (Py.S(detail.Get("classId")) != "6") throw new ArgumentException("当前仅支持单独安装模组依赖。");
            PyDict? file;
            if (pinned_version.Length > 0)
            {
                if (!long.TryParse(pinned_version, out long fileId) || fileId <= 0) throw new ArgumentException("依赖文件 ID 无效。");
                file = (Mods.CfFetch(dm, "/mods/" + addon + "/files/" + fileId) as PyDict)?.Get("data") as PyDict;
            }
            else file = ProjectCompatibility.SelectCfFile(Mods.CfFiles(dm, addon, null, target.GameVersion).OfType<PyDict>(), target.GameVersion, target.Loader);
            if (file == null) throw new InvalidOperationException("没有兼容的依赖文件。");
            if (Py.S(file.Get("modId")) != project) throw new InvalidDataException("依赖文件不属于所选项目。");
            var supported = CatalogFiles.SplitCfGameVersions(file.Get("gameVersions"));
            target.RequireCompatible(PyDict.Of("game_versions", supported[0], "loaders", supported[1]));
            title = Py.S(detail.Get("name")); number = Py.S(file.Get("displayName")); filename = Py.S(file.Get("fileName"));
            url = Py.S(file.Get("downloadUrl")); bytes = Py.Int(file.Get("fileLength", 0L));
            sha1 = Py.S(Py.ListOr(file.Get("hashes")).OfType<PyDict>().FirstOrDefault(hash => Py.S(hash.Get("algo")) == "1")?.Get("value"));
        }
        else if (source == "modrinth")
        {
            if (!Regex.IsMatch(project, @"\A[a-zA-Z0-9_.-]{1,120}\z")) throw new ArgumentException("依赖项目 ID 无效。");
            var detail = FetchModrinth(dm, "/project/" + Uri.EscapeDataString(project)) as PyDict ?? throw new InvalidDataException("依赖项目资料无效。");
            if (Py.S(detail.Get("project_type")) != "mod") throw new ArgumentException("当前仅支持单独安装模组依赖。");
            PyDict? fileVersion;
            if (pinned_version.Length > 0)
            {
                if (!Regex.IsMatch(pinned_version, @"\A[a-zA-Z0-9]{1,120}\z")) throw new ArgumentException("依赖版本 ID 无效。");
                fileVersion = FetchModrinth(dm, "/version/" + Uri.EscapeDataString(pinned_version)) as PyDict;
            }
            else
            {
                string query = "/project/" + Uri.EscapeDataString(project) + "/version?include_changelog=false&game_versions=" + Uri.EscapeDataString(PyJson.Dumps(Py.L(target.GameVersion))) + "&loaders=" + Uri.EscapeDataString(PyJson.Dumps(Py.L(target.Loader)));
                fileVersion = ProjectCompatibility.SelectVersion(Py.ListOr(FetchModrinth(dm, query)).OfType<PyDict>(), target.GameVersion, target.Loader);
            }
            if (fileVersion == null) throw new InvalidOperationException("没有兼容的依赖文件。");
            if (Py.S(fileVersion.Get("project_id")) != Py.S(detail.Get("id"))) throw new InvalidDataException("依赖版本不属于所选项目。");
            target.RequireCompatible(fileVersion);
            var file = Mods._primary_file(fileVersion) as PyDict ?? throw new InvalidDataException("依赖版本没有可下载文件。");
            title = Py.S(detail.Get("title")); number = Py.S(fileVersion.Get("version_number")); filename = Py.S(file.Get("filename"));
            url = Py.S(file.Get("url")); bytes = Py.Int(file.Get("size", 0L));
            var hashes = file.Get("hashes") as PyDict;
            sha1 = Py.S(hashes?.Get("sha1")); sha512 = Py.S(hashes?.Get("sha512"));
        }
        else throw new ArgumentException("不支持的资源来源。");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            !(uri.Host == "cdn.modrinth.com" || uri.Host.EndsWith(".forgecdn.net", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("上游未提供可校验的直接下载地址，请查看项目主页。");
        if (!Regex.IsMatch(sha1, @"\A[0-9a-fA-F]{40}\z") && !Regex.IsMatch(sha512, @"\A[0-9a-fA-F]{128}\z"))
            throw new InvalidDataException("上游未提供有效的文件校验值，无法确认安装。");
        var plan = new DependencyPlan { Token = Guid.NewGuid().ToString("N"), Name = title, Version = number, Filename = filename,
            Destination = target.Destination(filename), GameVersion = target.GameVersion, Loader = target.Loader, Bytes = bytes };
        if (File.Exists(plan.Destination)) throw new IOException("目标目录已存在同名文件，请先在模组管理中检查，避免覆盖。");
        foreach (var cached in _dependencyPlans.Where(entry => entry.Value.Expires < DateTime.UtcNow).ToArray()) _dependencyPlans.TryRemove(cached.Key, out _);
        if (_dependencyPlans.Count >= 50) throw new InvalidOperationException("待确认的安装过多，请稍后重试。");
        _dependencyPlans[plan.Token] = new PreparedDependency { Instance = instance, InstalledVersion = version, Url = url, Sha1 = sha1, Sha512 = sha512, Plan = plan, Expires = DateTime.UtcNow.AddMinutes(10) };
        return PyDict.Of("token", plan.Token, "name", plan.Name, "version", plan.Version, "filename", plan.Filename,
            "destination", plan.Destination, "game_version", plan.GameVersion, "loader", plan.Loader, "bytes", plan.Bytes);
    }

    public object install_dependency(string token)
    {
        if (!_dependencyPlans.TryRemove(token, out var prepared) || prepared.Expires < DateTime.UtcNow)
            throw new ArgumentException("安装确认已过期，请重新选择依赖。");
        return StartTask("安装依赖 " + prepared.Plan.Name, ctx =>
        {
            var plan = prepared.Plan;
            var target = DependencyTarget.Resolve(new Instance(prepared.Instance), prepared.InstalledVersion);
            if (target.GameVersion != plan.GameVersion || target.Loader != plan.Loader || target.Destination(plan.Filename) != plan.Destination)
                throw new IOException("目标版本或隔离目录已改变，请重新确认安装。");
            if (File.Exists(plan.Destination)) throw new IOException("目标文件已存在，已停止安装以避免覆盖。");
            Directory.CreateDirectory(target.Folder);
            string staging = Path.Combine(target.Folder, ".hell-dependency-" + token + ".tmp");
            try
            {
                _dm(ctx).Download(prepared.Url, staging, prepared.Sha1, plan.Bytes > 0 ? plan.Bytes : (long?)null, sha512: prepared.Sha512);
                DependencyTarget.RequireLocalPath(plan.Destination);
                File.Move(staging, plan.Destination);
                return "依赖安装完成：" + plan.Filename;
            }
            finally
            {
                if (File.Exists(staging)) File.Delete(staging);
                if (File.Exists(staging + ".part")) File.Delete(staging + ".part");
            }
        });
    }
}
