using System;
using Xunit;
using HelL.Services;

namespace HelL.Tests;

public class Phase11ModernAestheticTests
{
    [Theory]
    [InlineData(ControlShapeKind.Control, 6.0)]
    [InlineData(ControlShapeKind.Card, 10.0)]
    [InlineData(ControlShapeKind.Modal, 14.0)]
    [InlineData(ControlShapeKind.Pill, 999.0)]
    public void ModernAesthetic_CornerRadiusTokens_MatchesModernGuidelines(ControlShapeKind kind, double expectedRadius)
    {
        double radius = ModernAestheticUtils.GetCornerRadius(kind);
        Assert.Equal(expectedRadius, radius);
    }

    [Theory]
    [InlineData(ModernElevationLevel.Flat, 0.0, 0.0, 0.0, 0.0)]
    [InlineData(ModernElevationLevel.Card, 16.0, 0.08, 2.0, 0.08)]
    [InlineData(ModernElevationLevel.Flyout, 24.0, 0.14, 5.0, 0.12)]
    [InlineData(ModernElevationLevel.Modal, 36.0, 0.22, 10.0, 0.16)]
    public void ModernAesthetic_ElevationGradients_ProvideConsistentDepth(
        ModernElevationLevel level, double expectedBlur, double expectedOpacity, double expectedY, double expectedTopHighlight)
    {
        var elevation = ModernAestheticUtils.GetElevation(level);
        Assert.Equal(expectedBlur, elevation.Blur);
        Assert.Equal(expectedOpacity, elevation.ShadowOpacity);
        Assert.Equal(expectedY, elevation.ShadowY);
        Assert.Equal(expectedTopHighlight, elevation.TopHighlightAlpha);
    }

    [Fact]
    public void ModernAesthetic_SwitchMetrics_ConformsToFluidToggleSpec()
    {
        var switchMetrics = ModernAestheticUtils.GetModernSwitchMetrics();
        Assert.Equal(40.0, switchMetrics.Width);
        Assert.Equal(22.0, switchMetrics.Height);
        Assert.Equal(16.0, switchMetrics.KnobDiameter);
        Assert.Equal(18.0, switchMetrics.TravelDistance);
        Assert.Equal(11.0, switchMetrics.CornerRadius);
    }

    [Theory]
    [InlineData(TagIntent.Accent, "B.AccentText", "B.AccentSoft")]
    [InlineData(TagIntent.Danger, "B.Danger", "B.DangerSoft")]
    [InlineData(TagIntent.Warning, "B.Warn", "B.WarnSoft")]
    [InlineData(TagIntent.Success, "B.Ok", "B.AccentSoft2")]
    [InlineData(TagIntent.Neutral, "B.InkMuted", "B.LineSoft")]
    public void ModernAesthetic_TagColors_MapsSemanticIntentsCorrectly(
        TagIntent intent, string expectedFg, string expectedBg)
    {
        var (fg, bg) = ModernAestheticUtils.ResolveTagColors(intent);
        Assert.Equal(expectedFg, fg);
        Assert.Equal(expectedBg, bg);
    }

    [Fact]
    public void ModernAesthetic_SearchBoxMetrics_ProvidesCompactModernProportions()
    {
        var metrics = ModernAestheticUtils.GetSearchBoxMetrics();
        Assert.Equal(34.0, metrics.Height);
        Assert.Equal(8.0, metrics.CornerRadius);
        Assert.Equal(13.0, metrics.IconSize);
    }

    [Theory]
    [InlineData(true, 0.985)]
    [InlineData(false, 1.0)]
    public void ModernAesthetic_MicroInteractionScale_ProvidesSubtleTactileFeedback(
        bool isPressed, double expectedScale)
    {
        double scale = ModernAestheticUtils.GetMicroInteractionScale(isPressed);
        Assert.Equal(expectedScale, scale);
    }
}
