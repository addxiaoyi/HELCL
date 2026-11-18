#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using HelL.Core;
using HelL.Core.Launcher;

namespace HelL.Portable;

public sealed record LoaderVersion(string Id, string Label, bool Stable)
{
    public override string ToString() => Label + (Stable ? "" : " · 预发布");
}

public sealed partial class PortableLauncher
{
    private const string InstallJournal = ".portable-install.json";
    private const long MaxInstallJournalBytes = 1024 * 1024;
    private sealed record InstallAttempt(int Schema, string Version, string Loader, string? LoaderVersion,
        Dictionary<string, string> ReadyVersions, string PreviousVersion);

    public IReadOnlyList<LoaderVersion> LoaderVersions(string version, string loader, CancellationToken token)
    {
        RequireVersionId(version);
        loader = NormalizeLoader(loader);
        if (loader == "none") return [];
        token.ThrowIfCancellationRequested();
        var rows = LoaderMeta.ListLoaderVersions(Downloads(token), version, loader);
        token.ThrowIfCancellationRequested();
        return rows.OfType<PyDict>().Select(row => new LoaderVersion(Py.S(row.Get("id")),
            Py.S(row.Get("label", row.Get("id"))), Py.Truthy(row.Get("stable"))))
            .Where(row => row.Id.Length > 0).ToArray();
    }

    public string Install(string name, string version, string loader, string? loaderVersion,
        IProgress<InstallProgress> progress, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(progress);
        var instance = RequireInstance(name);
        RequireVersionId(version);
        loader = NormalizeLoader(loader);
        if (loader != "none" && string.IsNullOrWhiteSpace(loaderVersion))
            throw new ArgumentException("请先获取并选择加载器版本。", nameof(loaderVersion));
        if (loaderVersion != null) RequireVersionId(loaderVersion);
        token.ThrowIfCancellationRequested();

        using var installLease = new FileStream(Path.Combine(instance.path, ".portable-install.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        string journal = Path.Combine(instance.path, InstallJournal);
        var attempt = ReadAttempt(instance);
        if (attempt != null && (attempt.Version != version || attempt.Loader != loader || attempt.LoaderVersion != loaderVersion))
            throw new InvalidOperationException($"先重试上次未完成的安装：Minecraft {attempt.Version} / {attempt.Loader} {attempt.LoaderVersion}。");
        if (attempt == null)
        {
            var ready = instance.InstalledIds().Select(Py.S).Where(id => IsPlayableVersion(instance, id))
                .ToDictionary(id => id, id => VersionFingerprint(instance, id), StringComparer.Ordinal);
            attempt = new InstallAttempt(1, version, loader, loaderVersion, ready, Py.S(instance.Meta().Get("mc_version")));
            WriteAttempt(journal, attempt);
        }

        // The loader's version ID is supplied by upstream metadata, so the journal guards
        // every new or changed profile until the entire installation has finished.
        var downloads = Downloads(token, progress);
        var installer = new Installer(instance, downloads,
            (message, done, total) => progress.Report(new(message, done, total)), () => token.IsCancellationRequested);
        var extras = loader == "optifine" ? PyDict.Of("optifine_version", loaderVersion ?? "") : new PyDict();
        try
        {
            string installed = installGame(installer, version, loader, loaderVersion, extras);
            RequireVersionId(installed);
            token.ThrowIfCancellationRequested();
            ResolveLaunchMetadata(instance, installed, checkJournal: false);
            instance.SetMeta("mc_version", installed);
            foreach (string id in instance.InstalledIds().Select(Py.S))
                if (id == version || id == installed) File.Delete(PendingInstall(instance, id));
            File.Delete(journal);
            return installed;
        }
        catch
        {
            instance.SetMeta("mc_version", attempt.PreviousVersion);
            throw;
        }
    }

    private static string NormalizeLoader(string loader)
    {
        string normalized = (loader ?? "").Trim().ToLowerInvariant();
        return normalized switch
        {
            "" or "none" => "none",
            "fabric" or "forge" or "quilt" or "neoforge" or "optifine" or "liteloader" => normalized,
            _ => throw new ArgumentException("不支持所选加载器。", nameof(loader))
        };
    }

    private static bool IsPlayableVersion(Instance instance, string id)
    {
        try { ResolveLaunchMetadata(instance, id); return true; }
        catch (Exception error) when (error is ArgumentException or IOException or InvalidDataException or InvalidOperationException or DownloadError)
        {
            System.Diagnostics.Trace.TraceWarning($"PortableLauncher: unavailable profile {instance.name}/{id}: {error.Message}");
            return false;
        }
    }

    internal static PyDict ResolveLaunchMetadata(Instance instance, string id, bool checkJournal = true)
    {
        RequireVersionId(id);
        PyDict metadata = LoadVersion(id);
        var resolved = Manifest.ResolveInherits(metadata, LoadVersion);
        string client = McLauncher.ClientJarPath(instance, id, metadata, resolved);
        if (!File.Exists(client)) throw new InvalidDataException("游戏客户端文件缺失，请重新安装此版本。");
        return resolved;

        PyDict LoadVersion(string version)
        {
            RequireVersionId(version);
            if (!instance.HasVersion(version) || (checkJournal && File.Exists(PendingInstall(instance, version))))
                throw new InvalidDataException("版本或其父版本尚未完成安装：" + version);
            if (checkJournal && ReadAttempt(instance) is { } pending &&
                (!pending.ReadyVersions.TryGetValue(version, out string? fingerprint) ||
                 fingerprint != VersionFingerprint(instance, version)))
                throw new InvalidDataException("此版本仍有未完成的安装，请重试上次安装。");
            var profile = instance.VersionJson(version) as PyDict
                ?? throw new InvalidDataException("版本信息损坏：" + version);
            if (Py.Truthy(profile.Get("jar"))) RequireVersionId(Py.S(profile.Get("jar")));
            if (Py.Truthy(profile.Get("inheritsFrom"))) RequireVersionId(Py.S(profile.Get("inheritsFrom")));
            return profile;
        }
    }

    private static string VersionFingerprint(Instance instance, string id)
    {
        RequireVersionId(id);
        string folder = Path.Combine(instance.VersionsDir(), id);
        string json = Path.Combine(folder, id + ".json");
        string jar = Path.Combine(folder, id + ".jar");
        string metadataHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(json)));
        var client = new FileInfo(jar);
        return metadataHash + ":" + (client.Exists ? client.Length + ":" + client.LastWriteTimeUtc.Ticks : "parent");
    }

    private static InstallAttempt? ReadAttempt(Instance instance)
    {
        string path = Path.Combine(instance.path, InstallJournal);
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > MaxInstallJournalBytes) throw new InvalidDataException("安装恢复记录过大，请备份后修复。");
        try
        {
            var saved = JsonSerializer.Deserialize<InstallAttempt>(File.ReadAllText(path));
            if (saved is not { Schema: 1, ReadyVersions: not null }) throw new InvalidDataException("安装恢复记录格式不受支持。");
            return saved;
        }
        catch (JsonException error) { throw new InvalidDataException("安装恢复记录损坏，请保留记录并修复后重试。", error); }
    }

    private static void WriteAttempt(string path, InstallAttempt attempt)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(attempt));
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
