using System;

namespace HelL.Services;

public readonly struct ElevationGradientInfo
{
    public double TopAlpha { get; }
    public double BottomAlpha { get; }
    public double BorderThickness { get; }

    public ElevationGradientInfo(double topAlpha, double bottomAlpha, double borderThickness)
    {
        TopAlpha = topAlpha;
        BottomAlpha = bottomAlpha;
        BorderThickness = borderThickness;
    }
}

public readonly struct CardShellMetrics
{
    public double Padding { get; }
    public bool ShowHeaderInNormalMode { get; }

    public CardShellMetrics(double padding, bool showHeaderInNormalMode)
    {
        Padding = padding;
        ShowHeaderInNormalMode = showHeaderInNormalMode;
    }
}

public readonly struct LaunchButtonVisualState
{
    public string DisplayText { get; }
    public bool IsPulsing { get; }
    public string StatusColor { get; }

    public LaunchButtonVisualState(string displayText, bool isPulsing, string statusColor)
    {
        DisplayText = displayText;
        IsPulsing = isPulsing;
        StatusColor = statusColor;
    }
}

public static class VisualPolishUtils
{
    public const double StandardCardPadding = 18.0;
    public const double StandardFieldSpacing = 12.0;

    public static bool IsSeamlessCard(string cardType)
    {
        return string.Equals(cardType, "banner", StringComparison.OrdinalIgnoreCase);
    }

    public static CardShellMetrics GetCardShellMetrics(string cardType)
    {
        if (IsSeamlessCard(cardType))
        {
            return new CardShellMetrics(0.0, false);
        }
        return new CardShellMetrics(14.0, true);
    }

    public static double CalculatePulseOpacity(double timeSeconds, double periodSeconds, double minOpacity, double maxOpacity)
    {
        if (periodSeconds <= 0.0) return maxOpacity;
        if (minOpacity >= maxOpacity) return minOpacity;

        // 平滑正弦曲线：t=0 为 min，t=period/2 为 max，t=period 为 min
        double phase = (timeSeconds % periodSeconds) / periodSeconds;
        double wave = (1.0 - Math.Cos(phase * 2.0 * Math.PI)) / 2.0;
        return minOpacity + (maxOpacity - minOpacity) * wave;
    }

    public static ElevationGradientInfo GetElevationHighlightGradient(bool isDarkMode)
    {
        if (isDarkMode)
        {
            // 深色模式：顶部 12% 微白高光，底部 2% 暗边，模拟高质感亚克力/毛玻璃边缘
            return new ElevationGradientInfo(0.12, 0.02, 1.0);
        }
        else
        {
            // 浅色模式：更通透的微阴影与 1.0 细腻边框
            return new ElevationGradientInfo(0.06, 0.01, 1.0);
        }
    }

    public static LaunchButtonVisualState GetLaunchButtonVisualState(
        bool isLaunching, bool isGameRunning, int progress, string versionTitle)
    {
        if (isLaunching)
        {
            return new LaunchButtonVisualState(
                $"正在准备 ({progress}%)...",
                isPulsing: true,
                statusColor: "#FFAA00"
            );
        }

        if (isGameRunning)
        {
            return new LaunchButtonVisualState(
                "游戏运行中",
                isPulsing: true,
                statusColor: "#55FF55"
            );
        }

        return new LaunchButtonVisualState(
            "启动游戏",
            isPulsing: false,
            statusColor: "#FFFFFF"
        );
    }
}
