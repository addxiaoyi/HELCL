using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace HelL.Core.Launcher;

public sealed class InstallationPlan
{
    public string Destination { get; private set; } = "";
    public long? DownloadBytes { get; private set; }
    public long? FreeBytes { get; private set; }
    public bool AlreadyInstalled { get; private set; }
    public string ExistingPolicy { get; private set; } = "校验通过的已有文件会复用；需要替换的文件先下载校验，不删除存档。";
    public string Scope { get; private set; } = "当前平台客户端与库的已公布大小，不含游戏资源、Java 与额外加载器。缺少大小的文件未计入；实际下载量可能因复用而减少。";
    public bool SpaceKnownInsufficient => DownloadBytes.HasValue && FreeBytes.HasValue && DownloadBytes.Value > FreeBytes.Value;
    public string Summary => "目标：" + Destination + "\n已公布下载量：" + Bytes(DownloadBytes)
        + "\n磁盘可用：" + Bytes(FreeBytes) + "\n" + Scope + "\n" + ExistingPolicy
        + (AlreadyInstalled ? "\n目标版本已经存在，将先校验并复用文件。" : "")
        + (SpaceKnownInsufficient ? "\n磁盘空间不足，请先清理或选择其它目标。" : "");

    public static InstallationPlan ForVersion(Instance instance, string version, string loader = "", CancellationToken cancellation = default)
    {
        if (instance is null) throw new ArgumentNullException(nameof(instance));
        if (string.IsNullOrWhiteSpace(version) || version.Length > 128 || version.IndexOfAny(new[] { '/', '\\', ':', '\0' }) >= 0)
            throw new ArgumentException("安装版本标识无效。", nameof(version));
        cancellation.ThrowIfCancellationRequested();
        DependencyTarget.RequireLocalPath(instance.path);
        var manager = new DownloadManager(2L, cancel: () => cancellation.IsCancellationRequested, token: cancellation);
        PyDict metadata = Manifest.GetVersionJson(manager, version);
        var plan = FromMetadata(instance.path, version, metadata);
        if (loader.Length > 0 && loader is not ("none" or "vanilla"))
            plan.Scope += " 本次还会安装 " + loader + "，其文件大小未包含在该估算中。";
        cancellation.ThrowIfCancellationRequested();
        return plan;
    }

    internal static InstallationPlan FromMetadata(string folder, string version, PyDict metadata)
    {
        string root = Path.GetFullPath(folder);
        var files = new List<PyDict>();
        if ((metadata.Get("downloads") as PyDict)?.Get("client") is PyDict client) files.Add(client);
        foreach (PyDict library in Py.ListOr(metadata.Get("libraries")).OfType<PyDict>())
        {
            if (!Utils.CheckRules(library.Get("rules"))) continue;
            if (library.Get("downloads") is not PyDict downloads) continue;
            if (downloads.Get("artifact") is PyDict artifact) files.Add(artifact);
            string classifier = Installer.SelectNativeClassifier(library);
            if (classifier is not null && downloads.Get("classifiers") is PyDict natives && natives.Get(classifier) is PyDict native)
                files.Add(native);
        }
        long total = 0;
        bool known = false;
        foreach (PyDict file in files)
        {
            if (!long.TryParse(Py.S(file.Get("size")), out long size) || size < 0) continue;
            total = checked(total + size);
            known = true;
        }
        long? free = null;
        try { free = new DriveInfo(Path.GetPathRoot(root)).AvailableFreeSpace; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { }
        return new InstallationPlan
        {
            Destination = Path.Combine(root, "versions", version),
            AlreadyInstalled = File.Exists(Path.Combine(root, "versions", version, version + ".json")),
            DownloadBytes = known ? total : null,
            FreeBytes = free
        };
    }

    private static string Bytes(long? bytes) => bytes.HasValue ? (bytes.Value / 1024d / 1024d).ToString("N1") + " MiB" : "暂不可用";
}
