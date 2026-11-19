using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using HelL.Core;
using HelL.Core.Launcher;
using HelL.Portable;

namespace HelL.Mobile;

public sealed partial class MobileHome
{
    private async Task CheckMobileRecheckAsync()
    {
        var failures = new List<string>();
        string firstInstance = selected;
        string otherInstance = "controlled-picker-target";
        launcher.CreateInstance(otherInstance);
        launcher.SetReferenceVersion(otherInstance, "1.21.1");
        int provider = Provider.SelectedIndex;
        int kind = Kind.SelectedIndex;
        string query = Query.Text ?? "";
        try
        {
            Reload(firstInstance);
            Kind.SelectedItem = Kind.Items.OfType<ComboBoxItem>().Single(item => (string?)item.Tag == "resourcepack");
            ShowPage("resources");
            int pages = 0;
            resourceSearchPreview = _ => Task.FromResult<IReadOnlyList<CatalogProject>>(Enumerable.Range(pages++ * 2, 2)
                .Select(index => RecheckProject("", "legacy-slug-" + index)).ToArray());
            await SearchResourcesAsync(false);
            await SearchResourcesAsync(true);
            if (searchResults.Count != 4) failures.Add("empty-id pagination lost distinct slugs");

            CatalogProject old = searchResults[0];
            Provider.SelectedIndex = Provider.SelectedIndex == 0 ? 1 : 0;
            Projects.SelectedItem = old;
            if (DetailsButton.IsEnabled) failures.Add("a stale search row became actionable");
            if (CanOpenProject(old)) failures.Add("a stale search row could open a detail");

            await SearchResourcesAsync(false);
            old = searchResults[0];
            Query.Text = "changed query";
            Projects.SelectedItem = old;
            if (DetailsButton.IsEnabled || CanOpenProject(old)) failures.Add("query editing retained actionable stale results");
            var searched = new TaskCompletionSource<IReadOnlyList<CatalogProject>>(TaskCreationOptions.RunContinuationsAsynchronously);
            resourceSearchPreview = _ => searched.Task;
            Task searching = SearchResourcesAsync(false);
            Reload(otherInstance);
            searched.SetResult([RecheckProject("late-search", "late-search")]);
            await searching;
            if (searchResults.Count != 0) failures.Add("late search results crossed instance binding");

            Reload(firstInstance);
            CatalogProject first = RecheckProject("first-project", "first-project");
            CatalogProject next = RecheckProject("next-project", "next-project");
            PresentProject(first);
            var released = new TaskCompletionSource<IReadOnlyList<CatalogRelease>>(TaskCreationOptions.RunContinuationsAsynchronously);
            resourceReleasesPreview = _ => released.Task;
            Task waiting = LoadReleases();
            PresentProject(next);
            released.SetResult([new CatalogRelease("old-release", "old release", "old.zip", [reference], ["minecraft"], "", 0, new PyDict())]);
            await waiting;
            if (Releases.ItemCount != 0 || DownloadButton.IsEnabled) failures.Add("old release results crossed project binding");
            await CheckLateReleaseContextsAsync(firstInstance, otherInstance, first, failures);

            Reload(firstInstance);
            pageHistory.Clear();
            ShowPage("home", remember: false);
            ShowPage("library");
            ShowPage("manage");
            string originalName = InstanceName.Text ?? "";
            InstanceName.Text = originalName + " draft";
            RequestBack();
            if (confirmation is null) throw new IOException("The controlled back check did not open its draft confirmation.");
            CompleteConfirmation(false);
            await Task.Delay(40);
            if (currentPage != "manage" || pageHistory.Count == 0 || pageHistory.Peek() != "library")
                failures.Add("cancelled back consumed its return target");
            RequestBack();
            if (confirmation is null) throw new IOException("The second back did not retain its draft confirmation.");
            CompleteConfirmation(true);
            await Task.Delay(40);
            if (currentPage != "library") failures.Add("confirmed back skipped the original return target");
            InstanceName.Text = originalName;

            Reload(firstInstance);
            ShowPage("manage");
            var top = TopLevel.GetTopLevel(this) ?? throw new IOException("The controlled file-picker view is detached.");
            string originalCover = Path.Combine(launcher.InstanceFolder(firstInstance), ".hell-cover.png");
            string coverBefore = CoverFingerprint(originalCover);
            var cover = new TaskCompletionSource<IStorageFile?>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task choosing = ChooseCoverAsync(() => cover.Task);
            Reload(otherInstance);
            cover.SetResult(await top.StorageProvider.TryGetFileFromPathAsync(new Uri(Path.Combine(EvidenceRoot, "isolated-cover-fixture.png"))));
            await choosing;
            if (File.Exists(Path.Combine(launcher.InstanceFolder(otherInstance), ".hell-cover.png")))
                failures.Add("a cover picker wrote into a newly selected instance");
            if (CoverFingerprint(originalCover) != coverBefore) failures.Add("a stale cover picker modified its original instance");
            await CheckLateCoverContextAsync(firstInstance, top, originalCover, coverBefore, failures);

            Reload(firstInstance);
            string exportPath = Path.Combine(EvidenceRoot, "controlled-export-original.zip");
            File.WriteAllText(exportPath, "preserve existing destination");
            var export = new TaskCompletionSource<IStorageFile?>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task exporting = ExportInstanceAsync(_ => export.Task);
            Reload(otherInstance);
            export.SetResult(await top.StorageProvider.TryGetFileFromPathAsync(new Uri(exportPath)));
            await exporting;
            if (File.ReadAllText(exportPath) != "preserve existing destination")
                failures.Add("an export picker replaced a destination after changing instance");
            await CheckExportContextAsync(firstInstance, top, exportPath, failures);

            if (LaunchButton.MinHeight < 44 || RailNavigation.Children.OfType<Button>().Any(button => button.MinHeight < 44))
                failures.Add("primary or tablet-navigation targets remained below 44 logical pixels");
        }
        finally
        {
            resourceSearchPreview = null;
            resourceReleasesPreview = null;
            CompleteConfirmation(false);
            Kind.SelectedIndex = kind;
            Provider.SelectedIndex = provider;
            Query.Text = query;
            Reload(firstInstance);
            if (Directory.Exists(launcher.InstanceFolder(otherInstance))) new Instance(otherInstance).Delete();
            Reload(firstInstance);
            pageHistory.Clear();
        }
        File.WriteAllText(Path.Combine(EvidenceRoot, "mobile-recheck-preview.json"), JsonSerializer.Serialize(new
        {
            ok = failures.Count == 0, failures, checks = new[] { "legacy slug pagination", "stale provider selection and row action",
                "query editing", "late search after instance switch", "late releases after project, instance and query switch",
                "cancelled and confirmed back", "cover picker after instance or page switch", "cover cancellation",
                "export picker after instance or page switch", "valid archive export", "44px logical targets" },
            desktopHosted = !OperatingSystem.IsAndroid(), physicalTouchVerified = false, talkBackVerified = false
        }));
        if (failures.Count > 0) throw new IOException("Mobile recheck failed: " + string.Join("; ", failures));
    }

