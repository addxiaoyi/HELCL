using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using HelL.Services;

namespace HelL.Tests;

public class Phase9ModernUiAndOptimizationTests
{
    // --- 1. 游戏时长成就体系与分享卡片 (Playtime Milestones & Social Share) ---

    [Theory]
    [InlineData(0.0, "萌新起航", 1)]
    [InlineData(8.5, "萌新起航", 1)]
    [InlineData(25.0, "铜牌冒险家", 2)]
    [InlineData(120.0, "银牌工匠", 3)]
    [InlineData(350.0, "金牌建筑师", 4)]
    [InlineData(750.0, "红石大师", 5)]
    [InlineData(1500.0, "传奇终界使者", 6)]
    public void PlaytimeMilestone_CalculatesAccurateTier(double totalHours, string expectedTitle, int expectedLevel)
    {
        var milestone = ModernUiVisuals.GetPlaytimeMilestone(totalHours);
        Assert.Equal(expectedTitle, milestone.Title);
        Assert.Equal(expectedLevel, milestone.Level);
        Assert.False(string.IsNullOrEmpty(milestone.Glyph));
        Assert.False(string.IsNullOrEmpty(milestone.Description));
    }

    [Fact]
    public void FormatShareablePlaytimeSummary_BuildsCleanMarkdownReport()
    {
        string summary = ModernUiVisuals.FormatShareablePlaytimeSummary(
            totalHours: 145.8,
            instanceCount: 3,
            topInstanceName: "生电服整合",
            topInstanceHours: 98.2
        );

        Assert.Contains("🎮 HelL 游戏时光成就", summary);
        Assert.Contains("累计游玩: 145.8 小时", summary);
        Assert.Contains("成就头衔: 银牌工匠 (Lv.3)", summary);
        Assert.Contains("最常玩实例: 生电服整合 (98.2 小时)", summary);
        Assert.Contains("由 HelL 驱动", summary);
    }

    // --- 2. 实例健康度智能诊断 (Instance Health Diagnostics) ---

    [Fact]
    public void EvaluateInstanceHealth_DetectsOptimalSetupAsGradeS()
    {
        var report = ModernUiVisuals.EvaluateInstanceHealth(
            hasVersion: true,
            mcVersion: "1.20.1",
            javaMajor: 17,
            isJava64Bit: true,
            allocatedMb: 6144,
            systemAvailableMb: 12000,
            hasDependencyErrors: false
        );

        Assert.Equal(100, report.Score);
        Assert.Equal("S", report.Grade);
        Assert.Equal("优秀", report.StatusText);
        Assert.Empty(report.Issues);
    }

    [Fact]
    public void EvaluateInstanceHealth_PenalizesIncompatibleJavaAnd32Bit()
    {
        // MC 1.20.5+ 需 Java 21+，如果分配 32 位且 Java 版本过低，应扣分并提示严重告警
        var report = ModernUiVisuals.EvaluateInstanceHealth(
            hasVersion: true,
            mcVersion: "1.20.5",
            javaMajor: 8,
            isJava64Bit: false,
            allocatedMb: 4096,
            systemAvailableMb: 8000,
            hasDependencyErrors: false
        );

        Assert.True(report.Score <= 50);
        Assert.True(report.Grade == "C" || report.Grade == "D");
        Assert.Contains(report.Issues, i => i.Contains("32 位"));
        Assert.Contains(report.Issues, i => i.Contains("Java 21"));
    }

    [Fact]
    public void EvaluateInstanceHealth_WarnsOnLowMemoryAllocation()
    {
        var report = ModernUiVisuals.EvaluateInstanceHealth(
            hasVersion: true,
            mcVersion: "1.20.1",
            javaMajor: 17,
            isJava64Bit: true,
            allocatedMb: 512, // 512MB 过低
            systemAvailableMb: 8000,
            hasDependencyErrors: false
        );

        Assert.True(report.Score < 90);
        Assert.Contains(report.Issues, i => i.Contains("内存分配过低"));
    }

