using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Threading;
using HelL.Portable;

namespace HelL.Desktop;

public sealed partial class MainWindow
{
    private async Task CheckInitialResourceRefreshAsync()
    {
        var source = ResourcesView.FindControl<ComboBox>("SourcePicker")!;
        var results = ResourcesView.FindControl<ListBox>("Results")!;
        var originalSearch = ResourcesView.SearchFixture;
        int originalSource = source.SelectedIndex;
        var heldSearch = new TaskCompletionSource<IReadOnlyList<CatalogProject>>(TaskCreationOptions.RunContinuationsAsynchronously);
        CatalogProject[] rows = [new("initial-refresh", "Initial refresh", "modrinth", "resourcepack", "", "", "", "", "", "", 0)];
        int requests = 0;
        try
        {
            ResourcesView.SearchFixture = (_, _) => ++requests switch
            {
                1 => Task.FromException<IReadOnlyList<CatalogProject>>(new IOException("Controlled first search failure.")),
                2 => heldSearch.Task,
                _ => Task.FromResult<IReadOnlyList<CatalogProject>>(rows)
            };
            source.SelectedIndex = source.SelectedIndex == 0 ? 1 : 0;
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            if (requests != 0) throw new IOException("Changing filters before any explicit search queried resources.");

            await ResourcesView.Search();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            if (requests != 1 || IsWorking) throw new IOException("The initial failed search retried without a filter change.");
            source.SelectedIndex = source.SelectedIndex == 0 ? 1 : 0;
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            if (requests != 2 || !IsWorking) throw new IOException("Changing the source after the first failed search did not start a fresh request.");
            source.SelectedIndex = source.SelectedIndex == 0 ? 1 : 0;
            heldSearch.SetResult([rows[0] with { Id = "initial-stale" }]);
            DateTime deadline = DateTime.UtcNow.AddSeconds(3);
            do
            {
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                if (!IsWorking) break;
                await Task.Delay(20);
            } while (DateTime.UtcNow < deadline);
            if (IsWorking || requests != 3 || results.Items.OfType<CatalogProject>().SingleOrDefault()?.Id != rows[0].Id)
                throw new IOException("Changing filters before the first successful search did not load the latest results.");
            File.WriteAllText(Path.Combine(Program.DataRoot, "desktop-resource-initial-refresh.json"), JsonSerializer.Serialize(new
            {
                ok = true, unopenedSearchStaysIdle = true, failedSearchDoesNotLoop = true,
                filtersRecoverFirstFailure = true, filtersResumeBeforeFirstSuccess = true
            }));
        }
        finally
        {
            heldSearch.TrySetResult([]);
            source.SelectedIndex = originalSource;
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                DateTime deadline = DateTime.UtcNow.AddSeconds(3);
                while (IsWorking && DateTime.UtcNow < deadline) await Task.Delay(20);
                if (IsWorking) throw new TimeoutException("The initial resource search fixture did not settle.");
            }
            finally { ResourcesView.SearchFixture = originalSearch; }
        }
    }

    private async Task CheckResourceRefreshAsync()
    {
        var source = ResourcesView.FindControl<ComboBox>("SourcePicker")!;
        var order = ResourcesView.FindControl<ComboBox>("OrderPicker")!;
        var query = ResourcesView.FindControl<TextBox>("QueryBox")!;
        var results = ResourcesView.FindControl<ListBox>("Results")!;
        var originalSearch = ResourcesView.SearchFixture;
        int originalSource = source.SelectedIndex, originalOrder = order.SelectedIndex;
        string? originalQuery = query.Text;
        string originalPage = currentPage;
        bool wasClosing = closing;
        var requests = new List<(int Source, int Order, string Query)>();
        var failures = new List<string>();
        CatalogProject[] rows = [new("refresh-fixture", "Refresh fixture", "modrinth", "resourcepack", "", "", "", "", "", "", 0)];
        var busyGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var heldSearch = new TaskCompletionSource<IReadOnlyList<CatalogProject>>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            Closing += KeepSmokeOpen;
            ResourcesView.SearchFixture = RecordSearch;
            ShowPage("resources");
            query.Text = "automatic refresh fixture";
            await ResourcesView.Search();
            await DrainAsync();
            requests.Clear();

            ShowPage("launch");
            ReloadInstances(SelectedName);
            await DrainAsync();
            Check(requests.Count == 0, "Refreshing instances started a search on the hidden resource page.");
            requests.Clear();
            ShowPage("resources");
            ShowPage("resources");
            await DrainAsync();
            Check(requests.Count == 1, "Returning to resources did not resume exactly one pending search.");

            requests.Clear();
            source.SelectedIndex = source.SelectedIndex == 0 ? 1 : 0;
            source.SelectedIndex = source.SelectedIndex == 0 ? 1 : 0;
            order.SelectedIndex = order.SelectedIndex == 0 ? 1 : 0;
            await DrainAsync();
            Check(requests.Count == 1 && requests[0] == CurrentRequest(), "Rapid filter changes did not coalesce into the latest search.");

            requests.Clear();
            Task busy = RunTask(_ => busyGate.Task, "ResourceRefreshFixture");
            source.SelectedIndex = source.SelectedIndex == 0 ? 1 : 0;
            order.SelectedIndex = order.SelectedIndex == 0 ? 1 : 0;
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Check(requests.Count == 0, "An automatic resource search bypassed the busy task gate.");
            busyGate.SetResult();
            await busy;
            await DrainAsync();
            Check(requests.Count == 1 && requests[0] == CurrentRequest(), "Finishing another task lost the latest resource filters.");

            requests.Clear();
            ResourcesView.SearchFixture = (offset, token) =>
            {
                _ = RecordSearch(offset, token);
                return requests.Count == 1 ? heldSearch.Task : Task.FromResult<IReadOnlyList<CatalogProject>>(rows);
            };
            Task searching = ResourcesView.Search();
            source.SelectedIndex = source.SelectedIndex == 0 ? 1 : 0;
            heldSearch.SetResult([rows[0] with { Id = "stale-refresh" }]);
            await searching;
            await DrainAsync();
            Check(requests.Count == 2 && requests[^1] == CurrentRequest()
                && results.Items.OfType<CatalogProject>().SingleOrDefault()?.Id == rows[0].Id,
                "A filter change during search failed to replace the stale request with the latest results.");
            ResourcesView.SearchFixture = RecordSearch;

            requests.Clear();
            source.SelectedIndex = source.SelectedIndex == 0 ? 1 : 0;
            query.Text = "draft must wait for search";
            await DrainAsync();
            Check(requests.Count == 0, "Editing the query did not cancel the queued automatic search.");
            await ResourcesView.Search();
            await DrainAsync();
            Check(requests.Count == 1 && requests[0] == CurrentRequest(), "Explicit search did not submit the edited query exactly once.");

            requests.Clear();
            source.SelectedIndex = source.SelectedIndex == 0 ? 1 : 0;
            await ResourcesView.Search();
            await DrainAsync();
            Check(requests.Count == 1, "Explicit search left a duplicate automatic search queued.");

            requests.Clear();
            source.SelectedIndex = source.SelectedIndex == 0 ? 1 : 0;
            ShowPage("tasks");
            await DrainAsync();
            Check(requests.Count == 0, "A queued search started after leaving resources.");
            ShowPage("resources");
            await DrainAsync();
            Check(requests.Count == 1, "Reentering resources did not resume the deferred search once.");

            requests.Clear();
            source.SelectedIndex = source.SelectedIndex == 0 ? 1 : 0;
            closing = true;
            await DrainAsync();
            Check(requests.Count == 0, "A queued resource search started after window closing began.");

            File.WriteAllText(Path.Combine(Program.DataRoot, "desktop-resource-refresh.json"), JsonSerializer.Serialize(new
            {
                ok = failures.Count == 0, failures, hiddenPageDeferred = true, latestFiltersCoalesced = true,
                busyTaskResumed = true, staleSearchReplaced = true, editedQueryWaitsForSubmit = true,
                explicitSearchConsumesPending = true, queuedNavigationDeferred = true, closingSkipsSearch = true
            }));
            if (failures.Count > 0) throw new IOException(string.Join("; ", failures));
        }
        finally
        {
            busyGate.TrySetResult();
            heldSearch.TrySetResult([]);
            Closing -= KeepSmokeOpen;
            closing = wasClosing;
            ResourcesView.SearchFixture = RecordSearch;
            ShowPage("launch");
            source.SelectedIndex = originalSource;
            order.SelectedIndex = originalOrder;
            query.Text = originalQuery;
            try { await DrainAsync(); }
            finally { ResourcesView.SearchFixture = originalSearch; ShowPage(originalPage); }
        }

        (int, int, string) CurrentRequest() => (source.SelectedIndex, order.SelectedIndex, query.Text ?? "");

        Task<IReadOnlyList<CatalogProject>> RecordSearch(int offset, CancellationToken token)
        {
            requests.Add(CurrentRequest());
            return Task.FromResult<IReadOnlyList<CatalogProject>>(rows);
        }

        void Check(bool passed, string failure) { if (!passed) failures.Add(failure); }

        static void KeepSmokeOpen(object? sender, WindowClosingEventArgs args) => args.Cancel = true;

        async Task DrainAsync()
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(3);
            do
            {
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                if (!IsWorking) return;
                await Task.Delay(20);
            } while (DateTime.UtcNow < deadline);
            throw new TimeoutException("The controlled resource refresh did not settle.");
        }
    }
}
