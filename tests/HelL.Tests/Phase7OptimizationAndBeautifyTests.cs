using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using HelL.Services;

namespace HelL.Tests;

public class Phase7OptimizationAndBeautifyTests
{
    // --- 1. 视觉美化与现代材质计算 (Visual Polish) ---

    [Theory]
    [InlineData(0.0, 2.0, 0.4, 1.0, 0.4)]
    [InlineData(0.5, 2.0, 0.4, 1.0, 0.7)]
    [InlineData(1.0, 2.0, 0.4, 1.0, 1.0)]
    [InlineData(1.5, 2.0, 0.4, 1.0, 0.7)]
    [InlineData(2.0, 2.0, 0.4, 1.0, 0.4)]
    public void VisualPolish_CalculatePulseOpacity_GeneratesSmoothSineWave(double timeSec, double periodSec, double minOp, double maxOp, double expectedApprox)
    {
        double opacity = VisualPolishUtils.CalculatePulseOpacity(timeSec, periodSec, minOp, maxOp);
        Assert.InRange(opacity, minOp, maxOp);
        Assert.True(Math.Abs(opacity - expectedApprox) < 0.05, $"Expected ~{expectedApprox}, got {opacity}");
    }

    [Fact]
    public void VisualPolish_ElevationColors_AdaptToDarkAndLightMode()
    {
        var darkElevation = VisualPolishUtils.GetElevationHighlightGradient(isDarkMode: true);
        var lightElevation = VisualPolishUtils.GetElevationHighlightGradient(isDarkMode: false);

        // 深色模式下顶部高光使用微白半透，底部更暗，增加景深
        Assert.True(darkElevation.TopAlpha > 0.05);
        Assert.True(darkElevation.BottomAlpha < darkElevation.TopAlpha);

        // 浅色模式更通透自然
        Assert.True(lightElevation.BorderThickness >= 1.0);
    }

    [Theory]
    [InlineData(false, false, 0, "1.20.1", "启动游戏", false)]
    [InlineData(true, false, 45, "1.20.1", "正在准备 (45%)...", true)]
    [InlineData(false, true, 100, "1.20.1", "游戏运行中", true)]
    public void VisualPolish_GetLaunchButtonVisualState_ReflectsStateTransitions(
        bool isLaunching, bool isGameRunning, int progress, string versionTitle, string expectedText, bool expectedPulsing)
    {
        var state = VisualPolishUtils.GetLaunchButtonVisualState(isLaunching, isGameRunning, progress, versionTitle);
        Assert.Equal(expectedText, state.DisplayText);
        Assert.Equal(expectedPulsing, state.IsPulsing);
    }

    // --- 2. 交互简化与快捷路径 (Streamline & Simplify) ---

    [Fact]
    public void Streamline_QuickActionDirectories_ResolvesIsolatedAndGlobalPaths()
    {
        string baseDir = @"C:\Games\.minecraft";
        string versionId = "1.20.1-Forge";

        // 版本隔离模式：mods 与 saves 在 versions/1.20.1-Forge/ 内部
        var isolatedPaths = StreamlineLaunchUtils.GetQuickActionDirectories(baseDir, versionId, isIsolated: true);
        Assert.Equal(Path.Combine(baseDir, "versions", versionId, "mods"), isolatedPaths.ModsPath);
        Assert.Equal(Path.Combine(baseDir, "versions", versionId, "saves"), isolatedPaths.SavesPath);
        Assert.Equal(Path.Combine(baseDir, "versions", versionId, "logs"), isolatedPaths.LogsPath);

        // 全局模式：直接使用 .minecraft 根目录下的 mods 与 saves
        var globalPaths = StreamlineLaunchUtils.GetQuickActionDirectories(baseDir, versionId, isIsolated: false);
        Assert.Equal(Path.Combine(baseDir, "mods"), globalPaths.ModsPath);
        Assert.Equal(Path.Combine(baseDir, "saves"), globalPaths.SavesPath);
        Assert.Equal(Path.Combine(baseDir, "logs"), globalPaths.LogsPath);
    }

    [Fact]
    public void Streamline_FilterSettingsForMode_HidesAdvancedNoiseInSimpleMode()
    {
        var allKeys = new List<string>
        {
            "game_dir", "jvm_memory", "java_path", "resolution", "download_source",
            "jvm_extra_args", "gc_preset", "socks5_proxy", "authlib_custom_url", "mcp_enabled"
        };

        var simpleFiltered = StreamlineLaunchUtils.FilterSettingsForMode(allKeys, isSimpleMode: true).ToList();
        var advancedFiltered = StreamlineLaunchUtils.FilterSettingsForMode(allKeys, isSimpleMode: false).ToList();

        // 极简模式应过滤掉复杂选项，减少用户认知负担
        Assert.Contains("game_dir", simpleFiltered);
        Assert.Contains("jvm_memory", simpleFiltered);
        Assert.Contains("java_path", simpleFiltered);
        Assert.DoesNotContain("jvm_extra_args", simpleFiltered);
        Assert.DoesNotContain("socks5_proxy", simpleFiltered);
        Assert.DoesNotContain("mcp_enabled", simpleFiltered);

        // 专家极客模式应展示全部
        Assert.Equal(allKeys.Count, advancedFiltered.Count);
    }

