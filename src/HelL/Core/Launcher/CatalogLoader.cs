using System;

namespace HelL.Core.Launcher;

public static class CatalogLoader
{
    public static string Normalize(string loader) => (loader ?? "").Trim().ToLowerInvariant() switch
    {
        "" or "vanilla" => "", "fabric" => "fabric", "forge" => "forge", "neoforge" => "neoforge", "quilt" => "quilt",
        _ => throw new ArgumentException("不支持的加载器筛选。")
    };
    public static long CurseForge(string loader) => Normalize(loader) switch { "forge" => 1L, "fabric" => 4L, "quilt" => 5L, "neoforge" => 6L, _ => 0L };
}
