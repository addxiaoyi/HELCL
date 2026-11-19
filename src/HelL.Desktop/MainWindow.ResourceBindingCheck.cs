using Avalonia.Controls;
using HelL.Core;
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
            ResourcesView.SearchFixture = _ => search.Task;
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
        }
        finally { ResourcesView.SearchFixture = null; ResourcesView.ReleasesFixture = null; ResourcesView.DetailsFixture = null; }
    }
}
