using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;
using HelL.Services;

namespace HelL.Tests;

/// <summary>
/// 现代化高级感与彻底消除原生组件/控件自动化验收测试套件
/// </summary>
public class ModernControlStylesAndDeNativeTests
{
    private static string GetControlsXamlContent()
    {
        string path = Path.Combine(TestPaths.Root(), "src", "HelL", "Themes", "Controls.xaml");
        Assert.True(File.Exists(path), $"Controls.xaml 不存在于: {path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void ControlsXaml_MustContainImplicitStylesForEssentialControls()
    {
        string xaml = GetControlsXamlContent();

        // 核心原生控件必须具备隐式全局样式（TargetType 且不带 x:Key，或者其 TargetType 拥有无原生回退的 ControlTemplate）
        string[] requiredControls = new[]
        {
            "Button",
            "TextBox",
            "PasswordBox",
            "ComboBox",
            "ComboBoxItem",
            "CheckBox",
            "RadioButton",
            "Slider",
            "ProgressBar",
            "TabControl",
            "TabItem",
            "Expander",
            "GroupBox",
            "ListBox",
            "ListBoxItem",
            "ListView",
            "ListViewItem",
            "ScrollViewer",
            "ScrollBar",
            "Separator"
        };

        foreach (var controlName in requiredControls)
        {
            // 匹配格式: <Style TargetType="{x:Type controlName}" ...> 或 <Style x:Key="{x:Type controlName}" TargetType="{x:Type controlName}" ...>
            var pattern = $@"<Style\s+(x:Key=""\{{x:Type\s+{controlName}\}}""\s+)?TargetType=""\{{x:Type\s+{controlName}\}}""";
            var match = Regex.IsMatch(xaml, pattern);
            Assert.True(match, $"Controls.xaml 缺少针对原生控件 [{controlName}] 的全局现代化隐式样式定义！");
        }
    }

    [Fact]
    public void ControlsXaml_CheckBoxAndRadioButton_MustNotFallbackToNativeControls()
    {
        string xaml = GetControlsXamlContent();

        // 严禁 BasedOn="{StaticResource {x:Type CheckBox}}" 导致回退原生 Windows 灰框
        Assert.DoesNotMatch(@"<Style[^>]*TargetType=""\{x:Type\s+CheckBox\}""[^>]*BasedOn=""\{StaticResource\s+\{x:Type\s+CheckBox\}\}""", xaml);
        Assert.DoesNotMatch(@"<Style[^>]*TargetType=""\{x:Type\s+RadioButton\}""[^>]*BasedOn=""\{StaticResource\s+\{x:Type\s+RadioButton\}\}""", xaml);
        Assert.DoesNotMatch(@"<Style[^>]*TargetType=""\{x:Type\s+TabItem\}""[^>]*BasedOn=""\{StaticResource\s+\{x:Type\s+TabItem\}\}""", xaml);
        Assert.DoesNotMatch(@"<Style[^>]*TargetType=""\{x:Type\s+Expander\}""[^>]*BasedOn=""\{StaticResource\s+\{x:Type\s+Expander\}\}""", xaml);
        Assert.DoesNotMatch(@"<Style[^>]*TargetType=""\{x:Type\s+GroupBox\}""[^>]*BasedOn=""\{StaticResource\s+\{x:Type\s+GroupBox\}\}""", xaml);
    }

    [Theory]
    [InlineData(0, 3, 320, 0, 0)]
    [InlineData(1, 3, 320, 106.67, 0)]
    [InlineData(2, 3, 320, 213.33, 0)]
    public void ModernUiUtils_CalculateSegmentIndicatorGeometry_ReturnsAccurateMetrics(
        int activeIndex, int count, double totalWidth, double expectedOffsetApprox, double expectedPadding)
    {
        var (offset, width) = ModernUiUtils.CalculateSegmentIndicator(activeIndex, count, totalWidth, expectedPadding);
        Assert.True(Math.Abs(offset - expectedOffsetApprox) < 0.5, $"Expected offset ~{expectedOffsetApprox}, got {offset}");
        Assert.True(width > 0);
    }

    [Theory]
    [InlineData(ModernControlDensity.Compact, 28.0, 7.0, 12.0)]
    [InlineData(ModernControlDensity.Standard, 34.0, 8.0, 13.0)]
    [InlineData(ModernControlDensity.Comfortable, 40.0, 10.0, 14.0)]
    public void ModernUiUtils_GetControlMetrics_AdheresToModernDesignSpecs(
        ModernControlDensity density, double expectedHeight, double expectedRadius, double expectedFontSize)
    {
        var metrics = ModernUiUtils.GetControlMetrics(density);
        Assert.Equal(expectedHeight, metrics.Height);
        Assert.Equal(expectedRadius, metrics.CornerRadius);
        Assert.Equal(expectedFontSize, metrics.FontSize);
    }

    [Fact]
    public void ModernUiUtils_ValidateNoNativeFallback_DetectsRawNativeUsages()
    {
        string badXaml = "<Button Background=\"Gray\" BorderBrush=\"Black\" />";
        string goodXaml = "<Button Style=\"{StaticResource Btn.Primary}\" />";

        Assert.False(ModernUiUtils.IsCompliantModernSnippet(badXaml));
        Assert.True(ModernUiUtils.IsCompliantModernSnippet(goodXaml));
    }

    [Fact]
    public void ModernUiUtils_ExpanderMetrics_ProvidesConsistentHeaderAndPadding()
    {
        var metrics = ModernUiUtils.GetControlMetrics(ModernControlDensity.Standard);
        Assert.True(metrics.Height >= 32.0);
        Assert.True(metrics.CornerRadius >= 8.0);
    }
}
