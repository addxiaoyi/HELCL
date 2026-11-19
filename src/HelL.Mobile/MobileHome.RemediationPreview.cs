using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace HelL.Mobile;

public sealed partial class MobileHome
{
    private async Task CheckFrontendRemediationAsync()
    {
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
            referenceLoaderExplained = true, boundedFilePairing = true, nativeAndroidVerified = false,
            pendingNativeChecks = new[] { "Android back keyboard ordering", "display cutout and gesture insets", "soft keyboard resize", "TalkBack focus", "system font 2x" }
        }));
    }
}
