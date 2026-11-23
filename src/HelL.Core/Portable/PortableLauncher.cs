#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HelL.Core;
using HelL.Core.Launcher;

namespace HelL.Portable;

public sealed record InstanceEntry(string Name, string[] Versions)
{
    public string Description => Versions.Length == 0 ? "尚未安装版本" : string.Join(" · ", Versions);
    public override string ToString() => Name;
}

public sealed record InstallProgress(string Message, long Completed = 0, long Total = 0)
{
    public double Percent => Total <= 0 ? 0 : Math.Clamp(100.0 * Completed / Total, 0, 100);
}

/// <summary>Launch operations shared by the desktop client and isolated verification tools.</summary>
public sealed partial class PortableLauncher
{
    private readonly Func<Installer, string, string, string?, PyDict, string> installGame;

    public PortableLauncher(string root) : this(root, (installer, version, loader, loaderVersion, extras)
        => GameInstall.InstallGame(installer, version, loader, loaderVersion ?? "", extras)) { }

    internal PortableLauncher(string root, Func<Installer, string, string, string?, PyDict, string> installGame)
    {
        this.installGame = installGame ?? throw new ArgumentNullException(nameof(installGame));
        Utils.SetRoot(root);
        Config.Reset();
        Config.CONFIG.Set("auto_check_update", false);
        Config.CONFIG.Set("feedback_consent", false);
        Config.CONFIG.Save();
    }

