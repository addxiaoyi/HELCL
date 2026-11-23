namespace HelL.Desktop.Models;

public sealed partial class DesktopTask
{
    public bool IsFailed => state == DesktopTaskState.Failed;

    partial void StateAppearanceChanged() => Changed(nameof(IsFailed));
}
