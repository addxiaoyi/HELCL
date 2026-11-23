using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace HelL.Mobile;

public sealed partial class MobileHome
{
    private TopLevel? platformTop;
    private IInsetsManager? insets;
    private IInputPane? inputPane;
    private int nativeBackEvents;
    private int nativeBackKeyEvents;
    private int nativeKeyboardEvents;
    private int nativeSafeAreaEvents;

    private void AttachMobileInsets(object? sender, VisualTreeAttachmentEventArgs e)
    {
        DetachMobileInsets();
        platformTop = TopLevel.GetTopLevel(this);
        if (platformTop is null) return;
        platformTop.BackRequested += OnPlatformBack;
        TopLevel.SetAutoSafeAreaPadding(this, false);
        insets = platformTop.InsetsManager;
        inputPane = platformTop.InputPane;
        if (insets is not null) insets.SafeAreaChanged += SafeAreaChanged;
        if (inputPane is not null) inputPane.StateChanged += InputPaneChanged;
        ApplySafeArea();
        RecordPlatformAudit();
    }

    private void OnPlatformBack(object? sender, RoutedEventArgs e)
    {
        nativeBackEvents++;
        if (RequestBack()) e.Handled = true;
        RecordPlatformAudit();
    }
    private void SafeAreaChanged(object? sender, SafeAreaChangedArgs e) { nativeSafeAreaEvents++; ApplySafeArea(); RecordPlatformAudit(); }
    private void ApplySafeArea()
    {
        if (OperatingSystem.IsAndroid() && insets is not null) Padding = insets.SafeAreaPadding;
    }
    private void InputPaneChanged(object? sender, InputPaneStateEventArgs e)
    {
        bool keyboard = e.NewState == InputPaneState.Open;
        nativeKeyboardEvents++;
        BottomBar.IsVisible = !wide && !keyboard;
        Dispatcher.UIThread.Post(() =>
        {
            if (platformTop?.FocusManager?.GetFocusedElement() is Control focused) focused.BringIntoView();
        });
        RecordPlatformAudit();
    }
    private void DetachMobileInsets()
    {
        if (platformTop is not null) platformTop.BackRequested -= OnPlatformBack;
        if (insets is not null) insets.SafeAreaChanged -= SafeAreaChanged;
        if (inputPane is not null) inputPane.StateChanged -= InputPaneChanged;
        platformTop = null; insets = null; inputPane = null;
    }

    private void RecordPlatformAudit()
    {
        if (!OperatingSystem.IsAndroid() || MobileRuntime.ReportRoot.Length == 0) return;
        try
        {
            Directory.CreateDirectory(MobileRuntime.ReportRoot);
            File.WriteAllText(Path.Combine(MobileRuntime.ReportRoot, "mobile-platform-events.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                schema = 1, platform = "android", backEvents = nativeBackEvents, backKeyEvents = nativeBackKeyEvents, keyboardEvents = nativeKeyboardEvents,
                safeAreaEvents = nativeSafeAreaEvents, safeArea = new { left = Padding.Left, top = Padding.Top, right = Padding.Right, bottom = Padding.Bottom },
                systemTextScale = MobileRuntime.SystemTextScale, page = currentPage,
                instanceNameTap = NativeAuditTap(InstanceName),
                aiSettingsOpen = AiSettingsPanel.IsVisible,
                aiAddressTap = NativeAuditTap(AiAddress),
                aiComposerVisible = AiComposer.IsEffectivelyVisible,
                keyboardOpen = inputPane?.State == InputPaneState.Open, backHook = platformTop is not null,
                insetsHook = insets is not null, inputPaneHook = inputPane is not null
            }));
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { System.Diagnostics.Trace.TraceWarning("Mobile platform evidence: " + error.GetType().Name); }
    }

    private object? NativeAuditTap(Control control)
    {
        if (!control.IsEffectivelyVisible || control.Bounds.Width <= 0 || control.Bounds.Height <= 0) return null;
        var center = new Point(control.Bounds.Width / 2, control.Bounds.Height / 2);
        Point? location = control.TranslatePoint(center, this);
        if (location is null || location.Value.X < 0 || location.Value.Y < 0
            || location.Value.X > Bounds.Width || location.Value.Y > Bounds.Height) return null;
        PixelPoint screen = control.PointToScreen(center);
        return new { x = screen.X, y = screen.Y };
    }
}
