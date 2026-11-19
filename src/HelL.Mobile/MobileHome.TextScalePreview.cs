using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;

namespace HelL.Mobile;

public sealed partial class MobileHome
{
    private async Task CheckSystemTextLayoutAsync()
    {
        if (MobileRuntime.SystemTextScale < 1.99) return;
        ShowPage("home");
        HomePage.Offset = Vector.Zero;
        var added = new TextBlock { Text = "动态文字" };
        var parent = new Button { Content = added };
        LaunchSummary.Children.Add(parent);
        ApplyTextScale();
        double firstSize = added.FontSize;
        for (int pass = 0; pass < 3; pass++) ApplyTextScale();
        bool dynamicStable = Math.Abs(firstSize - added.FontSize) < 0.01;
        LaunchSummary.Children.Remove(parent);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        await Capture("home-system-font-200");
        var failures = new List<string>();
        if (InstancePickerLabel.FontSize > 28.01 || added.FontSize > 28.01)
            failures.Add($"Font scaling compounded (static={InstancePickerLabel.FontSize}, dynamic={added.FontSize}).");
        if (!dynamicStable) failures.Add("A new button's inherited text size changed on repeated scaling.");
        Point action = LaunchButton.TranslatePoint(default, HomePage) ?? default;
        bool actionVisible = action.Y >= 0 && action.Y + LaunchButton.Bounds.Height <= HomePage.Bounds.Height;
        if (!actionVisible) failures.Add("The primary instance action is outside the initial home viewport.");
        foreach (TextBlock label in BottomNavigation.GetLogicalDescendants().OfType<TextBlock>())
            if (label.TextLayout.TextLines.Count != 1 || label.TextLayout.WidthIncludingTrailingWhitespace > label.Bounds.Width + 1)
                failures.Add("A bottom navigation label wraps or exceeds its target width: " + label.Text);
        File.WriteAllText(Path.Combine(EvidenceRoot, "mobile-system-font-200.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            ok = failures.Count == 0, failures, systemTextScale = MobileRuntime.SystemTextScale,
            staticFontSize = InstancePickerLabel.FontSize, dynamicFontSize = added.FontSize,
            dynamicInheritedTextStable = dynamicStable,
            primaryActionVisible = actionVisible, bottomLabelsSingleLine = failures.All(message => !message.StartsWith("A bottom")),
            physicalDeviceVerified = false, talkBackVerified = false
        }));
        if (failures.Count > 0) throw new IOException(string.Join(" ", failures));
    }
}
