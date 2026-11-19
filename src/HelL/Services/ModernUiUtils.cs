using System;
using System.Text.RegularExpressions;

namespace HelL.Services;

/// <summary>
/// 现代化控件视觉密度与规格
/// </summary>
public enum ModernControlDensity
{
    Compact,
    Standard,
    Comfortable
}

/// <summary>
/// 现代控件尺寸规格度量
/// </summary>
public sealed class ControlMetrics
{
    public double Height { get; }
    public double CornerRadius { get; }
    public double FontSize { get; }

    public ControlMetrics(double height, double cornerRadius, double fontSize)
    {
        Height = height;
        CornerRadius = cornerRadius;
        FontSize = fontSize;
    }
}

/// <summary>
/// 现代化高级 UI 辅助与几何计算工具
/// </summary>
public static class ModernUiUtils
{
    /// <summary>
    /// 获取符合现代高级设计规范的控件尺寸规格
    /// </summary>
    public static ControlMetrics GetControlMetrics(ModernControlDensity density)
    {
        return density switch
        {
            ModernControlDensity.Compact => new ControlMetrics(28.0, 7.0, 12.0),
            ModernControlDensity.Comfortable => new ControlMetrics(40.0, 10.0, 14.0),
            _ => new ControlMetrics(34.0, 8.0, 13.0)
        };
    }

    /// <summary>
    /// 计算现代分段控件（Segmented Control）或滑块指示器的精确几何位移与宽度
    /// </summary>
    public static (double Offset, double Width) CalculateSegmentIndicator(int activeIndex, int totalCount, double totalWidth, double containerPadding)
    {
        if (totalCount <= 0 || totalWidth <= 0)
        {
            return (0.0, 0.0);
        }

        double availableWidth = Math.Max(0.0, totalWidth - containerPadding * 2);
        double itemWidth = availableWidth / totalCount;
        int clampedIndex = Math.Max(0, Math.Min(activeIndex, totalCount - 1));
        double offset = containerPadding + clampedIndex * itemWidth;

        return (offset, itemWidth);
    }

    /// <summary>
    /// 校验 XAML 或 UI 节点是否符合无原生回退的现代高级规范
    /// </summary>
    public static bool IsCompliantModernSnippet(string xamlSnippet)
    {
        if (string.IsNullOrWhiteSpace(xamlSnippet))
        {
            return true;
        }

        // 检查是否存在典型的生硬原生灰底/黑边/裸控件写法
        if (Regex.IsMatch(xamlSnippet, @"Background\s*=\s*""Gray""", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(xamlSnippet, @"BorderBrush\s*=\s*""Black""", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(xamlSnippet, @"<Button\s*/>", RegexOptions.IgnoreCase))
        {
            return false;
        }

        return true;
    }
}
