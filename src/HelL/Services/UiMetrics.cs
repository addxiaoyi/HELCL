using System.Windows;

namespace HelL.Services;

public static class UiMetrics
{
    public const double CaptionSize = 12;
    public const double BodySize = 14;
    public const double HeadingSize = 18;
    public const double TitleSize = 24;
    public const double CaptionLine = 18;
    public const double BodyLine = 22;
    public const double HeadingLine = 28;
    public const double TitleLine = 32;
    public const double ControlHeight = 34;
    public const double PageInset = 24;
    public const double SectionGap = 20;

    public static Thickness PageMargin => new Thickness(PageInset, PageInset, PageInset, 20);
}
