using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace HelL.Core.Launcher.Ai.Local;

public sealed class ModelCacheFile
{
    public string Id { get; set; } = "";
    public long Size { get; set; }
    public long ModifiedTicks { get; set; }
    public bool Partial { get; set; }
}

public sealed class ModelStorageInfo
{
    public string Root { get; set; } = "";
    public string ModelsPath { get; set; } = "";
    public long CacheBytes { get; set; }
    public long RuntimeBytes { get; set; }
    public long ModelBytes { get; set; }
    public long InstalledModelBytes { get; set; }
    public long FreeBytes { get; set; }
    public List<ModelCacheFile> Cache { get; set; } = new List<ModelCacheFile>();
}

public sealed class ModelStorage
{
    private static readonly Regex CacheName = new Regex(@"\A(?:[a-f0-9]{64}\.gguf|ollama-[a-f0-9]{64}\.zip)(?:\.partial)?\z", RegexOptions.CultureInvariant);
    private readonly string _root;
    public ModelStorage(string root) => _root = Path.GetFullPath(root);

    public ModelStorageInfo Inspect(string modelsPath)
    {
        var info = new ModelStorageInfo { Root = _root, ModelsPath = modelsPath,
            RuntimeBytes = TreeBytes(Path.Combine(_root, "runtime")), ModelBytes = TreeBytes(modelsPath),
            FreeBytes = new DriveInfo(Path.GetPathRoot(_root)!).AvailableFreeSpace };
        string folder = Path.Combine(_root, "downloads");
        if (!Directory.Exists(folder)) return info;
        RequireRegularPath(folder);
        foreach (string path in Directory.EnumerateFiles(folder))
        {
            var file = new FileInfo(path);
            if (!CacheName.IsMatch(file.Name) || (file.Attributes & FileAttributes.ReparsePoint) != 0) continue;
            info.Cache.Add(new ModelCacheFile { Id = file.Name, Size = file.Length, ModifiedTicks = file.LastWriteTimeUtc.Ticks, Partial = file.Name.EndsWith(".partial", StringComparison.Ordinal) });
            info.CacheBytes = checked(info.CacheBytes + file.Length);
        }
        return info;
    }

    public long DeleteCache(IReadOnlyList<ModelCacheFile> selected)
    {
        if (selected.Count == 0) throw new ArgumentException("请先选择要清理的缓存文件。");
        string folder = Path.Combine(_root, "downloads");
        RequireRegularPath(folder);
        var paths = new List<string>();
        foreach (var item in selected)
        {
            if (!CacheName.IsMatch(item.Id)) throw new ArgumentException("缓存文件标识无效。");
            string path = Path.GetFullPath(Path.Combine(folder, item.Id));
            if (!path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("缓存路径越界。");
            RequireRegularPath(path);
            var file = new FileInfo(path);
            if (!file.Exists || file.Length != item.Size || file.LastWriteTimeUtc.Ticks != item.ModifiedTicks)
                throw new IOException("缓存文件已变化，请刷新占用列表后重新选择。");
            paths.Add(path);
        }
        long freed = 0;
        foreach (string path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            RequireRegularPath(path);
            long bytes = new FileInfo(path).Length;
            File.Delete(path);
            freed += bytes;
        }
        return freed;
    }

    private static long TreeBytes(string root)
    {
        if (!Directory.Exists(root)) return 0;
        RequireRegularPath(root);
        long bytes = 0;
        int visited = 0;
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.Count > 0)
        {
            if (++visited > 10000) throw new IOException("存储目录过大，无法完整统计。");
            foreach (string child in Directory.EnumerateFileSystemEntries(pending.Pop()))
            {
                var attr = File.GetAttributes(child);
                if ((attr & FileAttributes.ReparsePoint) != 0) continue;
                if ((attr & FileAttributes.Directory) != 0) pending.Push(child);
                else bytes = checked(bytes + new FileInfo(child).Length);
            }
        }
        return bytes;
    }

    public static void RequireRegularPath(string path)
    {
        string? current = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("存储路径包含链接或重解析点，已停止文件操作。");
            current = Path.GetDirectoryName(current);
        }
    }
}
