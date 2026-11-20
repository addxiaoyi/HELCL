using Avalonia.Controls;
using Avalonia.Interactivity;
using HelL.Core;
using HelL.Desktop.Views;
using HelL.Portable;

namespace HelL.Desktop;

public sealed partial class MainWindow
{
    private async Task CheckResourceBinding()
    {
        ShowPage("resources");
        var source = ResourcesView.FindControl<ComboBox>("SourcePicker")!;
        var results = ResourcesView.FindControl<ListBox>("Results")!;
        var releases = ResourcesView.FindControl<ComboBox>("ReleasePicker")!;
        var docs = ResourcesView.FindControl<StackPanel>("ProjectDocuments")!;
        var project = new CatalogProject("fixture", "fixture", "modrinth", "resourcepack", "Old target fixture", "fixture", "fixture", "", "", "", 0);
        var release = new CatalogRelease("release-fixture", "fixture", "fixture.zip", [], [], "", 0, new PyDict());
        source.SelectedIndex = 0;
        await Task.Delay(50);
        try
        {
            var search = new TaskCompletionSource<IReadOnlyList<CatalogProject>>(TaskCreationOptions.RunContinuationsAsynchronously);
            ResourcesView.SearchFixture = (_, _) => search.Task;
            Task pendingSearch = ResourcesView.Search();
            source.SelectedIndex = 1;
            search.SetResult([project]);
            await pendingSearch;
            if ((results.ItemsSource as IEnumerable<CatalogProject>)?.Any(row => row.Id == project.Id) == true)
                throw new IOException("Old search results crossed a changed target binding.");

            ResourcesView.PresentProject(project);
            var files = new TaskCompletionSource<IReadOnlyList<CatalogRelease>>(TaskCreationOptions.RunContinuationsAsynchronously);
            ResourcesView.ReleasesFixture = _ => files.Task;
            Task pendingFiles = ResourcesView.LoadReleases();
            source.SelectedIndex = 0;
            files.SetResult([release]);
            await pendingFiles;
            if (releases.ItemCount != 0) throw new IOException("Old release choices crossed a changed target binding.");

            ResourcesView.PresentProject(project);
            ResourcesView.ReleasesFixture = _ => Task.FromResult<IReadOnlyList<CatalogRelease>>([release]);
            var details = new TaskCompletionSource<CatalogProjectDetails>(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ResourcesView.DetailsFixture = _ => { entered.TrySetResult(); return details.Task; };
            Task pendingDetails = ResourcesView.LoadReleases();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            source.SelectedIndex = 1;
            details.SetResult(new CatalogProjectDetails([], "stale private document", "old note", "https://modrinth.com/"));
            await pendingDetails;
            if (docs.Children.Count != 0) throw new IOException("An old project document crossed a changed target binding.");

            var firstPage = Enumerable.Range(0, 30).Select(index => new CatalogProject(
                "page-fixture-" + index, "Pagination fixture " + index, "modrinth", "resourcepack",
                "fixture summary", "fixture", "fixture", "", "", "", index)).ToArray();
            var lastPage = firstPage.Take(2).Select(project => project with { Id = "last-" + project.Id }).ToArray();
            int request = 0;
            var pageTwo = new TaskCompletionSource<IReadOnlyList<CatalogProject>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var pageTwoRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var requestedOffsets = new List<int>();
            ResourcesView.SearchFixture = (offset, _) =>
            {
                requestedOffsets.Add(offset);
                int current = request++;
                if (current == 1)
                {
                    pageTwoRequested.TrySetResult();
                    return pageTwo.Task;
                }
                return Task.FromResult<IReadOnlyList<CatalogProject>>(current switch
                {
                    0 or 3 => firstPage,
                    1 or 4 => lastPage,
                    _ => Array.Empty<CatalogProject>()
                });
            };
            await ResourcesView.Search();
            var next = ResourcesView.FindControl<Button>("NextPageButton")!;
            var previous = ResourcesView.FindControl<Button>("PreviousPageButton")!;
            var pageLabel = ResourcesView.FindControl<TextBlock>("PageLabel")!;
            var resultNotice = ResourcesView.FindControl<TextBlock>("ResultNotice")!;
            if (results.ItemCount != firstPage.Length || !next.IsEnabled || previous.IsEnabled)
                throw new IOException("The first full resource page did not expose its next-page action.");
            next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await pageTwoRequested.Task.WaitAsync(TimeSpan.FromSeconds(2));
            ResourcesView.FindControl<Button>("SearchButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            pageTwo.SetResult(lastPage);
            for (int attempt = 0; attempt < 100 && IsWorking; attempt++) await Task.Delay(20);
            if (requestedOffsets.Count != 2 || requestedOffsets[0] != 0 || requestedOffsets[1] != ResourceBrowserView.PageSize
                || results.ItemCount != lastPage.Length || !next.IsEnabled || !previous.IsEnabled
                || pageLabel.Text?.Contains("第 2 页", StringComparison.Ordinal) != true)
                throw new IOException("A short non-empty resource page was mistaken for the end of pagination.");
            next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (int attempt = 0; attempt < 100 && IsWorking; attempt++) await Task.Delay(20);
            if (results.ItemCount != lastPage.Length || next.IsEnabled || !previous.IsEnabled
                || pageLabel.Text?.Contains("第 2 页", StringComparison.Ordinal) != true
                || resultNotice.Text?.Contains("末页", StringComparison.Ordinal) != true)
                throw new IOException("An empty next resource page cleared the last non-empty results or advanced pagination.");

            previous.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (int attempt = 0; attempt < 100 && IsWorking; attempt++) await Task.Delay(20);
            if (results.ItemCount != firstPage.Length || next.IsEnabled != true || previous.IsEnabled
                || pageLabel.Text?.Contains("第 1 页", StringComparison.Ordinal) != true)
                throw new IOException("Returning from the last resource page did not restore the first page.");
            next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (int attempt = 0; attempt < 100 && IsWorking; attempt++) await Task.Delay(20);
            if (results.ItemCount != lastPage.Length || next.IsEnabled || !previous.IsEnabled
                || resultNotice.Text?.Contains("末页", StringComparison.Ordinal) != true)
                throw new IOException("Returning to the known last page lost its terminal-page notice.");

            var query = ResourcesView.FindControl<TextBox>("QueryBox")!;
            query.Text = "no matching resource";
            if (next.IsEnabled || previous.IsEnabled)
                throw new IOException("Changing the resource query left stale pagination controls enabled.");
            if (results.ItemCount != lastPage.Length
                || pageLabel.Text?.Contains("上次结果：第 2 页", StringComparison.Ordinal) != true
                || resultNotice.Text?.Contains("搜索条件已改变", StringComparison.Ordinal) != true)
                throw new IOException("Editing a query while idle did not clearly identify the retained page as stale.");
            await ResourcesView.Search();
            var discovery = ResourcesView.FindControl<Control>("DiscoveryPanel")!;
            if (results.ItemCount != 0 || next.IsEnabled || previous.IsEnabled || !discovery.IsVisible
                || resultNotice.Text?.Contains("没有匹配结果", StringComparison.Ordinal) != true)
                throw new IOException("A new empty resource search inherited the old last-page state.");

            int boundaryOffset = -1;
            ResourcesView.SearchFixture = (offset, _) =>
            {
                boundaryOffset = offset;
                return Task.FromResult<IReadOnlyList<CatalogProject>>(firstPage);
            };
            await ResourcesView.Search();
            typeof(ResourceBrowserView).GetField("pageIndex", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(ResourcesView, ResourceBrowserView.MaximumPageIndex - 1);
            typeof(ResourceBrowserView).GetField("displayedPage", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(ResourcesView, ResourceBrowserView.MaximumPageIndex - 1);
            next.IsEnabled = true;
            next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (int attempt = 0; attempt < 100 && IsWorking; attempt++) await Task.Delay(20);
            if (boundaryOffset != PortableCatalog.MaximumSearchOffset - (PortableCatalog.MaximumSearchOffset % ResourceBrowserView.PageSize)
                || results.ItemCount != firstPage.Length || pageLabel.Text?.Contains($"第 {ResourceBrowserView.MaximumPageIndex + 1} 页", StringComparison.Ordinal) != true
                || next.IsEnabled || resultNotice.Text?.Contains("达到可翻页范围上限", StringComparison.Ordinal) != true)
                throw new IOException("The final valid resource page did not explain why further pagination is unavailable.");
            next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (boundaryOffset != PortableCatalog.MaximumSearchOffset - (PortableCatalog.MaximumSearchOffset % ResourceBrowserView.PageSize))
                throw new IOException("The disabled resource pager attempted an out-of-range offset.");
        }
        finally { ResourcesView.SearchFixture = null; ResourcesView.ReleasesFixture = null; ResourcesView.DetailsFixture = null; }
    }
}
