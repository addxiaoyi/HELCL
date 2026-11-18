using System;
using System.Collections.Generic;
using Xunit;
using HelL.Services;
using HelL.Core.Launcher.Sync;

namespace HelL.Tests;

public class Phase7UiOptimizationAndEnhancementTests
{
    [Theory]
    [InlineData(2, 1, 0, 3, "发现 6 个模组：2 个可更新，1 个已是最新，3 个全新添加")]
    [InlineData(0, 0, 0, 5, "发现 5 个全新模组")]
    [InlineData(0, 3, 0, 0, "所选 3 个模组均已安装且为相同版本")]
    [InlineData(1, 0, 2, 0, "发现 3 个模组：1 个可更新，2 个较旧版本")]
    public void FormatImportSummary_ProducesUserFriendlyText(int newer, int same, int older, int fresh, string expectedText)
    {
        string summary = MultiplayerAndDialogUtils.FormatImportSummary(newer, same, older, fresh);
        Assert.Equal(expectedText, summary);
    }

    [Theory]
    [InlineData(SyncDecision.Conflict, "两边都改过", true)]
    [InlineData(SyncDecision.Push, "↑ 电脑较新", false)]
    [InlineData(SyncDecision.Pull, "↓ 手机较新", false)]
    [InlineData(SyncDecision.Same, "已一致", false)]
    [InlineData(SyncDecision.OnlyLocal, "仅电脑有", false)]
    [InlineData(SyncDecision.OnlyRemote, "仅手机有", false)]
    public void GetSyncDecisionBadge_ReturnsDirectionalTextAndAlertStatus(SyncDecision decision, string expectedText, bool expectedAlert)
    {
        var (text, isAlert) = MultiplayerAndDialogUtils.GetSyncDecisionBadge(decision);
        Assert.Equal(expectedText, text);
        Assert.Equal(expectedAlert, isAlert);
    }

    [Fact]
    public void FormatDependencyWarning_BuildsWarningWhenMissing()
    {
        var missing = new List<string> { "Fabric API", "Cloth Config" };
        string warning = NetworkAndCatalogUtils.FormatDependencyWarning(missing);
        Assert.Contains("缺少前置模组", warning);
        Assert.Contains("Fabric API", warning);
        Assert.Contains("Cloth Config", warning);

        string emptyWarning = NetworkAndCatalogUtils.FormatDependencyWarning(new List<string>());
        Assert.Null(emptyWarning);
    }

    [Fact]
    public void FilterAvailableLaunchers_FiltersExistingPathsOnly()
    {
        var candidates = new List<LauncherCandidate>
        {
            new LauncherCandidate("PCL2", "D:\\fake\\pcl", "PCL2"),
            new LauncherCandidate("HMCL", "D:\\fake\\hmcl", "HMCL")
        };

        var filtered = MultiplayerAndDialogUtils.FilterAvailableLaunchers(candidates, path => path.Contains("pcl"));
        Assert.Single(filtered);
        Assert.Equal("PCL2", filtered[0].Name);
    }

    [Theory]
    [InlineData(true, "64 位 (推荐)", false)]
    [InlineData(false, "32 位 (内存受限)", true)]
    public void FormatJavaArchitectureBadge_Highlights32BitWarning(bool is64Bit, string expectedBadge, bool expectedWarning)
    {
        var (badge, isWarning) = AccountAndJavaUtils.FormatJavaArchitectureBadge(is64Bit);
        Assert.Equal(expectedBadge, badge);
        Assert.Equal(expectedWarning, isWarning);
    }

    [Theory]
    [InlineData(false, "正面视角 (点击看背面)")]
    [InlineData(true, "背面视角 (点击看正面)")]
    public void GetSkinAngleLabel_TogglesCorrectly(bool isBack, string expectedLabel)
    {
        string label = AccountAndJavaUtils.GetSkinAngleLabel(isBack);
        Assert.Equal(expectedLabel, label);
    }
}
