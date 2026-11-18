using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using HelL.Models;
using HelL.Pages;
using HelL.Services;

namespace HelL.Tests;

public class Phase9PageOptimizationTests
{
    [Theory]
    [InlineData("1.20.1-Fabric", "120", true)]
    [InlineData("1.20.1-Fabric", "fabric", true)]
    [InlineData("生电测试服", "sd", true)]
    [InlineData("生电测试服", "csf", true)]
    [InlineData("纯净生存", "xyz", false)]
    public void VersionFiltering_MatchesPinyinAndSubstrings(string name, string query, bool expected)
    {
        bool matches = InstanceUtils.MatchesPinyinOrSubstr(name, query);
        Assert.Equal(expected, matches);
    }

    [Theory]
    [InlineData("1.20.1", "1.20")]
    [InlineData("1.21.4-fabric", "1.21")]
    [InlineData("1.7.10", "1.7")]
    [InlineData("26.1.2", "26.1")]
    [InlineData("custom-pack", "custom")]
    public void VersionGrouping_ExtractsMajorGroupCorrectly(string versionId, string expectedGroup)
    {
        string group = InstanceUtils.ExtractVersionGroup(versionId);
        Assert.Equal(expectedGroup, group);
    }

    [Fact]
    public void CatalogDependencyCheck_SuggestsMissingCoreMods()
    {
        var installed = new List<string> { "sodium-fabric-mc1.20.1.jar" };
        var required = new List<string> { "fabric-api", "architectury" };

        var suggestions = StreamlineLaunchUtils.RecommendMissingModFixes(installed, required);
        Assert.Equal(2, suggestions.Count);
        Assert.Contains(suggestions, s => s.ModId == "fabric-api");
        Assert.Contains(suggestions, s => s.ModId == "architectury-api");

        string warning = NetworkAndCatalogUtils.FormatDependencyWarning(suggestions.Select(s => s.DisplayName));
        Assert.NotNull(warning);
        Assert.Contains("缺少前置模组", warning);
        Assert.Contains("Fabric API", warning);
    }

    [Fact]
    public void AiFixWindow_BuildCrashContext_MasksSensitivePathAndIp()
    {
        var report = new CrashReport
        {
            Instance = "TestInstance",
            Version = "1.20.1",
            ExitCode = 1,
            Headline = "Crashed at C:\\Users\\Alice\\AppData\\Roaming\\.minecraft\\logs",
            Summary = "Connecting to 192.168.1.50:25565",
            Detail = "Stack trace pointing to D:\\Users\\Bob\\Desktop\\HelL\\run.bat and 10.0.0.12",
            DirectFile = "C:\\Users\\Alice\\.minecraft\\crash-reports\\crash-1.txt"
        };

        string context = CrashPromptFormatter.BuildCrashPrompt(
            report.Instance, report.Version, report.ExitCode, report.ExitHint,
            report.Headline, report.Summary, report.Detail, report.DirectFile, "测试接口");

        Assert.DoesNotContain("Alice", context);
        Assert.DoesNotContain("Bob", context);
        Assert.DoesNotContain("192.168.1.50", context);
        Assert.Contains("***", context);
        Assert.Contains("192.168.*.*", context);
        Assert.Contains("测试接口", context);
    }

    [Fact]
    public void GlobalMods_Filtering_FiltersByFilenameAndPinyin()
    {
        var mods = new List<GlobalModRow>
        {
            new GlobalModRow { Filename = "jei-1.20.1-forge.jar", Size = "1.2 MB", Enabled = true },
            new GlobalModRow { Filename = "高亮显示-wthit.jar", Size = "450 KB", Enabled = true },
            new GlobalModRow { Filename = "cloth-config.jar", Size = "800 KB", Enabled = false }
        };

        // 搜索 jei
        var res1 = mods.Where(m => m.Filename.Contains("jei", StringComparison.OrdinalIgnoreCase) || InstanceUtils.MatchesPinyinOrSubstr(m.Filename, "jei")).ToList();
        Assert.Single(res1);
        Assert.Equal("jei-1.20.1-forge.jar", res1[0].Filename);

        // 拼音首字母搜索 "glxs" -> 高亮显示
        var res2 = mods.Where(m => m.Filename.Contains("glxs", StringComparison.OrdinalIgnoreCase) || InstanceUtils.MatchesPinyinOrSubstr(m.Filename, "glxs")).ToList();
        Assert.Single(res2);
        Assert.Equal("高亮显示-wthit.jar", res2[0].Filename);
    }

    [Fact]
    public void LaunchQuickActions_ResolvesPathsForIsolatedAndGlobal()
    {
        string baseDir = @"D:\Games\.minecraft";
        string versionId = "1.20.4-fabric";

        var isolated = StreamlineLaunchUtils.GetQuickActionDirectories(baseDir, versionId, isIsolated: true);
        Assert.Equal(Path.Combine(baseDir, "versions", versionId, "mods"), isolated.ModsPath);
        Assert.Equal(Path.Combine(baseDir, "versions", versionId, "screenshots"), isolated.ScreenshotsPath);

        var global = StreamlineLaunchUtils.GetQuickActionDirectories(baseDir, versionId, isIsolated: false);
        Assert.Equal(Path.Combine(baseDir, "mods"), global.ModsPath);
        Assert.Equal(Path.Combine(baseDir, "screenshots"), global.ScreenshotsPath);
    }
}
