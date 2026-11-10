#nullable enable
using System;
using System.IO;

namespace HelL.Portable;

/// <summary>Keeps portable state outside the executable and signed macOS bundle.</summary>
public sealed class PortableHome : IDisposable
{
    private readonly FileStream lease;
    public string Root { get; }

    private PortableHome(string root, FileStream lease)
    {
        Root = root;
        this.lease = lease;
    }

    public static string Resolve(string executableDirectory, string requested, bool macOS)
    {
        string folder = Path.GetFullPath(executableDirectory);
        DirectoryInfo? ancestor = new DirectoryInfo(folder);
        while (ancestor != null && !ancestor.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            ancestor = ancestor.Parent;

        if (!string.IsNullOrWhiteSpace(requested))
        {
            if (!Path.IsPathFullyQualified(requested))
                throw new ArgumentException("TIPCL_HOME 必须是绝对路径。");
            string root = Path.GetFullPath(requested);
            if (ancestor != null && IsInside(root, ancestor.FullName))
                throw new ArgumentException("数据目录不能位于 .app 内部，请选择应用旁的 data 文件夹。");
            return root;
        }

        if (macOS && folder.Replace('\\', '/').Contains("/AppTranslocation/", StringComparison.Ordinal))
            throw new IOException("请先退出，将解压后的 HelL 文件夹移到可写位置，再打开应用；或用 TIPCL_HOME 指定数据目录。");

        return Path.Combine(macOS && ancestor != null ? ancestor.Parent!.FullName : folder, "data");
    }

    public static PortableHome Open(string root)
    {
        root = Path.GetFullPath(root);
        try
        {
            Directory.CreateDirectory(root);
            // Keep the file after closing: unlinking a lock can let two Unix processes own different inodes.
            var lease = new FileStream(Path.Combine(root, ".launcher.lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
            return new PortableHome(root, lease);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new IOException("数据目录不可写，请将便携包解压到个人文件夹，或设置 TIPCL_HOME。", ex);
        }
        catch (IOException ex)
        {
            throw new IOException("无法打开数据目录：可能已被另一个启动器使用，或磁盘不可写。请关闭另一窗口后重试。", ex);
        }
    }

    private static bool IsInside(string path, string parent) =>
        path.Equals(parent, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public void Dispose() => lease.Dispose();
}
