using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HelL.Core.Platform;

namespace HelL.Core.Launcher;

public static class InstanceArtwork
{
    private const string CoverName = ".hell-cover.png";
    private static readonly object CoverLock = new object();

    public static string CustomCover(Instance instance)
    {
        if (!Py.Truthy(instance.Meta().Get("custom_cover"))) return "";
        string path = Path.Combine(instance.path, CoverName);
        return InstanceMedia.IsLocalImage(path, instance.path) ? path : "";
    }

    public static void SetCover(Instance instance, string path)
    {
        string full = Path.GetFullPath(path);
        if (!InstanceMedia.IsLocalImage(full, Path.GetDirectoryName(full)!)) throw new ArgumentException("请选择有效的本地 PNG 或 JPEG 图片（不超过 32 MB）。");
        if (PlatformServices.ImageCodec == null) throw new InvalidOperationException("图片解码器未初始化。");
        byte[] bytes;
        using (var input = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (input.Length > InstanceMedia.MaxImageBytes) throw new IOException("图片过大。");
            bytes = new byte[(int)input.Length];
            int offset = 0;
            while (offset < bytes.Length) { int read = input.Read(bytes, offset, bytes.Length - offset); if (read == 0) throw new IOException("图片文件读取不完整。"); offset += read; }
        }
        using var decoded = PlatformServices.ImageCodec.Decode(bytes) ?? throw new IOException("图片已损坏，无法作为封面。");
        if (decoded.Width <= 0 || decoded.Height <= 0 || (long)decoded.Width * decoded.Height > 64000000) throw new IOException("图片尺寸不受支持。");
        double scale = Math.Min(1, 1280d / Math.Max(decoded.Width, decoded.Height));
        using var preview = decoded.Resize(Math.Max(1, (int)(decoded.Width * scale)), Math.Max(1, (int)(decoded.Height * scale)));
        lock (CoverLock)
        {
            RequireDirectory(instance.path);
            string target = Path.Combine(instance.path, CoverName);
            if (File.Exists(target) && (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0) throw new IOException("封面文件不能是链接。");
            string pending = Path.Combine(instance.path, ".hell-cover-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var file = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { byte[] encoded = preview.EncodePng(); file.Write(encoded, 0, encoded.Length); file.Flush(true); }
                if (File.Exists(target)) File.Replace(pending, target, null); else File.Move(pending, target);
                instance.SetMeta("custom_cover", true);
            }
            finally { if (File.Exists(pending)) File.Delete(pending); }
        }
    }

    public static void ResetCover(Instance instance)
    {
        lock (CoverLock) { RequireDirectory(instance.path); instance.SetMeta("custom_cover", false); }
    }

    public static PyDict Screenshots(IEnumerable<string> roots, int offset, int limit)
    {
        if (offset < 0 || limit < 1 || limit > 60) throw new ArgumentException("截图分页参数无效。");
        var files = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (string root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string folder = Path.Combine(root, "screenshots");
            if (!Directory.Exists(folder)) continue;
            RequireDirectory(folder);
            foreach (var file in new DirectoryInfo(folder).EnumerateFiles())
            {
                if (!file.Exists || file.Extension.ToLowerInvariant() is not (".png" or ".jpg" or ".jpeg") || file.Length > InstanceMedia.MaxImageBytes || (file.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                files[file.FullName] = file;
            }
        }
        var ordered = files.Values.OrderByDescending(file => file.LastWriteTimeUtc).ThenBy(file => file.FullName, StringComparer.Ordinal).ToList();
        offset = ordered.Count == 0 ? 0 : Math.Min(offset, (ordered.Count - 1) / limit * limit);
        var page = ordered.Skip(offset).Take(limit).Select(file => (object)PyDict.Of("name", file.Name, "path", file.FullName, "bytes", file.Length, "mtime", new DateTimeOffset(file.LastWriteTimeUtc).ToUnixTimeSeconds())).ToList();
        return PyDict.Of("items", page, "total", ordered.Count, "offset", offset);
    }

    private static void RequireDirectory(string path)
    {
        string? current = Path.GetFullPath(path);
        if (current.StartsWith(@"\\", StringComparison.Ordinal)) throw new IOException("不支持网络共享封面目录。");
        while (!string.IsNullOrEmpty(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("图片目录不能包含链接或重解析点。");
            current = Path.GetDirectoryName(current);
        }
    }
}