    // --- 3. 已安装版本现代筛选与多维排序 (Version Filter & Smart Sort) ---

    [Fact]
    public void VersionCatalog_FilterByLoaderCategory()
    {
        var sampleVersions = new List<VersionFilterItem>
        {
            new VersionFilterItem("1.20.1-fabric", "Fabric", "1.20.1", 35),
            new VersionFilterItem("1.20.1-forge", "Forge", "1.20.1", 50),
            new VersionFilterItem("1.20.4-neoforge", "NeoForge", "1.20.4", 12),
            new VersionFilterItem("1.20.4-vanilla", "原版", "1.20.4", 0),
            new VersionFilterItem("1.16.5-forge", "Forge", "1.16.5", 80),
            new VersionFilterItem("1.7.10-forge", "Forge", "1.7.10", 120),
        };

        var fabricOnly = ModernVersionCatalogUtils.FilterVersions(sampleVersions, category: "Fabric", searchQuery: "");
        Assert.Single(fabricOnly);
        Assert.Equal("1.20.1-fabric", fabricOnly[0].Id);

        var forgeOnly = ModernVersionCatalogUtils.FilterVersions(sampleVersions, category: "Forge", searchQuery: "");
        Assert.Equal(3, forgeOnly.Count);

        var mc120Plus = ModernVersionCatalogUtils.FilterVersions(sampleVersions, category: "1.20+", searchQuery: "");
        Assert.Equal(4, mc120Plus.Count);
    }

    [Fact]
    public void VersionCatalog_SortByVersionsAndModCount()
    {
        var sampleVersions = new List<VersionFilterItem>
        {
            new VersionFilterItem("1.16.5-forge", "Forge", "1.16.5", 80),
            new VersionFilterItem("1.20.4-neoforge", "NeoForge", "1.20.4", 12),
            new VersionFilterItem("1.7.10-forge", "Forge", "1.7.10", 120),
            new VersionFilterItem("1.20.1-fabric", "Fabric", "1.20.1", 35),
        };

        var sortedByVersion = ModernVersionCatalogUtils.SortVersions(sampleVersions, VersionSortMode.VersionDescending);
        Assert.Equal("1.20.4-neoforge", sortedByVersion[0].Id);
        Assert.Equal("1.20.1-fabric", sortedByVersion[1].Id);
        Assert.Equal("1.16.5-forge", sortedByVersion[2].Id);
        Assert.Equal("1.7.10-forge", sortedByVersion[3].Id);

        var sortedByMods = ModernVersionCatalogUtils.SortVersions(sampleVersions, VersionSortMode.ModCountDescending);
        Assert.Equal("1.7.10-forge", sortedByMods[0].Id); // 120 mods
        Assert.Equal("1.16.5-forge", sortedByMods[1].Id); // 80 mods
    }

    // --- 4. 紧凑数值展示与加载器矩阵标准化 (Compact Metrics & Loader Badges) ---

    [Theory]
    [InlineData(0, "0")]
    [InlineData(850, "850")]
    [InlineData(1200, "1.2K")]
    [InlineData(45600, "45.6K")]
    [InlineData(1000000, "1.0M")]
    [InlineData(3480000, "3.48M")]
    public void FormatCompactMetric_OutputsCleanKAndMStrings(long count, string expectedFormatted)
    {
        string formatted = ModernUiVisuals.FormatCompactMetric(count);
        Assert.Equal(expectedFormatted, formatted);
    }

    [Fact]
    public void ExtractSupportedLoaders_NormalizesAndDeduplicatesLoaders()
    {
        var rawList = new List<string> { "fabric", "FABRIC", "neoforge", "quilt", "unknown_loader" };
        var normalized = ModernVersionCatalogUtils.NormalizeLoaderTags(rawList);

        Assert.Equal(3, normalized.Count);
        Assert.Contains("Fabric", normalized);
        Assert.Contains("NeoForge", normalized);
        Assert.Contains("Quilt", normalized);
        Assert.DoesNotContain("unknown_loader", normalized);
    }
}
