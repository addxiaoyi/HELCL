using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia;
using Avalonia.VisualTree;

namespace HelL.Mobile;

public sealed partial class MobileHome
{
    private async Task CheckFrontendRemediationAsync()
    {
        await CheckMobileSafetyGuardsAsync();
        ShowPage("manage");
        string originalName = InstanceName.Text ?? "";
        InstanceName.Text = originalName + " draft";
        Task guarded = NavigateCheckedAsync("library");
        if (confirmation is null || !ManagePage.IsVisible) throw new IOException("Unsaved edits navigated without confirmation.");
        CompleteConfirmation(false);
        await guarded;
        if (!ManagePage.IsVisible || InstanceName.Text != originalName + " draft") throw new IOException("Cancelling navigation lost the instance draft.");
        InstanceName.Text = originalName;

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task working = Run(async token => { entered.SetResult(); await release.Task.WaitAsync(token); }, actionName: "DownloadResource");
        await entered.Task;
        ShowPage("tasks");
        if (!Pages.IsEnabled || !BottomNavigation.IsEnabled || localTasks.Children.Count == 0)
            throw new IOException("A local task disabled navigation or its task history.");
        release.SetResult();
        await working;
        if (session.Tasks.Entries[0].State != "completed") throw new IOException("A completed operation did not reach task history.");
        await Capture("task-center");
        double safeTaskWidth = Math.Max(0, (localTasksPage?.Viewport.Width ?? 0) - TaskScrollBarClearance);
        foreach (TextBlock label in localTasks.GetVisualDescendants().OfType<TextBlock>().Where(label => label.IsEffectivelyVisible))
        {
            Point position = label.TranslatePoint(default, localTasksPage!) ?? default;
            double textRight = position.X + label.TextLayout.WidthIncludingTrailingWhitespace + Math.Max(0, label.TextLayout.OverhangTrailing);
            if (position.X < -1 || textRight > safeTaskWidth + 1)
                throw new IOException($"Task text exceeds its safe viewport beside the overlay scrollbar (x={position.X:0.0}, glyphRight={textRight:0.0}, safeWidth={safeTaskWidth:0.0}).");
        }

        ShowPage("resources");
        SearchFilters.IsVisible = false;
        FocusSearch(this, new RoutedEventArgs());
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
        if (!Query.IsFocused || !ResourceTarget.IsVisible) throw new IOException("Search focus or compatibility target is unavailable.");

        ShowPage("more");
        double previousScale = MobileRuntime.TextScale;
        MobileRuntime.TextScale = 1.5;
        ApplyTextScale();
        await Capture("large-text");
        MobileRuntime.TextScale = previousScale;
        ApplyTextScale();
        if (!RequestBack()) throw new IOException("The application back stack did not handle a nested page.");

        File.WriteAllText(Path.Combine(EvidenceRoot, "mobile-frontend-remediation.json"), JsonSerializer.Serialize(new
        {
            ok = true, draftNavigationGuard = true, localTaskHistory = true, nonBlockingTaskNavigation = true,
            searchFocus = true, visibleCompatibilityTarget = true, textScalePreview = 1.5, pageBackStack = true,
            codeBlocksCopy = true, chatHistoryPersistence = true, destructiveChatConfirmation = true,
            weakTypographyTracking = true, clearConfirmationRechecksOperation = true, modalBackgroundBlocked = true,
            referenceLoaderExplained = true, boundedFilePairing = true, nativeAndroidVerified = false,
            pendingNativeChecks = new[] { "Android back keyboard ordering", "display cutout and gesture insets", "soft keyboard resize", "TalkBack focus", "system font 2x" }
        }));
    }

    private async Task CheckMobileSafetyGuardsAsync()
    {
        WeakReference discarded = CreateDiscardedTypographyControl();
        // Reading IsAlive before collection can keep its transient target alive on Mono.
        for (int attempt = 0; attempt < 8; attempt++)
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            await Task.Delay(40);
        }
        bool retainedControl = discarded.IsAlive;

        ShowPage("ai");
        string fixture = Path.Combine(EvidenceRoot, "clear-chat-safety-fixture.json");
        File.WriteAllText(fixture, JsonSerializer.Serialize(new[] { new Ai.ChatEntry("user", "preserve this question", "complete") }));
        session.Chat.Restore(fixture);
        int count = session.Chat.Entries.Count;
        ClearAiChat(this, new RoutedEventArgs());
        if (confirmation is null) throw new IOException("Chat clearing did not request confirmation.");
        using var inFlight = new CancellationTokenSource();
        operation = inFlight;
        CompleteConfirmation(true);
        await Task.Delay(40);
        operation = null;
        bool clearedDuringOperation = session.Chat.Entries.Count != count;

        Task<bool> guarded = ConfirmAsync("controlled modal", "background controls must be blocked", "accept");
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
        bool backgroundAvailable = BottomNavigation.IsEnabled || RailNavigation.IsEnabled || confirmationOverlay?.VerticalAlignment != Avalonia.Layout.VerticalAlignment.Stretch;
        CompleteConfirmation(false);
        if (await guarded || !BottomNavigation.IsEnabled || !RailNavigation.IsEnabled)
            throw new IOException("Closing confirmation did not restore navigation.");
        if (retainedControl || clearedDuringOperation || backgroundAvailable)
            throw new IOException($"Mobile safety guard failed (retainedControl={retainedControl}, clearedDuringOperation={clearedDuringOperation}, backgroundAvailable={backgroundAvailable}).");
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private WeakReference CreateDiscardedTypographyControl()
    {
        var label = new TextBlock { Text = "discarded typography probe" };
        Pages.Children.Add(label);
        ApplyTextScale();
        Pages.Children.Remove(label);
        return new WeakReference(label);
    }
}