    [Fact]
    public void Streamline_RecommendMissingModFixes_IdentifiesKnownDependencies()
    {
        var installed = new[] { "sodium-fabric.jar", "iris-fabric.jar" };
        var required = new[] { "fabric-api", "sodium" };

        var suggestions = StreamlineLaunchUtils.RecommendMissingModFixes(installed, required);
        Assert.NotEmpty(suggestions);
        Assert.Contains(suggestions, s => s.ModId == "fabric-api");
        Assert.DoesNotContain(suggestions, s => s.ModId == "sodium"); // sodium 已经安装（文件名匹配）
    }

    // --- 3. 性能优化与体验强化 (Performance Optimization) ---

    [Fact]
    public void Performance_ThrottledLogBuffer_BatchesAndCapsLinesEfficiently()
    {
        var buffer = new ThrottledLogBuffer(maxLines: 50);

        // 快速推送 100 条日志
        for (int i = 0; i < 100; i++)
        {
            buffer.EnqueueLog($"[INFO] Log message line #{i}");
        }

        // 提取批处理合并内容
        string combined = buffer.FlushBatch();
        Assert.NotNull(combined);

        // 验证行数受容量上限保护（最大50条），不发生内存暴涨
        int lineCount = buffer.GetSnapshot().Count;
        Assert.True(lineCount <= 50, $"Expected <= 50, got {lineCount}");

        // 并且保留最新的日志
        Assert.Contains("Log message line #99", buffer.GetSnapshot().Last());
    }

    [Theory]
    [InlineData(16384, 12000, 10, false, 4096, 6144)] // 内存充足、轻量模组
    [InlineData(8192, 3500, 150, true, 3072, 3840)]   // 内存紧张但装了多模组+光影，智能收敛避让物理上限
    [InlineData(4096, 1800, 5, false, 1536, 2048)]   // 低配机器保护
    public void Performance_CalculateAdaptiveMemory_RecommendsSafeAllocationRange(
        int totalMb, int availableMb, int modCount, bool hasShaders, int expectedMinMb, int expectedMaxMb)
    {
        var (minMem, maxMem) = PerformanceOptimizerUtils.CalculateAdaptiveMemory(totalMb, availableMb, modCount, hasShaders);
        Assert.True(minMem <= maxMem);
        Assert.True(maxMem <= availableMb + 512, "Max allocated memory cannot severely exceed available physical RAM");
        Assert.Equal(expectedMinMb, minMem);
        Assert.Equal(expectedMaxMb, maxMem);
    }

    [Theory]
    [InlineData(64, 64, 8, 8, 8, 8)]
    [InlineData(128, 128, 16, 16, 16, 16)] // 高清材质自适应按比例缩放
    public void Performance_GetSkinHeadCropRect_CalculatesStandardTextureCoordinates(
        int width, int height, int expectedX, int expectedY, int expectedW, int expectedH)
    {
        var rect = PerformanceOptimizerUtils.GetSkinHeadCropRect(width, height);
        Assert.Equal(expectedX, rect.X);
        Assert.Equal(expectedY, rect.Y);
        Assert.Equal(expectedW, rect.Width);
        Assert.Equal(expectedH, rect.Height);
    }

    [Fact]
    public void Streamline_ClassifyHomeConfigFields_SeparatesPrimaryAndAdvanced()
    {
        var allFields = new[] { "instance", "version", "account", "java", "memory", "resolution", "server" };
        var (primary, advanced) = StreamlineLaunchUtils.ClassifyHomeConfigFields(allFields);

        // 主卡片只保留最核心的3项：实例、版本、账号
        Assert.Equal(new[] { "instance", "version", "account" }, primary);
        // 其余高频干扰项移至高级/折叠层
        Assert.Equal(new[] { "java", "memory", "resolution", "server" }, advanced);
    }

    [Fact]
    public void VisualPolish_UiMetrics_EnsureGenerousBreathingRoom()
    {
        // 视觉留白规范：卡片内衬 >= 18px，表单行间距 >= 12px
        Assert.True(VisualPolishUtils.StandardCardPadding >= 18.0, "Card padding should be at least 18px for breathing room");
        Assert.True(VisualPolishUtils.StandardFieldSpacing >= 12.0, "Field spacing should be at least 12px to prevent tight clutter");
    }

