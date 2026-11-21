using Avalonia.Controls;
using Avalonia.Interactivity;
using HelL.Desktop.Models;
using HelL.Desktop.Services;
using HelL.Desktop.Views;
using HelL.Portable;

namespace HelL.Desktop;

public sealed partial class MainWindow
{
    private async Task CheckResourceRetryAsync(CatalogProject[] firstPage, CatalogProject[] secondPage)
    {
        var offsets = new List<int>();
        bool rejectNext = true;
        ResourcesView.SearchFixture = (offset, _) =>
        {
            offsets.Add(offset);
            if (offset == ResourceBrowserView.PageSize && rejectNext)
            {
                rejectNext = false;
                return Task.FromException<IReadOnlyList<CatalogProject>>(new IOException("Controlled page fetch failure."));
            }
            return Task.FromResult<IReadOnlyList<CatalogProject>>(offset == 0 ? firstPage : secondPage);
        };
        var query = ResourcesView.FindControl<TextBox>("QueryBox")!;
        var next = ResourcesView.FindControl<Button>("NextPageButton")!;
        var pageLabel = ResourcesView.FindControl<TextBlock>("PageLabel")!;
        var results = ResourcesView.FindControl<ListBox>("Results")!;
        var taskList = TasksView.FindControl<ListBox>("TaskList")!;
        TasksView.FindControl<TextBox>("SearchBox")!.Clear();
        TasksView.FindControl<ComboBox>("StatePicker")!.SelectedIndex = 0;
        DesktopDialogs.Confirmation = (_, _) => Task.FromResult(true);
        try
        {
            query.Text = "page retry fixture";
            await ResourcesView.Search();
            next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForResourceTask();
            if (TasksView.History.First().State != DesktopTaskState.Failed || results.ItemCount != firstPage.Length)
                throw new IOException("A failed page request did not retain its previous results.");
            await RetryTask(TasksView.History.First());
            if (!offsets.SequenceEqual(new[] { 0, ResourceBrowserView.PageSize, ResourceBrowserView.PageSize })
                || results.ItemCount != secondPage.Length || pageLabel.Text?.Contains("第 2 页", StringComparison.Ordinal) != true)
                throw new IOException("Retrying a failed page fetched the previous page instead of the failed offset.");

            query.Text = "new failed page fixture";
            await ResourcesView.Search();
            rejectNext = true;
            next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForResourceTask();
            var failed = TasksView.History.First();
            query.Text = "edited after failed page";
            int beforeRetry = offsets.Count;
            await RetryTask(failed);
            if (offsets.Count != beforeRetry || TasksView.History.First().State != DesktopTaskState.Failed)
                throw new IOException("A stale task retry fetched resources using changed search conditions.");

            const string releaseFailure = "Controlled release fetch failure.";
            ResourcesView.ReleasesFixture = _ => Task.FromException<IReadOnlyList<CatalogRelease>>(new IOException(releaseFailure));
            ResourcesView.PresentProject(firstPage[0]);
            await ResourcesView.LoadReleases();
            failed = TasksView.History.First();
            if (failed.State != DesktopTaskState.Failed || failed.Message != releaseFailure)
                throw new IOException("The resource version failure fixture did not produce a retryable task.");
            ResourcesView.PresentProject(secondPage[0]);
            int releaseRequests = 0;
            ResourcesView.ReleasesFixture = _ =>
            {
                releaseRequests++;
                return Task.FromResult<IReadOnlyList<CatalogRelease>>([]);
            };
            ResourcesView.DetailsFixture = _ => Task.FromResult(new CatalogProjectDetails([], "Fixture", "", ""));
            await RetryTask(failed);
            if (releaseRequests != 0 || TasksView.History.First().State != DesktopTaskState.Failed)
                throw new IOException("Retrying a resource version failure fetched versions for a different project.");
        }
        finally
        {
            DesktopDialogs.Confirmation = null;
            ShowPage("resources");
        }

        async Task RetryTask(DesktopTask failed)
        {
            ShowPage("tasks");
            taskList.SelectedItem = failed;
            TasksView.FindControl<Button>("RetryButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForResourceTask();
            if (ReferenceEquals(TasksView.History.First(), failed))
                throw new IOException("The retry action did not create a new task.");
        }

        async Task WaitForResourceTask()
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(3);
            while (IsWorking && DateTime.UtcNow < deadline) await Task.Delay(20);
            if (IsWorking) throw new TimeoutException("The controlled resource retry did not finish.");
        }
    }
}
