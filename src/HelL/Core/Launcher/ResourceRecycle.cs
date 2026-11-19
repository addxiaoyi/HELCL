using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace HelL.Core.Launcher;

public sealed class RecycledResource
{
    public string Receipt { get; set; } = "";
    public string Original { get; set; } = "";
    public string Recycled { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Instance { get; set; } = "";
    public string Version { get; set; } = "";
    public string Hash { get; set; } = "";
}

public static class ResourceRecycle
{
    private static readonly object Gate = new object();

    public static RecycledResource Move(Instance instance, string filename, string kind, string version = "")
    {
        if (instance is null) throw new ArgumentNullException(nameof(instance));
        ValidateFilename(filename);
        string original = Destination(instance, filename, kind, version);
        DependencyTarget.RequireLocalPath(original);
        if (!File.Exists(original)) throw new FileNotFoundException("资源文件已经不存在，请刷新列表。", filename);
        string id = Guid.NewGuid().ToString("N");
        string folder = Path.Combine(instance.path, ".hell-recycle", id);
        string payload = Path.Combine(folder, "payload");
        lock (Gate)
        {
            DependencyTarget.RequireLocalPath(folder);
            Directory.CreateDirectory(folder);
            var entry = new RecycledResource
            {
                Receipt = id,
                Original = original,
                Recycled = payload,
                Kind = kind,
                Instance = instance.name,
                Version = version,
                Hash = HashFile(original)
            };
            try
            {
                Utils.WriteJson(Path.Combine(folder, "receipt.json"), PyDict.Of("schema", 1L,
                    "instance", instance.name, "filename", filename, "kind", kind, "version", version, "sha256", entry.Hash));
                File.Move(original, payload);
                return entry;
            }
            catch
            {
                if (!File.Exists(payload))
                {
                    string record = Path.Combine(folder, "receipt.json");
                    if (File.Exists(record)) File.Delete(record);
                    if (Directory.Exists(folder)) Directory.Delete(folder);
                }
                throw;
            }
        }
    }

    public static string Restore(Instance instance, string receipt)
    {
        if (instance is null) throw new ArgumentNullException(nameof(instance));
        if (!Guid.TryParseExact(receipt, "N", out _)) throw new ArgumentException("回收记录标识无效。", nameof(receipt));
        lock (Gate)
        {
            string folder = Path.Combine(instance.path, ".hell-recycle", receipt);
            DependencyTarget.RequireLocalPath(folder);
            string record = Path.Combine(folder, "receipt.json");
            if (!File.Exists(record) || new FileInfo(record).Length > 8192) throw new InvalidDataException("回收记录不存在或已损坏。");
            var metadata = Utils.ReadJson(record) as PyDict ?? throw new InvalidDataException("回收记录已损坏。");
            if (Py.Int(metadata.Get("schema")) != 1 || Py.S(metadata.Get("instance")) != instance.name)
                throw new InvalidDataException("回收记录不属于当前实例。");
            string filename = Py.S(metadata.Get("filename"));
            ValidateFilename(filename);
            string target = Destination(instance, filename, Py.S(metadata.Get("kind")), Py.S(metadata.Get("version")));
            string payload = Path.Combine(folder, "payload");
            DependencyTarget.RequireLocalPath(target);
            DependencyTarget.RequireLocalPath(payload);
            if (File.Exists(target) || Directory.Exists(target)) throw new IOException("原位置已经有同名文件，未覆盖。请先处理冲突后重试恢复。");
            string expected = Py.S(metadata.Get("sha256"));
            if (!Regex.IsMatch(expected, @"\A[0-9a-f]{64}\z") || !File.Exists(payload) || HashFile(payload) != expected)
                throw new InvalidDataException("回收文件校验失败，未恢复或覆盖任何文件。");
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.Move(payload, target);
            File.Delete(record);
            Directory.Delete(folder);
            return target;
        }
    }

    private static string Destination(Instance instance, string filename, string kind, string version)
    {
        string game = instance.path;
        if (version.Length > 0)
        {
            ValidateFilename(version);
            if (!instance.InstalledIds().Contains(version)) throw new ArgumentException("目标游戏版本已不存在。");
            game = VersionSettings.GameDir(instance, version);
        }
        string folder = kind switch
        {
            "mod" => "mods", "resourcepack" => "resourcepacks", "shader" => "shaderpacks", "datapack" => "datapacks",
            _ => throw new ArgumentException("此资源类型不支持文件回收。", nameof(kind))
        };
        return Path.Combine(game, folder, filename);
    }

    private static void ValidateFilename(string filename)
    {
        if (string.IsNullOrWhiteSpace(filename) || filename.Length > 240 || filename is "." or ".."
            || filename.IndexOfAny(new[] { '/', '\\', ':', '\0' }) >= 0 || filename.Any(char.IsControl)
            || filename.EndsWith(".", StringComparison.Ordinal) || filename.EndsWith(" ", StringComparison.Ordinal))
            throw new ArgumentException("资源文件名无效。", nameof(filename));
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        using var hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }
}
