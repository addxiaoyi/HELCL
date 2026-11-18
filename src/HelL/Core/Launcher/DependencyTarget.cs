using System;
using System.IO;
using System.Linq;

namespace HelL.Core.Launcher;

public sealed class DependencyTarget
{
    public string GameVersion { get; }
    public string Loader { get; }
    public string Folder { get; }

    private DependencyTarget(string gameVersion, string loader, string folder)
    { GameVersion = gameVersion; Loader = loader; Folder = folder; }

    public static DependencyTarget Resolve(Instance instance, string version)
    {
        if (!instance.InstalledIds().Contains(version)) throw new ArgumentException("目标游戏版本已不存在，请重新选择。");
        var metadata = instance.VersionJson(version) as PyDict ?? throw new InvalidDataException("目标版本资料不可用。");
        string game = Mods._mc_version_from_text(metadata.Get("inheritsFrom") ?? metadata.Get("id") ?? version) ?? "";
        var libraries = Py.ListOr(metadata.Get("libraries")).OfType<PyDict>().Select(row => Py.S(row.Get("name"))).ToList();
        string loader = libraries.Any(name => name.StartsWith("net.fabricmc:fabric-loader:", StringComparison.Ordinal)) ? "fabric"
            : libraries.Any(name => name.StartsWith("org.quiltmc:quilt-loader:", StringComparison.Ordinal)) ? "quilt"
            : libraries.Any(name => name.StartsWith("net.neoforged:neoforge:", StringComparison.Ordinal)) ? "neoforge"
            : libraries.Any(name => name.StartsWith("net.minecraftforge:forge:", StringComparison.Ordinal)) ? "forge" : "";
        if (game.Length == 0 || loader.Length == 0) throw new ArgumentException("请先为目标版本安装 Fabric、Forge、NeoForge 或 Quilt 加载器。");
        var settings = VersionSettings.Load(instance, version);
        string folder = VersionSettings.IsIsolated(settings) ? VersionSettings.ModsDir(instance, version, settings) : Path.Combine(instance.path, "mods");
        RequireLocalPath(folder);
        return new DependencyTarget(game, loader, folder);
    }

    public static void RequireInstallable(string relation)
    {
        if (relation != "required" && relation != "optional") throw new ArgumentException("只有必需或可选依赖可以单独安装。");
    }

    public void RequireCompatible(PyDict file)
    {
        if (ProjectCompatibility.SelectVersion(new[] { file }, GameVersion, Loader) == null)
            throw new InvalidOperationException("依赖文件与目标 Minecraft 版本或加载器不兼容，请选择其他目标。");
    }

    public string Destination(string filename)
    {
        if (!filename.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("依赖文件必须是 JAR 模组。");
        string path = Mods._download_dest(Folder, filename);
        RequireLocalPath(path);
        return path;
    }

    public static void RequireLocalPath(string path)
    {
        string? current = Path.GetFullPath(path);
        if (current.StartsWith(@"\\", StringComparison.Ordinal)) throw new IOException("依赖安装目录不能是网络共享。");
        while (!string.IsNullOrEmpty(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("依赖安装路径包含链接，请选择本地目录。");
            current = Path.GetDirectoryName(current);
        }
    }
}
