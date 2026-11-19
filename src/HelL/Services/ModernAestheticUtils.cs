using System;

namespace HelL.Services;

public enum ControlShapeKind
{
    Control,
    Card,
    Modal,
    Pill
}

public enum ModernElevationLevel
{
    Flat = 0,
    Card = 1,
    Flyout = 2,
    Modal = 3
}

public enum TagIntent
{
    Neutral,
    Accent,
    Success,
    Warning,
    Danger
}

public sealed class ModernElevationProfile
{
    public ModernElevationLevel Level { get; set; }
    public double Blur { get; set; }
    public double ShadowOpacity { get; set; }
    public double ShadowY { get; set; }
    public double TopHighlightAlpha { get; set; }
}

public sealed class ModernSwitchMetrics
{
    public double Width { get; set; } = 40.0;
    public double Height { get; set; } = 22.0;
    public double KnobDiameter { get; set; } = 16.0;
    public double TravelDistance { get; set; } = 18.0;
    public double CornerRadius { get; set; } = 11.0;
}

public sealed class SearchBoxMetrics
{
    public double Height { get; set; } = 34.0;
    public double CornerRadius { get; set; } = 8.0;
    public double IconSize { get; set; } = 13.0;
}

public static class ModernAestheticUtils
{
    public static double GetCornerRadius(ControlShapeKind kind) => kind switch
    {
        ControlShapeKind.Control => 6.0,
        ControlShapeKind.Card => 10.0,
        ControlShapeKind.Modal => 14.0,
        ControlShapeKind.Pill => 999.0,
        _ => 6.0
    };

    public static ModernElevationProfile GetElevation(ModernElevationLevel level) => level switch
    {
        ModernElevationLevel.Card => new ModernElevationProfile
        {
            Level = ModernElevationLevel.Card,
            Blur = 16.0,
            ShadowOpacity = 0.08,
            ShadowY = 2.0,
            TopHighlightAlpha = 0.08
        },
        ModernElevationLevel.Flyout => new ModernElevationProfile
        {
            Level = ModernElevationLevel.Flyout,
            Blur = 24.0,
            ShadowOpacity = 0.14,
            ShadowY = 5.0,
            TopHighlightAlpha = 0.12
        },
        ModernElevationLevel.Modal => new ModernElevationProfile
        {
            Level = ModernElevationLevel.Modal,
            Blur = 36.0,
            ShadowOpacity = 0.22,
            ShadowY = 10.0,
            TopHighlightAlpha = 0.16
        },
        _ => new ModernElevationProfile
        {
            Level = ModernElevationLevel.Flat,
            Blur = 0.0,
            ShadowOpacity = 0.0,
            ShadowY = 0.0,
            TopHighlightAlpha = 0.0
        }
    };

    public static ModernSwitchMetrics GetModernSwitchMetrics() => new ModernSwitchMetrics();

    public static (string Fg, string Bg) ResolveTagColors(TagIntent intent) => intent switch
    {
        TagIntent.Accent => ("B.AccentText", "B.AccentSoft"),
        TagIntent.Danger => ("B.Danger", "B.DangerSoft"),
        TagIntent.Warning => ("B.Warn", "B.WarnSoft"),
        TagIntent.Success => ("B.Ok", "B.AccentSoft2"),
        _ => ("B.InkMuted", "B.LineSoft")
    };

    public static SearchBoxMetrics GetSearchBoxMetrics() => new SearchBoxMetrics();

    public static double GetMicroInteractionScale(bool isPressed) => isPressed ? 0.985 : 1.0;
}
