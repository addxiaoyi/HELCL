using Avalonia.Media;

namespace HelL.Desktop.Models;

public sealed partial class DesktopTask
{
    private static readonly IBrush BgNeutral = new SolidColorBrush(Color.Parse("#168E8E93"));
    private static readonly IBrush FgNeutral = new SolidColorBrush(Color.Parse("#A1A1A6"));
    private static readonly IBrush BgFailed = new SolidColorBrush(Color.Parse("#1FFF453A"));
    private static readonly IBrush FgFailed = new SolidColorBrush(Color.Parse("#FF453A"));

    public IBrush BadgeBackground => state == DesktopTaskState.Failed ? BgFailed : BgNeutral;
    public IBrush BadgeForeground => state == DesktopTaskState.Failed ? FgFailed : FgNeutral;

    partial void StateAppearanceChanged()
    {
        Changed(nameof(BadgeBackground));
        Changed(nameof(BadgeForeground));
    }
}
