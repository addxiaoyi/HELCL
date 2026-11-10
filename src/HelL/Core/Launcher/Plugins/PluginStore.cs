using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HelL.Core.Launcher.Plugins;

public sealed class PluginStore
{
    public const long MaxPackageBytes = 16 * 1024 * 1024;
    private readonly string _root;
    private readonly object _gate = new();
    public PluginStore(string root) { _root = Path.GetFullPath(root); }
    public List<InstalledPlugin> List()
    {
        lock (_gate)
        {
            if (!Directory.Exists(_root)) return new();
            return Directory.GetDirectories(_root).Where(path => PluginManifest.ValidId(Path.GetFileName(path))).Select(Read).OrderBy(plugin => plugin.Manifest.Name).ToList();
        }
    }
    private static InstalledPlugin Read(string folder)
    {
        try
        {
            if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) throw new IOException("插件目录不允许使用链接。");
            string file = Path.Combine(folder, "plugin.json");
            if (!File.Exists(file) || new FileInfo(file).Length > 512 * 1024) throw new InvalidDataException("插件清单缺失或过大，请重新安装。");
            var manifest = PluginManifest.Parse(File.ReadAllText(file, Encoding.UTF8));
            if (manifest.Id != Path.GetFileName(folder)) throw new InvalidDataException("插件目录与标识不匹配。");
            return new InstalledPlugin { Manifest = manifest, Directory = folder, Enabled = !File.Exists(Path.Combine(folder, ".disabled")) };
        }
        catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is JsonException)
        { return new InstalledPlugin { Directory = folder, Manifest = new PluginManifest { Id = Path.GetFileName(folder), Name = Path.GetFileName(folder) }, Error = ex.Message }; }
    }

    public PluginManifest InspectPackage(string file, string? sha256 = null)
    {
        if (new FileInfo(file).Length > MaxPackageBytes) throw new InvalidDataException("插件包不能超过 16 MB。");
        using var stream = File.OpenRead(file);
        if (!string.IsNullOrEmpty(sha256))
        {
            using var hash = SHA256.Create();
            string actual = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "");
            if (!string.Equals(actual, sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("插件包校验失败，请重新下载。");
            stream.Position = 0;
        }
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        ValidateEntries(archive);
        var entry = archive.GetEntry("plugin.json") ?? throw new InvalidDataException("插件包根目录缺少 plugin.json。");
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        var manifest = PluginManifest.Parse(reader.ReadToEnd());
        foreach (string asset in manifest.Screenshots.Concat(manifest.Modules.Select(module => module.Settings.TryGetValue("image", out string path) ? path : "")).Where(path => path.Length > 0))
            if (archive.GetEntry(asset) == null) throw new InvalidDataException("插件包缺少声明的图片：" + asset);
        return manifest;
    }
    private static void ValidateEntries(ZipArchive archive)
    {
        if (archive.Entries.Count > 128) throw new InvalidDataException("插件包包含过多文件。");
        long bytes = 0; var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            string name = entry.FullName;
            if (!names.Add(name) || (name != "plugin.json" && name != "assets/" && !PluginManifest.SafeAsset(name))) throw new InvalidDataException("插件包包含不支持的文件或路径：" + name);
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new InvalidDataException("插件包不能包含符号链接。");
            bytes += entry.Length;
            if (bytes > MaxPackageBytes || (name == "plugin.json" && entry.Length > 512 * 1024)) throw new InvalidDataException("插件解压后超出 16 MB 限制。");
        }
    }
    public PluginManifest InstallDownload(byte[] package, string sha256, string id, string version)
    {
        if (package == null || package.LongLength > MaxPackageBytes) throw new InvalidDataException("插件包为空或超过 16 MB。");
        lock (_gate)
        {
            Directory.CreateDirectory(_root);
            // Keep staging beside the installation, where write access is already required.
            string pending = Path.Combine(_root, ".download-" + Guid.NewGuid().ToString("N") + ".zip");
            try { File.WriteAllBytes(pending, package); return InstallPackage(pending, sha256, id, version); }
            finally { if (File.Exists(pending)) File.Delete(pending); }
        }
    }
    public PluginManifest InstallPackage(string file, string? sha256 = null, string? expectedId = null, string? expectedVersion = null)
    {
        lock (_gate)
        {
            if (new FileInfo(file).Length > MaxPackageBytes) throw new InvalidDataException("插件包不能超过 16 MB。");
            Directory.CreateDirectory(_root);
            string stage = Path.Combine(_root, ".stage-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
            try
            {
                // Validate the private copy, so replacing an imported file cannot bypass verification.
                string package = Path.Combine(stage, "package.zip"); File.Copy(file, package);
                var manifest = InspectPackage(package, sha256);
                if ((expectedId != null && manifest.Id != expectedId) || (expectedVersion != null && manifest.Version != expectedVersion)) throw new InvalidDataException("下载的插件与目录清单不匹配。");
                string payload = Path.Combine(stage, "payload"); Directory.CreateDirectory(payload);
                using (var archive = ZipFile.OpenRead(package))
                    foreach (var entry in archive.Entries.Where(entry => !entry.FullName.EndsWith("/", StringComparison.Ordinal)))
                    {
                        string target = Path.Combine(payload, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!); entry.ExtractToFile(target);
                    }
                Commit(manifest, payload); return manifest;
            }
            finally { DeleteOwned(stage); }
        }
    }
    public void InstallBuiltin(PluginManifest manifest)
    {
        lock (_gate)
        {
            manifest.Validate(); Directory.CreateDirectory(_root);
            string stage = Path.Combine(_root, ".stage-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
            try { File.WriteAllText(Path.Combine(stage, "plugin.json"), JsonSerializer.Serialize(manifest, PluginManifest.Json), new UTF8Encoding(false)); Commit(manifest, stage); }
            finally { if (Directory.Exists(stage)) DeleteOwned(stage); }
        }
    }
    private void Commit(PluginManifest manifest, string stage)
    {
        string target = Owned(manifest.Id), backup = Path.Combine(_root, ".backup-" + Guid.NewGuid().ToString("N"));
        bool existed = Directory.Exists(target);
        if (existed)
        {
            if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0) throw new IOException("不能替换链接目录。");
            var previous = Read(target);
            if (previous.Error.Length == 0 && new Version(manifest.Version) < new Version(previous.Manifest.Version)) throw new InvalidDataException("安装包版本较旧，保留现有插件。");
            if (File.Exists(Path.Combine(target, ".disabled"))) File.WriteAllText(Path.Combine(stage, ".disabled"), "");
            Directory.Move(target, backup);
        }
        try { Directory.Move(stage, target); }
        catch { if (existed) Directory.Move(backup, target); throw; }
        if (existed) DeleteOwned(backup);
    }
    public void SetEnabled(string id, bool enabled)
    {
        lock (_gate)
        {
            string folder = Owned(id); var plugin = Read(folder);
            if (plugin.Error.Length > 0) throw new IOException(plugin.Error);
            string flag = Path.Combine(folder, ".disabled");
            if (enabled) File.Delete(flag); else File.WriteAllText(flag, "");
        }
    }
    public void Uninstall(string id) { lock (_gate) { string folder = Owned(id); if (Directory.Exists(folder)) DeleteOwned(folder); } }
    private string Owned(string id)
    {
        if (!PluginManifest.ValidId(id)) throw new InvalidDataException("插件标识无效。");
        return Path.Combine(_root, id);
    }
    private void DeleteOwned(string path)
    {
        string full = Path.GetFullPath(path);
        if (!full.StartsWith(_root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("插件路径超出安装目录。");
        if (Directory.Exists(full))
        {
            RejectLinks(full); Directory.Delete(full, true);
        }
    }
    private static void RejectLinks(string folder)
    {
        if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) throw new IOException("插件目录包含链接，请手动检查。");
        foreach (string child in Directory.GetDirectories(folder)) RejectLinks(child);
    }
}
