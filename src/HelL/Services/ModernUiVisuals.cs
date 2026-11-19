using System;
using System.Collections.Generic;
using System.Globalization;

namespace HelL.Services;

public readonly struct PlaytimeMilestone
{
    public string Title { get; }
    public int Level { get; }
    public string Glyph { get; }
    public string Description { get; }
    public string BadgeColor { get; }

    public PlaytimeMilestone(string title, int level, string glyph, string description, string badgeColor)
    {
        Title = title;
        Level = level;
        Glyph = glyph;
        Description = description;
        BadgeColor = badgeColor;
    }
}

public readonly struct InstanceHealthReport
{
    public int Score { get; }
    public string Grade { get; }
    public string StatusText { get; }
    public IReadOnlyList<string> Issues { get; }

    public InstanceHealthReport(int score, string grade, string statusText, IReadOnlyList<string> issues)
    {
        Score = score;
        Grade = grade;
        StatusText = statusText;
        Issues = issues;
    }
}

public static class ModernUiVisuals
{
    public static PlaytimeMilestone GetPlaytimeMilestone(double totalHours)
    {
        if (totalHours < 10.0)
        {
            return new PlaytimeMilestone("萌新起航", 1, "\ue734", "刚踏入方块世界的新人冒险家", "#8E8E93");
        }
        if (totalHours < 50.0)
        {
            return new PlaytimeMilestone("铜牌冒险家", 2, "\ue735", "已掌握生存基础，走过千山万水", "#CD7F32");
        }
        if (totalHours < 200.0)
        {
            return new PlaytimeMilestone("银牌工匠", 3, "\ue786", "精通自动化与各式建筑建造", "#A0A0A0");
        }
        if (totalHours < 500.0)
        {
            return new PlaytimeMilestone("金牌建筑师", 4, "\ue734", "拥有雄伟的帝国与多重下界交通网", "#FFD700");
        }
        if (totalHours < 1000.0)
        {
            return new PlaytimeMilestone("红石大师", 5, "\ue964", "红石巨械与刷怪塔尽在掌控之中", "#FF453A");
        }
        return new PlaytimeMilestone("传奇终界使者", 6, "\ue724", "主宰末地与寰宇的资深传奇老兵", "#AF52DE");
    }

    public static string FormatShareablePlaytimeSummary(
        double totalHours,
        int instanceCount,
        string topInstanceName,
        double topInstanceHours)
    {
        var milestone = GetPlaytimeMilestone(totalHours);
        return $"""
        🎮 HelL 游戏时光成就
        ----------------------------------------
        累计游玩: {totalHours:F1} 小时
        成就头衔: {milestone.Title} (Lv.{milestone.Level})
        管理实例: {instanceCount} 个
        最常玩实例: {topInstanceName} ({topInstanceHours:F1} 小时)
        ----------------------------------------
        ✨ 由 HelL 驱动 · 激进现代启动器
        """;
    }

    public static InstanceHealthReport EvaluateInstanceHealth(
        bool hasVersion,
        string mcVersion,
        int javaMajor,
        bool isJava64Bit,
        int allocatedMb,
        int systemAvailableMb,
        bool hasDependencyErrors)
    {
        int score = 100;
        var issues = new List<string>();

        if (!hasVersion)
        {
            score -= 50;
            issues.Add("未选择或未安装游戏版本");
        }

        // Java 与 Minecraft 版本兼容规则评估
        if (!string.IsNullOrEmpty(mcVersion))
        {
            var (mcMajor, mcMinor) = ParseMajorMinor(mcVersion);
            if (mcMajor == 1 && mcMinor >= 21 || (mcMajor == 1 && mcMinor == 20 && mcVersion.Contains("20.5") || mcVersion.Contains("20.6")) || mcMajor >= 26)
            {
                if (javaMajor > 0 && javaMajor < 21)
                {
                    score -= 40;
                    issues.Add($"Minecraft {mcVersion} 需要 Java 21+ (当前为 Java {javaMajor})");
                }
            }
            else if (mcMajor == 1 && mcMinor >= 18)
            {
                if (javaMajor > 0 && javaMajor < 17)
                {
                    score -= 30;
                    issues.Add($"Minecraft {mcVersion} 需要 Java 17+ (当前为 Java {javaMajor})");
                }
            }
        }

        if (!isJava64Bit)
        {
            score -= 30;
            issues.Add("当前 Java 运行时为 32 位，内存与运行效率受限");
        }

        if (allocatedMb < 1024)
        {
            score -= 15;
            issues.Add($"内存分配过低 ({allocatedMb}MB)，游戏启动可能卡顿甚至 OOM 崩溃");
        }
        else if (systemAvailableMb > 0 && allocatedMb > systemAvailableMb)
        {
            score -= 20;
            issues.Add($"分配内存 ({allocatedMb}MB) 超过系统可用物理内存 ({systemAvailableMb}MB)");
        }

        if (hasDependencyErrors)
        {
            score -= 25;
            issues.Add("检测到部分模组缺失前置依赖，可能导致游戏无法启动");
        }

        score = Math.Max(0, Math.Min(100, score));

        string grade;
        string statusText;
        if (score >= 95)
        {
            grade = "S";
            statusText = "优秀";
        }
        else if (score >= 80)
        {
            grade = "A";
            statusText = "良好";
        }
        else if (score >= 60)
        {
            grade = "B";
            statusText = "一般";
        }
        else if (score >= 40)
        {
            grade = "C";
            statusText = "需关注";
        }
        else
        {
            grade = "D";
            statusText = "急需修复";
        }

        return new InstanceHealthReport(score, grade, statusText, issues);
    }

    public static string FormatCompactMetric(long count)
    {
        if (count < 0) return "0";
        if (count >= 1_000_000)
        {
            double m = (double)count / 1_000_000.0;
            string s = m.ToString(m >= 10.0 ? "0.#" : (m % 1.0 == 0.0 ? "0.0" : "0.##"), CultureInfo.InvariantCulture);
            return s + "M";
        }
        if (count >= 1_000)
        {
            double k = (double)count / 1_000.0;
            return k.ToString(k >= 10.0 ? "0.#" : "0.#", CultureInfo.InvariantCulture) + "K";
        }
        return count.ToString(CultureInfo.InvariantCulture);
    }

    private static (int Major, int Minor) ParseMajorMinor(string version)
    {
        if (string.IsNullOrWhiteSpace(version)) return (0, 0);
        string[] parts = version.Trim().Split(new[] { '.', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);
        int major = 0;
        int minor = 0;
        if (parts.Length > 0) int.TryParse(parts[0], out major);
        if (parts.Length > 1) int.TryParse(parts[1], out minor);
        return (major, minor);
    }
}
