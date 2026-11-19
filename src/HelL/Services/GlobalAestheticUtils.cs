using System;

namespace HelL.Services;

/// <summary>
/// 全局现代审美与多级景深/视觉分层规范
/// </summary>
public static class GlobalAestheticUtils
{
    public sealed class ElevationProfile
    {
        public int Level { get; set; }
        public double Blur { get; set; }
        public double ShadowOpacity { get; set; }
        public double ShadowY { get; set; }
        public double TopHighlightAlpha { get; set; }
    }

    public sealed class CapsuleToolbarMetrics
    {
        public double CornerRadius { get; set; } = 999.0;
        public double BackgroundOpacity { get; set; } = 0.85;
        public double ItemSpacing { get; set; } = 4.0;
        public double PaddingHorizontal { get; set; } = 10.0;
        public double PaddingVertical { get; set; } = 4.0;
    }

    public static ElevationProfile GetElevation(int level)
    {
        return level switch
        {
            1 => new ElevationProfile // 卡片层
            {
                Level = 1,
                Blur = 16.0,
                ShadowOpacity = 0.08,
                ShadowY = 2.0,
                TopHighlightAlpha = 0.08
            },
            2 => new ElevationProfile // 浮动工具栏、胶囊条、下拉浮层
            {
                Level = 2,
                Blur = 24.0,
                ShadowOpacity = 0.14,
                ShadowY = 5.0,
                TopHighlightAlpha = 0.12
            },
            3 => new ElevationProfile // 模态弹窗、主对话框
            {
                Level = 3,
                Blur = 36.0,
                ShadowOpacity = 0.22,
                ShadowY = 10.0,
                TopHighlightAlpha = 0.16
            },
            _ => new ElevationProfile // 平底底板
            {
                Level = 0,
                Blur = 0.0,
                ShadowOpacity = 0.0,
                ShadowY = 0.0,
                TopHighlightAlpha = 0.0
            }
        };
    }

    public static CapsuleToolbarMetrics GetCapsuleToolbarMetrics()
    {
        return new CapsuleToolbarMetrics();
    }
}
