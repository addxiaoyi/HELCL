using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia;
using Avalonia.VisualTree;

namespace HelL.Mobile;

public sealed partial class MobileHome
{
    private async Task CheckFrontendRemediationAsync()
    {
        await CheckMobileRecheckAsync();
        await CheckMobileSafetyGuardsAsync();
        await CheckMobileDetailsAsync();
        await CheckMobileIconsAsync();
        await CheckMobileRefinementAsync();
        await CheckMobileReadingAsync();
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
        session.Tasks.ClearFinished();
        ShowPage("tasks");
        var emptyClear = localTasks.Children.OfType<Button>().Single(button => button.Name == "ClearFinishedTasksButton");
        if (emptyClear.IsEnabled || !localTasks.Children.OfType<TextBlock>().Any(label => label.Text == "暂无已结束任务可清除。"))
            throw new IOException("An empty task history did not explain why clearing is unavailable.");
        Task working = Run(async token => { entered.SetResult(); await release.Task.WaitAsync(token); }, actionName: "DownloadResource");
        await entered.Task;
        ShowPage("tasks");
        if (!Pages.IsEnabled || !BottomNavigation.IsEnabled || localTasks.Children.Count == 0)
            throw new IOException("A local task disabled navigation or its task history.");
        var runningClear = localTasks.Children.OfType<Button>().Single(button => button.Name == "ClearFinishedTasksButton");
        if (runningClear.IsEnabled || !localTasks.Children.OfType<TextBlock>().Any(label => label.Text == "运行中的任务结束后可清理历史记录。"))
            throw new IOException("An active task did not explain why clearing is unavailable.");
        release.SetResult();
        await working;
        if (session.Tasks.Entries[0].State != "completed") throw new IOException("A completed operation did not reach task history.");
        var finishedClear = localTasks.Children.OfType<Button>().Single(button => button.Name == "ClearFinishedTasksButton");
        if (!finishedClear.IsEnabled) throw new IOException("A finished task could not be cleared from its history.");
        await Capture("task-center");
        double safeTaskWidth = Math.Max(0, (localTasksPage?.Viewport.Width ?? 0) - TaskScrollBarClearance);
        foreach (TextBlock label in localTasks.GetVisualDescendants().OfType<TextBlock>().Where(label => label.IsEffectivelyVisible))
        {
            Point position = label.TranslatePoint(default, localTasksPage!) ?? default;
            double textRight = position.X + label.TextLayout.WidthIncludingTrailingWhitespace + Math.Max(0, label.TextLayout.OverhangTrailing);
            if (position.X < -1 || textRight > safeTaskWidth + 1)
                throw new IOException($"Task text exceeds its safe viewport beside the overlay scrollbar (text={label.Text}, x={position.X:0.0}, width={label.Bounds.Width:0.0}, size={label.FontSize:0.0}, glyphRight={textRight:0.0}, safeWidth={safeTaskWidth:0.0}).");
        }
        finishedClear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        string[] confirmationCopy = confirmationOverlay?.GetLogicalDescendants().OfType<TextBlock>()
            .Select(label => label.Text ?? "").ToArray() ?? [];
        string[] confirmationActions = confirmationOverlay?.GetLogicalDescendants().OfType<Button>()
            .Select(button => button.Content?.ToString() ?? "").ToArray() ?? [];
        if (confirmation is null || session.Tasks.Entries.Count != 1
            || !confirmationCopy.Any(copy => copy.Contains("移除 1 条已结束任务", StringComparison.Ordinal))
            || !confirmationActions.Contains("清除历史") || !confirmationActions.Contains("取消"))
            throw new IOException("Clearing finished tasks did not show the count and clear-specific confirmation actions.");
        CompleteConfirmation(false);
        await Task.Delay(30);
        if (session.Tasks.Entries.Count != 1)
            throw new IOException("Declining the task-history confirmation removed finished records.");
        finishedClear = localTasks.Children.OfType<Button>().Single(button => button.Name == "ClearFinishedTasksButton");
        finishedClear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (confirmation is null) throw new IOException("Task-history clearing could not be confirmed.");
        string laterTask = session.Tasks.Start("Finishes while confirmation is open", "Mobile preview");
        session.Tasks.Update(laterTask, "completed", "Finished during confirmation.");
        CompleteConfirmation(true);
        await Task.Delay(30);
        bool clearFeedbackVisible = session.Tasks.Entries.Count == 1 && session.Tasks.Entries[0].Id == laterTask
            && localTasks.Children.OfType<TextBlock>().Any(label => label.Text == "已清除 1 条已结束任务。")
            && localTasks.Children.OfType<Button>().Single(button => button.Name == "ClearFinishedTasksButton") is { IsEnabled: true } remainingButton
            && remainingButton.Content?.ToString() == "清除已结束任务（1）"
            && AutomationProperties.GetName(remainingButton) == "清除已结束任务，1 条";
        if (!clearFeedbackVisible)
            throw new IOException("Task clearing did not report its removal or preserve tasks that ended after confirmation opened.");
        var clearedButton = localTasks.Children.OfType<Button>().Single(button => button.Name == "ClearFinishedTasksButton");
        clearedButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (confirmation is null) throw new IOException("A remaining finished task could not be confirmed for clearing.");
        CompleteConfirmation(true);
        await Task.Delay(30);
        bool emptyHistoryFeedbackVisible = session.Tasks.Entries.Count == 0
            && localTasks.Children.OfType<TextBlock>().Any(label => label.Text == "已清除 1 条已结束任务。")
            && localTasks.Children.OfType<TextBlock>().Any(label => label.Text == "暂无已结束任务可清除。")
            && localTasks.Children.OfType<Button>().Single(button => button.Name == "ClearFinishedTasksButton") is { IsEnabled: false };
        if (!emptyHistoryFeedbackVisible)
            throw new IOException("Clearing the remaining finished task did not update its count, state, and empty history hint.");
        clearedButton = localTasks.Children.OfType<Button>().Single(button => button.Name == "ClearFinishedTasksButton");
        if (clearedButton.IsEnabled) throw new IOException("Task clearing stayed enabled after the finished history was removed.");
        await Capture("task-center-cleared");

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
            clearFinishedTaskAvailability = true, clearFinishedTaskFeedback = clearFeedbackVisible && emptyHistoryFeedbackVisible,
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
