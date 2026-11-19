using System;
using System.Text.RegularExpressions;

namespace HelL.Services;

/// <summary>
/// 启动页 Hero Banner 视觉标牌与版本元数据解析器
/// </summary>
public static class LaunchBannerVisualUtils
{
    public sealed class LoaderMeta
    {
        public string LoaderType { get; set; } = "Vanilla";
        public string BadgeText { get; set; } = "原版";
        public string AccentHex { get; set; } = "#2E9B6B";
        public string Glyph { get; set; } = "\ue768";
        public string Description { get; set; } = "官方原版运行时";
    }

    public sealed class LaunchButtonDimensions
    {
        public double Height { get; set; } = 46.0;
        public double CornerRadius { get; set; } = 10.0;
        public double PaddingHorizontal { get; set; } = 32.0;
        public double PaddingVertical { get; set; } = 12.0;
        public double FontSize { get; set; } = 15.5;
    }

    public static LoaderMeta ResolveLoaderMeta(string rawVersion)
    {
        if (string.IsNullOrWhiteSpace(rawVersion))
        {
            return new LoaderMeta
            {
                LoaderType = "Vanilla",
                BadgeText = "未选择",
                AccentHex = "#888888",
                Glyph = "\ue7ba",
                Description = "尚未指定游戏版本"
            };
        }

        string lower = rawVersion.ToLowerInvariant();

        // 1. NeoForge (需在 Forge 之前匹配)
        if (lower.Contains("neoforge"))
        {
            return new LoaderMeta
            {
                LoaderType = "NeoForge",
                BadgeText = "NEOFORGE",
                AccentHex = "#FF7324",
                Glyph = "\ue71d",
                Description = "NeoForged 新一代模组引擎"
            };
        }

        // 2. Fabric
        if (lower.Contains("fabric"))
        {
            return new LoaderMeta
            {
                LoaderType = "Fabric",
                BadgeText = "FABRIC",
                AccentHex = "#3884FF",
                Glyph = "\ue702",
                Description = "轻量级高性能模组加载器"
            };
        }

        // 3. Quilt
        if (lower.Contains("quilt"))
        {
            return new LoaderMeta
            {
                LoaderType = "Quilt",
                BadgeText = "QUILT",
                AccentHex = "#9C59D1",
                Glyph = "\ue735",
                Description = "现代模块化生态加载器"
            };
        }

        // 4. Forge
        if (lower.Contains("forge"))
        {
            return new LoaderMeta
            {
                LoaderType = "Forge",
                BadgeText = "FORGE",
                AccentHex = "#DF8B27",
                Glyph = "\ue74e",
                Description = "经典全功能模组架构"
            };
        }

        // 5. OptiFine
        if (lower.Contains("optifine"))
        {
            return new LoaderMeta
            {
                LoaderType = "OptiFine",
                BadgeText = "OPTIFINE",
                AccentHex = "#D93B3B",
                Glyph = "\ue706",
                Description = "高清高清画质与光影加速"
            };
        }

        // 6. Snapshot (如 24w14a, 1.21-pre1, 1.20-rc2)
        if (Regex.IsMatch(lower, @"\d{2}w\d{2}[a-z]|pre|rc|snapshot"))
        {
            return new LoaderMeta
            {
                LoaderType = "Snapshot",
                BadgeText = "快照",
                AccentHex = "#7D8C99",
                Glyph = "\ue81c",
                Description = "前沿测试与抢先体验版"
            };
        }

        // 7. 纯正原版 Vanilla
        return new LoaderMeta
        {
            LoaderType = "Vanilla",
            BadgeText = "原版",
            AccentHex = "#2E9B6B",
            Glyph = "\ue7c3",
            Description = "纯净原汁原味官方环境"
        };
    }

    public static (string DisplayTitle, string Subtitle) FormatHeadline(string instance, string rawVersion)
    {
        if (string.IsNullOrWhiteSpace(rawVersion))
        {
            return ("未选择游戏版本", "请先在版本管理或配置中选择/下载版本");
        }

        // 提取主要 MC 版本，如 1.20.1-Fabric-0.15.7 -> 1.20.1
        var match = Regex.Match(rawVersion, @"(1\.\d+(\.\d+)?|\d{2}w\d{2}[a-z]|\d+\.\d+)");
        string mcCore = match.Success ? match.Value : rawVersion;
        string displayTitle = $"Minecraft {mcCore}";

        var meta = ResolveLoaderMeta(rawVersion);
        string subtitle;
        if (meta.LoaderType == "Vanilla")
        {
            subtitle = $"原版官方环境 · 实例「{instance}」";
        }
        else
        {
            subtitle = $"{meta.LoaderType} 架构 · 实例「{instance}」";
        }

        return (displayTitle, subtitle);
    }

    public static LaunchButtonDimensions GetLaunchButtonDimensions()
    {
        return new LaunchButtonDimensions();
    }

    public static string FormatBannerStatus(bool isBusy, string rawMessage, bool isSuccess)
    {
        if (isBusy)
        {
            if (string.IsNullOrWhiteSpace(rawMessage))
            {
                return "正在准备启动...";
            }
            string lower = rawMessage.ToLowerInvariant();
            if (lower.Contains("asset") || lower.Contains("resource"))
            {
                return "正在准备资源文件...";
            }
            if (lower.Contains("librar") || lower.Contains("jar"))
            {
                return "正在解析并下载依赖库...";
            }
            if (lower.Contains("java") || lower.Contains("jvm"))
            {
                return "正在配置 Java 运行环境...";
            }
            return rawMessage;
        }

        if (isSuccess)
        {
            return "启动完成，游戏愉快！";
        }

        if (!string.IsNullOrWhiteSpace(rawMessage))
        {
            return $"启动失败：{rawMessage}";
        }

        return "启动失败，请检查崩溃日志";
    }
}