    public IReadOnlyList<InstanceEntry> InstancesList() => Instances.ListLegacyInstances()
        .Select(Py.S).Select(name => new Instance(name))
        .Select(instance => new InstanceEntry(instance.name, instance.InstalledIds().Select(Py.S)
            .Where(id => IsPlayableVersion(instance, id)).ToArray()))
        .OrderBy(instance => instance.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();

    public InstanceEntry CreateInstance(string name)
    {
        name = name.Trim();
        if (name.Length == 0 || name.Length > 48 || Instances.SanitizeInstanceName(name) != name || name == Instances.RootName())
            throw new ArgumentException("实例名称应为 1–48 个字符，不含路径符号或系统保留名称。");
        var instance = new Instance(name, forCreation: true);
        instance.Create();
        return new InstanceEntry(instance.name, Array.Empty<string>());
    }

    public void SelectInstance(string name)
    {
        RequireInstance(name);
        Config.CONFIG.Commit(PyDict.Of("default_instance", name));
    }

    public string SelectedInstance => Py.S(Config.CONFIG.Get("default_instance"));

    public string JavaPreference(string name)
    {
        string preference = RequireInstance(name).JavaPref();
        return LaunchJava.Automatic(preference) ? "" : preference;
    }

    public string[] RemoteVersions(CancellationToken token)
    {
        var manifest = Manifest.ListRemoteVersions(Downloads(token));
        token.ThrowIfCancellationRequested();
        return manifest.Values.OfType<PyDict>().Where(row => Py.S(row.Get("type")) == "release")
            .OrderByDescending(row => Py.S(row.Get("releaseTime")), StringComparer.Ordinal)
            .Select(row => Py.S(row.Get("id"))).ToArray();
    }

    public string Install(string name, string version, IProgress<InstallProgress> progress, CancellationToken token)
        => Install(name, version, "none", null, progress, token);

    public PortableLaunchOptions LaunchOptions(string name)
        => PortableLaunchOptions.Load(Path.Combine(RequireInstance(name).path, "portable-launch.json"));

    public void SaveLaunchOptions(string name, PortableLaunchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Save(Path.Combine(RequireInstance(name).path, "portable-launch.json"));
    }

    public string[] DetectJava() => Java.AllJavas().OfType<PyDict>()
        .Select(row => Py.S(row.Get("exe"))).Where(File.Exists).Distinct().ToArray();

    public McLauncher.LaunchCommand PrepareDemo(string name, string version, string javaPath,
        IProgress<InstallProgress> progress, CancellationToken token)
        => PrepareLaunch(name, version, javaPath, progress, token, null);

    public McLauncher.LaunchCommand PrepareLaunch(string name, string version, string javaPath,
        IProgress<InstallProgress> progress, CancellationToken token, PyDict? accountProps)
    {
        var instance = RequireInstance(name);
        RequireVersionId(version);
        if (!IsPlayableVersion(instance, version))
            throw new InvalidOperationException("请先完成所选游戏版本的安装。");
        var metadata = ResolveLaunchMetadata(instance, version);
        var options = LaunchOptions(name).Validate();

        token.ThrowIfCancellationRequested();
        string java;
        if (!string.IsNullOrWhiteSpace(javaPath))
        {
            java = Path.GetFullPath(javaPath.Trim());
            if (!File.Exists(java)) throw new InvalidOperationException("所选 Java 文件不存在，请重新检测或选择。");
            long? major = Java.GetJavaMajor(java);
            if (!major.HasValue) throw new InvalidOperationException("无法运行所选 Java，请检查它是否适合本机系统，以及是否具有执行权限。");
            if (!Java.JavaUsableFor(metadata, java))
                throw new InvalidOperationException($"当前 Java 为 {major}，此版本需要 Java {Java.RequiredJavaMajor(metadata)} 及以上版本。");
        }
        else
        {
            java = Java.ResolveLaunchJava(metadata, dm: Downloads(token, progress),
                on_progress: (message, done, total) => progress.Report(new(message, done, total)));
            if (string.IsNullOrEmpty(java) || !Java.JavaUsableFor(metadata, java))
                throw new InvalidOperationException("Java 未通过启动检测。可再次启动以修复运行时，或手动选择兼容的 Java。");
        }
        token.ThrowIfCancellationRequested();
        instance.SetJavaPref(javaPath);
        var account = accountProps ?? PyDict.Of("name", "HelLPlayer", "uuid", Utils.OfflineUuid("HelLPlayer"),
            "token", "0", "user_type", "legacy", "xuid", "");
        return McLauncher.BuildLaunchCommand(instance, version, account, java, memory_mb: (long)options.MemoryMb,
            width: (long)options.Width, height: (long)options.Height, extra_game_args: accountProps == null ? new object[] { "--demo" } : Array.Empty<object>());
    }

    public async Task<int> RunDemoAsync(McLauncher.LaunchCommand launch, Action<int> started)
    {
        Directory.CreateDirectory(launch.game_dir);
        string logs = Path.Combine(Utils.ROOT, "logs");
        Directory.CreateDirectory(logs);
        string logPath = Path.Combine(logs, $"game-{DateTime.UtcNow:yyyyMMdd-HHmmss-ffff}.log");
        using var log = new StreamWriter(logPath);
        using var process = new Process { StartInfo = Utils.MakeStartInfo(launch.cmd, launch.game_dir) };
        if (!process.Start()) throw new IOException("Java 进程未能启动，请检查所选 Java。");
        process.StandardInput.Close();
        var gate = new SemaphoreSlim(1, 1);
        Task stdout = CopyLog(process.StandardOutput);
        Task stderr = CopyLog(process.StandardError);
        started(process.Id);
        await process.WaitForExitAsync().ConfigureAwait(false);
        await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
        gate.Dispose();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"游戏退出，代码 {process.ExitCode}。日志：{logPath}。可重新安装版本或更换 Java 后重试。");
        return process.ExitCode;

        async Task CopyLog(StreamReader reader)
        {
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                await gate.WaitAsync().ConfigureAwait(false);
                try { await log.WriteLineAsync(line).ConfigureAwait(false); }
                finally { gate.Release(); }
            }
        }
    }

    private static DownloadManager Downloads(CancellationToken token, IProgress<InstallProgress>? progress = null)
    {
        token.ThrowIfCancellationRequested();
        return new DownloadManager(on_progress: (message, done, total) => progress?.Report(new(message, done, total)),
            cancel: () => token.IsCancellationRequested, token: token);
    }

    private static Instance RequireInstance(string name)
    {
        if (!Instances.ListLegacyInstances().Select(Py.S).Contains(name))
            throw new ArgumentException("实例不存在，请刷新列表后重试。");
        return new Instance(name);
    }

    private static void RequireVersionId(string version)
    {
        if (string.IsNullOrWhiteSpace(version) || !Regex.IsMatch(version, @"\A[A-Za-z0-9][A-Za-z0-9_.+-]{0,100}\z"))
            throw new ArgumentException("请选择有效的 Minecraft 版本。");
    }

    private static string PendingInstall(Instance instance, string version) =>
        Path.Combine(instance.VersionsDir(), version, ".portable-install-pending");
}
