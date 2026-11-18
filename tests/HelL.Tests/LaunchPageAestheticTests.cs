using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using HelL.Services;

namespace HelL.Tests;

public class LaunchPageAestheticTests
{
    [Theory]
    [InlineData("banner", true)]
    [InlineData("config", false)]
    [InlineData("log", false)]
    [InlineData("news", false)]
    [InlineData("quick", false)]
    [InlineData("playtime", false)]
    public void SeamlessBanner_CardTypeBanner_IsIdentifiedAsSeamless(string cardType, bool expectedSeamless)
    {
        bool actual = VisualPolishUtils.IsSeamlessCard(cardType);
        Assert.Equal(expectedSeamless, actual);
    }

    [Fact]
    public void CardSurface_BannerHasZeroOuterPadding_ForFullBleedHeroExperience()
    {
        var bannerMetrics = VisualPolishUtils.GetCardShellMetrics("banner");
        var standardMetrics = VisualPolishUtils.GetCardShellMetrics("config");

        // 横幅卡片外部内衬必须为 0，实现真正沉浸式的 Hero Banner
        Assert.Equal(0.0, bannerMetrics.Padding);
        Assert.False(bannerMetrics.ShowHeaderInNormalMode, "Banner should not show a redundant '启动横幅' header in normal view");

        // 普通卡片保留标准舒适内衬和标题栏
        Assert.True(standardMetrics.Padding >= 12.0);
        Assert.True(standardMetrics.ShowHeaderInNormalMode);
    }

    [Theory]
    [InlineData(false, 3)] // 默认精简折叠模式：仅实例、版本、账号 3 项核心决策
    [InlineData(true, 7)]  // 展开模式：包含 Java、内存、分辨率、直连服务器等全量 7 项
    public void ConfigSections_TogglesAdvancedDrawerFields(bool expanded, int expectedFieldCount)
    {
        var fields = StreamlineLaunchUtils.GetActiveConfigFields(expanded);
        Assert.Equal(expectedFieldCount, fields.Count);
        if (!expanded)
        {
            Assert.Contains("instance", fields);
            Assert.Contains("version", fields);
            Assert.Contains("account", fields);
            Assert.DoesNotContain("java", fields);
            Assert.DoesNotContain("jvm_memory", fields);
        }
        else
        {
            Assert.Contains("java", fields);
            Assert.Contains("jvm_memory", fields);
            Assert.Contains("resolution", fields);
            Assert.Contains("server", fields);
        }
    }

    [Theory]
    [InlineData(true, "", true, "正在准备启动...")]
    [InlineData(true, "downloading assets", true, "正在准备资源文件...")]
    [InlineData(false, null, true, "启动完成，游戏愉快！")]
    [InlineData(false, "Java not found", false, "启动失败：Java not found")]
    public void BannerStatus_FormatsFriendlyMessages(bool isBusy, string rawMessage, bool isSuccess, string expectedFriendly)
    {
        string formatted = LaunchBannerVisualUtils.FormatBannerStatus(isBusy, rawMessage, isSuccess);
        Assert.Equal(expectedFriendly, formatted);
    }
}