    private async Task CheckLateReleaseContextsAsync(string firstInstance, string otherInstance, CatalogProject entry, List<string> failures)
    {
        PresentProject(entry);
        var released = new TaskCompletionSource<IReadOnlyList<CatalogRelease>>(TaskCreationOptions.RunContinuationsAsynchronously);
        resourceReleasesPreview = _ => released.Task;
        Task loading = LoadReleases();
        Reload(otherInstance);
        released.SetResult([RecheckRelease()]);
        await loading;
        if (Releases.ItemCount != 0 || DownloadButton.IsEnabled) failures.Add("old release results crossed instance binding");

        Reload(firstInstance);
        PresentProject(entry);
        released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        loading = LoadReleases();
        Query.Text = "another query";
        released.SetResult([RecheckRelease()]);
        await loading;
        if (Releases.ItemCount != 0 || DownloadButton.IsEnabled) failures.Add("old release results crossed query binding");
    }

    private async Task CheckLateCoverContextAsync(string firstInstance, TopLevel top, string path, string before, List<string> failures)
    {
        Reload(firstInstance);
        var cover = new TaskCompletionSource<IStorageFile?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task choosing = ChooseCoverAsync(() => cover.Task);
        ShowPage("library");
        ShowPage("manage");
        cover.SetResult(await top.StorageProvider.TryGetFileFromPathAsync(new Uri(Path.Combine(EvidenceRoot, "isolated-cover-fixture.png"))));
        await choosing;
        if (CoverFingerprint(path) != before) failures.Add("a cover picker crossed a page leave and return");

        cover = new(TaskCreationOptions.RunContinuationsAsynchronously);
        choosing = ChooseCoverAsync(() => cover.Task);
        operation!.Cancel();
        cover.SetResult(await top.StorageProvider.TryGetFileFromPathAsync(new Uri(Path.Combine(EvidenceRoot, "isolated-cover-fixture.png"))));
        await choosing;
        if (CoverFingerprint(path) != before || session.Tasks.Entries[0].State != "cancelled")
            failures.Add("a cancelled cover picker modified content or failed to report cancellation");
    }

    private async Task CheckExportContextAsync(string firstInstance, TopLevel top, string path, List<string> failures)
    {
        Reload(firstInstance);
        var export = new TaskCompletionSource<IStorageFile?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task exporting = ExportInstanceAsync(_ => export.Task);
        ShowPage("library");
        ShowPage("manage");
        export.SetResult(await top.StorageProvider.TryGetFileFromPathAsync(new Uri(path)));
        await exporting;
        if (File.ReadAllText(path) != "preserve existing destination") failures.Add("an export picker crossed a page leave and return");
        await ExportInstanceAsync(_ => top.StorageProvider.TryGetFileFromPathAsync(new Uri(path)));
        using var archive = System.IO.Compression.ZipFile.OpenRead(path);
        if (archive.Entries.Count == 0) failures.Add("an unchanged instance context failed to export a valid archive");
    }

    private static string CoverFingerprint(string path) => File.Exists(path)
        ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))) : "";
    private CatalogRelease RecheckRelease() => new("old-release", "old release", "old.zip", [reference], ["minecraft"], "", 0, new PyDict());

    private static CatalogProject RecheckProject(string id, string slug) => new(id, slug, "modrinth", "resourcepack",
        slug, slug, "controlled fixture", "", "", "", 0);
}
