using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HelL.Services;

public sealed class QuickActionPaths
{
    public string ModsPath { get; set; } = "";
    public string SavesPath { get; set; } = "";
    public string LogsPath { get; set; } = "";
    public string ScreenshotsPath { get; set; } = "";
    public string CrashReportsPath { get; set; } = "";
}

public sealed class MissingModFixSuggestion
{
    public string ModId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
}

public static class StreamlineLaunchUtils
{
    private static readonly HashSet<string> SimpleSettingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "game_dir",
        "jvm_memory",
        "java_path",
        "resolution",
        "download_source",
        "window_mode",
        "ui_scale",
        "language"
    };

    private static readonly HashSet<string> PrimaryHomeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "instance", "version", "account"
    };

    public static (IReadOnlyList<string> Primary, IReadOnlyList<string> Advanced) ClassifyHomeConfigFields(IEnumerable<string> fieldKeys)
    {
        var primary = new List<string>();
        var advanced = new List<string>();

        if (fieldKeys != null)
        {
            foreach (var key in fieldKeys)
            {
                if (PrimaryHomeKeys.Contains(key))
                {
                    primary.Add(key);
                }
                else
                {
                    advanced.Add(key);
                }
            }
        }

        return (primary, advanced);
    }

    public static IReadOnlyList<string> GetActiveConfigFields(bool expanded)
    {
        var list = new List<string> { "instance", "version", "account" };
        if (expanded)
        {
            list.AddRange(new[] { "java", "jvm_memory", "resolution", "server" });
        }
        return list;
    }

    public static QuickActionPaths GetQuickActionDirectories(string gameDir, string versionId, bool isIsolated)
    {
        string root = isIsolated && !string.IsNullOrWhiteSpace(versionId)
            ? Path.Combine(gameDir, "versions", versionId)
            : gameDir;

        return new QuickActionPaths
        {
            ModsPath = Path.Combine(root, "mods"),
            SavesPath = Path.Combine(root, "saves"),
            LogsPath = Path.Combine(root, "logs"),
            ScreenshotsPath = Path.Combine(root, "screenshots"),
            CrashReportsPath = Path.Combine(root, "crash-reports")
        };
    }

    public static IEnumerable<string> FilterSettingsForMode(IEnumerable<string> allSettingKeys, bool isSimpleMode)
    {
        if (allSettingKeys == null) return Enumerable.Empty<string>();
        if (!isSimpleMode) return allSettingKeys;

        return allSettingKeys.Where(k => SimpleSettingKeys.Contains(k));
    }

    public static List<MissingModFixSuggestion> RecommendMissingModFixes(
        IEnumerable<string> installedModFileNames, IEnumerable<string> requiredDependencies)
    {
        var list = new List<MissingModFixSuggestion>();
        if (requiredDependencies == null) return list;

        var installedNames = (installedModFileNames ?? Enumerable.Empty<string>())
            .Select(f => Path.GetFileNameWithoutExtension(f).ToLowerInvariant())
            .ToList();

        foreach (var req in requiredDependencies)
        {
            if (string.IsNullOrWhiteSpace(req)) continue;
            string key = req.Trim().ToLowerInvariant();

            // 检查已安装列表中是否已经包含该关键字
            bool found = installedNames.Any(n => n.Contains(key));
            if (!found)
            {
                list.Add(CreateSuggestionForDep(req));
            }
        }

        return list;
    }

    private static MissingModFixSuggestion CreateSuggestionForDep(string depId)
    {
        string norm = depId.Trim().ToLowerInvariant();
        return norm switch
        {
            "fabric-api" or "fabric" => new MissingModFixSuggestion
            {
                ModId = "fabric-api",
                DisplayName = "Fabric API (通用核心运行库)",
                DownloadUrl = "https://modrinth.com/mod/fabric-api"
            },
            "cloth-config" => new MissingModFixSuggestion
            {
                ModId = "cloth-config",
                DisplayName = "Cloth Config v11+ (配置界面前置)",
                DownloadUrl = "https://modrinth.com/mod/cloth-config"
            },
            "architectury" or "architectury-api" => new MissingModFixSuggestion
            {
                ModId = "architectury-api",
                DisplayName = "Architectury API (跨平台架构前置)",
                DownloadUrl = "https://modrinth.com/mod/architectury-api"
            },
            "kotlin-for-forge" => new MissingModFixSuggestion
            {
                ModId = "kotlin-for-forge",
                DisplayName = "Kotlin for Forge",
                DownloadUrl = "https://modrinth.com/mod/kotlin-for-forge"
            },
            _ => new MissingModFixSuggestion
            {
                ModId = depId,
                DisplayName = depId,
                DownloadUrl = $"https://modrinth.com/mods?q={Uri.EscapeDataString(depId)}"
            }
        };
    }
}