    // --- 4. 启动页 Hero Banner 与版本美化标牌 (Launch Banner Visuals) ---

    [Theory]
    [InlineData("1.20.1-Fabric-0.15.7", "Fabric", "FABRIC", "#3884FF")]
    [InlineData("1.20.4-neoforge-20.4.80", "NeoForge", "NEOFORGE", "#FF7324")]
    [InlineData("1.12.2-forge-14.23.5.2860", "Forge", "FORGE", "#DF8B27")]
    [InlineData("1.21-quilt-0.25", "Quilt", "QUILT", "#9C59D1")]
    [InlineData("1.20.4-OptiFine_HD_U_I7", "OptiFine", "OPTIFINE", "#D93B3B")]
    [InlineData("24w14a", "Snapshot", "快照", "#7D8C99")]
    [InlineData("1.20.1", "Vanilla", "原版", "#2E9B6B")]
    [InlineData("", "Vanilla", "未选择", "#888888")]
    public void LaunchBannerVisual_ResolvesLoaderTypeBadgeAndColor(
        string rawVersion, string expectedLoader, string expectedBadge, string expectedHexColor)
    {
        var meta = LaunchBannerVisualUtils.ResolveLoaderMeta(rawVersion);
        Assert.Equal(expectedLoader, meta.LoaderType);
        Assert.Equal(expectedBadge, meta.BadgeText);
        Assert.Equal(expectedHexColor, meta.AccentHex);
        Assert.False(string.IsNullOrEmpty(meta.Glyph));
    }

    [Theory]
    [InlineData("测试整合", "1.20.1-Fabric-0.15.7", "Minecraft 1.20.1", "Fabric 架构 · 实例「测试整合」")]
    [InlineData("MyWorld", "1.20.4", "Minecraft 1.20.4", "原版官方环境 · 实例「MyWorld」")]
    [InlineData("无版本", "", "未选择游戏版本", "请先在版本管理或配置中选择/下载版本")]
    public void LaunchBannerVisual_FormatHeadline_SeparatesTitleAndSubtitle(
        string instance, string rawVersion, string expectedTitle, string expectedSub)
    {
        var (title, sub) = LaunchBannerVisualUtils.FormatHeadline(instance, rawVersion);
        Assert.Equal(expectedTitle, title);
        Assert.Equal(expectedSub, sub);
    }

    [Fact]
    public void LaunchBannerVisual_GetLaunchButtonDimensions_MatchesPclGoldenRatio()
    {
        var dims = LaunchBannerVisualUtils.GetLaunchButtonDimensions();
        Assert.True(dims.Height >= 44.0, "Launch button height must be at least 44px for prominent touch/click");
        Assert.True(dims.CornerRadius >= 8.0, "Button corner radius should be rounded Fluent/PCL style");
        Assert.True(dims.PaddingHorizontal >= 24.0, "Padding must ensure a wide, confidence-inspiring CTA");
        Assert.True(dims.FontSize >= 15.0, "Font size should be prominent");
    }

    // --- 5. 全局审美与多级景深规范 (Global Aesthetic & Elevation) ---

    [Theory]
    [InlineData(0, 0.0, 0.0, 0.0, 0.0)]
    [InlineData(1, 16.0, 0.08, 2.0, 0.08)] // 卡片级景深
    [InlineData(2, 24.0, 0.14, 5.0, 0.12)] // 浮动工具栏与胶囊栏
    [InlineData(3, 36.0, 0.22, 10.0, 0.16)] // 模态弹窗与大浮层
    public void GlobalAesthetic_GetElevation_ReturnsStandardMetrics(
        int level, double expectedBlur, double expectedOpacity, double expectedY, double expectedTopHighlightAlpha)
    {
        var elevation = GlobalAestheticUtils.GetElevation(level);
        Assert.Equal(expectedBlur, elevation.Blur);
        Assert.Equal(expectedOpacity, elevation.ShadowOpacity);
        Assert.Equal(expectedY, elevation.ShadowY);
        Assert.Equal(expectedTopHighlightAlpha, elevation.TopHighlightAlpha);
    }

    [Fact]
    public void GlobalAesthetic_CapsuleToolbar_ProvidesStandardMetrics()
    {
        var metrics = GlobalAestheticUtils.GetCapsuleToolbarMetrics();
        Assert.Equal(999.0, metrics.CornerRadius);
        Assert.True(metrics.BackgroundOpacity >= 0.8, "Capsule bar should have acrylic backdrop opacity >= 0.8");
        Assert.True(metrics.ItemSpacing >= 4.0, "Item spacing between capsule tools");
    }
}
