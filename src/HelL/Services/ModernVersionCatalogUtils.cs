using System;
using System.Collections.Generic;
using System.Linq;

namespace HelL.Services;

public enum VersionSortMode
{
    VersionDescending,
    VersionAscending,
    ModCountDescending,
    NameAscending
}

public sealed class VersionFilterItem
{
    public string Id { get; init; }
    public string Loader { get; init; }
    public string Mc { get; init; }
    public int Mods { get; init; }

    public VersionFilterItem(string id, string loader, string mc, int mods)
    {
        Id = id ?? "";
        Loader = loader ?? "";
        Mc = mc ?? "";
        Mods = mods;
    }
}

public static class ModernVersionCatalogUtils
{
    private static readonly HashSet<string> KnownCanonicalLoaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Fabric", "Forge", "NeoForge", "Quilt", "OptiFine", "LiteLoader"
    };

    public static List<VersionFilterItem> FilterVersions(
        IEnumerable<VersionFilterItem> versions,
        string category,
        string searchQuery)
    {
        if (versions == null) return new List<VersionFilterItem>();

        var query = versions;

        // 1. 类别过滤
        if (!string.IsNullOrWhiteSpace(category) &&
            !string.Equals(category, "All", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(category, "全部", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(category, "Fabric", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(v => string.Equals(v.Loader, "Fabric", StringComparison.OrdinalIgnoreCase));
            }
            else if (string.Equals(category, "Forge", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(v => string.Equals(v.Loader, "Forge", StringComparison.OrdinalIgnoreCase));
            }
            else if (string.Equals(category, "NeoForge", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(v => string.Equals(v.Loader, "NeoForge", StringComparison.OrdinalIgnoreCase));
            }
            else if (string.Equals(category, "Quilt", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(v => string.Equals(v.Loader, "Quilt", StringComparison.OrdinalIgnoreCase));
            }
            else if (string.Equals(category, "原版", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(category, "Vanilla", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(v => string.IsNullOrEmpty(v.Loader) || v.Loader == "原版" || v.Loader == "无" || v.Loader == "Vanilla");
            }
            else if (category.StartsWith("1.20+", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(v =>
                {
                    var (maj, min, _) = ParseSemVer(!string.IsNullOrEmpty(v.Mc) ? v.Mc : v.Id);
                    return (maj == 1 && min >= 20) || maj >= 26;
                });
            }
            else if (category.StartsWith("1.16+", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(v =>
                {
                    var (maj, min, _) = ParseSemVer(!string.IsNullOrEmpty(v.Mc) ? v.Mc : v.Id);
                    return (maj == 1 && min >= 16) || maj >= 26;
                });
            }
            else if (category.Contains("<1.13") || category.Contains("旧版"))
            {
                query = query.Where(v =>
                {
                    var (maj, min, _) = ParseSemVer(!string.IsNullOrEmpty(v.Mc) ? v.Mc : v.Id);
                    return maj == 1 && min < 13;
                });
            }
        }

        // 2. 文本搜索过滤
        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            string q = searchQuery.Trim();
            query = query.Where(v =>
                v.Id.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                v.Mc.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                InstanceUtils.MatchesPinyinOrSubstr(v.Id, q));
        }

        return query.ToList();
    }

    public static List<VersionFilterItem> SortVersions(
        IEnumerable<VersionFilterItem> versions,
        VersionSortMode sortMode)
    {
        if (versions == null) return new List<VersionFilterItem>();

        return sortMode switch
        {
            VersionSortMode.VersionDescending => versions
                .OrderByDescending(v => ParseSemVer(!string.IsNullOrEmpty(v.Mc) ? v.Mc : v.Id))
                .ThenBy(v => v.Id, StringComparer.OrdinalIgnoreCase)
                .ToList(),

            VersionSortMode.VersionAscending => versions
                .OrderBy(v => ParseSemVer(!string.IsNullOrEmpty(v.Mc) ? v.Mc : v.Id))
                .ThenBy(v => v.Id, StringComparer.OrdinalIgnoreCase)
                .ToList(),

            VersionSortMode.ModCountDescending => versions
                .OrderByDescending(v => v.Mods)
                .ThenBy(v => v.Id, StringComparer.OrdinalIgnoreCase)
                .ToList(),

            VersionSortMode.NameAscending => versions
                .OrderBy(v => v.Id, StringComparer.OrdinalIgnoreCase)
                .ToList(),

            _ => versions.ToList()
        };
    }

    public static List<string> NormalizeLoaderTags(IEnumerable<string> rawLoaders)
    {
        if (rawLoaders == null) return new List<string>();

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in rawLoaders)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            string trimmed = raw.Trim();

            // 匹配规范名称
            string canonical = null;
            if (string.Equals(trimmed, "fabric", StringComparison.OrdinalIgnoreCase)) canonical = "Fabric";
            else if (string.Equals(trimmed, "forge", StringComparison.OrdinalIgnoreCase)) canonical = "Forge";
            else if (string.Equals(trimmed, "neoforge", StringComparison.OrdinalIgnoreCase)) canonical = "NeoForge";
            else if (string.Equals(trimmed, "quilt", StringComparison.OrdinalIgnoreCase)) canonical = "Quilt";
            else if (string.Equals(trimmed, "optifine", StringComparison.OrdinalIgnoreCase)) canonical = "OptiFine";
            else if (string.Equals(trimmed, "liteloader", StringComparison.OrdinalIgnoreCase)) canonical = "LiteLoader";

            if (canonical != null && seen.Add(canonical))
            {
                result.Add(canonical);
            }
        }

        return result;
    }

    private static (int Major, int Minor, int Patch) ParseSemVer(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (0, 0, 0);

        // 提取第一个形如 x.y[.z] 的主版本段
        string clean = raw.Trim();
        int slash = clean.IndexOf('/');
        if (slash >= 0) clean = clean.Substring(slash + 1);

        string[] parts = clean.Split(new[] { '.', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);
        int maj = 0, min = 0, patch = 0;

        if (parts.Length > 0) int.TryParse(parts[0], out maj);
        if (parts.Length > 1) int.TryParse(parts[1], out min);
        if (parts.Length > 2) int.TryParse(parts[2], out patch);

        return (maj, min, patch);
    }
}
