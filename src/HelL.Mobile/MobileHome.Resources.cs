using Avalonia.Controls;
using Avalonia.Interactivity;
using HelL.Core.Launcher;
using HelL.Portable;

namespace HelL.Mobile;

public sealed partial class MobileHome
{
    private const int ResourcePageSize = 30;
    private int resourceRevision;
    private int resourceOffset;
    private sealed record ResourceBinding(string Instance, string Version, string Loader, string Kind,
        string Provider, string Query, string Order);
    private ResourceBinding? resourceSearchBinding;
    private ResourceBinding? resourceProjectBinding;
    private Avalonia.Vector resourceListOffset;
    private Func<CancellationToken, Task<IReadOnlyList<CatalogProject>>>? resourceSearchPreview;
    private Func<CancellationToken, Task<IReadOnlyList<CatalogRelease>>>? resourceReleasesPreview;
    private ResourceBinding ResourceContext() => new(selected, reference,
        selected.Length == 0 ? "" : launcher.ReferenceLoader(selected), SelectedKind, SelectedProvider,
        Query.Text?.Trim() ?? "", resourceOrder);
    private static (string Source, string Key) ProjectIdentity(CatalogProject entry) =>
        (entry.Source, string.IsNullOrWhiteSpace(entry.Id) ? entry.Slug : entry.Id);
    private bool SearchBindingCurrent => resourceSearchBinding is not null && resourceSearchBinding == ResourceContext();
    private bool ProjectBindingCurrent => project is not null && resourceProjectBinding == ResourceContext();
    private void ResourceQueryChanged(object? sender, TextChangedEventArgs e)
    {
        if (ready) InvalidateResourceSelection();
    }
    private void InvalidateResourceSelection()
    {
        ++resourceRevision;
        project = null;
        resourceProjectBinding = null;
        Projects.SelectedItem = null;
        Releases.ItemsSource = Array.Empty<CatalogRelease>();
        DetailsButton.IsEnabled = DownloadButton.IsEnabled = false;
        LoadMoreResourcesButton.IsVisible = false;
    }
    private void FiltersChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!ready) return;
        if (SelectedKind == "world" && Provider.SelectedIndex == 0) Provider.SelectedIndex = 1;
        SyncCategory();
        InvalidateResourceSelection();
        SearchNotice.IsVisible = true;
        SearchNotice.Text = searchResults.Count == 0 ? "选择资源类型，搜索与当前实例兼容的内容。" : "筛选已修改，显示上次结果；点击搜索更新。";
        LoadMoreResourcesButton.IsVisible = false;
    }
    private void ClearProjects()
    {
        InvalidateResourceSelection();
        resourceSearchBinding = null;
        searchResults = [];
        Projects.ItemsSource = Array.Empty<CatalogProject>();
        SearchPanel.IsVisible = true;
        DetailsPanel.IsVisible = false;
        DetailsButton.IsEnabled = DownloadButton.IsEnabled = false;
        SearchNotice.IsVisible = true;
        SearchNotice.Text = selected.Length == 0 ? "先创建一个实例，资源将按它的版本筛选。" : "选择资源类型，浏览适配当前实例的内容。";
        SearchRetry.IsVisible = selected.Length > 0;
        SearchRetry.Content = "浏览资源";
    }
    private async void SearchResources(object? sender, RoutedEventArgs e) => await SearchResourcesAsync(false);
    private async void LoadMoreResources(object? sender, RoutedEventArgs e) => await SearchResourcesAsync(true);
    private Task SearchResourcesAsync(bool more) => Run(async token =>
    {
        RequireSelection();
        var binding = ResourceContext();
        var target = catalog.Target(binding.Instance, binding.Version);
        int offset = more && binding == resourceSearchBinding ? resourceOffset : 0;
        InvalidateResourceSelection();
        int revision = ++resourceRevision;
        SearchPanel.IsVisible = true;
        DetailsPanel.IsVisible = false;
        SearchNotice.IsVisible = true;
        SearchNotice.Text = searchResults.Count > 0 ? "正在更新，保留上次成功结果…" : "正在搜索…";
        SearchRetry.IsVisible = false;
        try
        {
            var rows = resourceSearchPreview is { } search ? await search(token)
                : await Task.Run(() => catalog.Search(binding.Kind, binding.Query, binding.Provider, target, token, binding.Order, offset, ResourcePageSize), token);
            token.ThrowIfCancellationRequested();
            if (revision != resourceRevision || disposed || binding != ResourceContext()) return;
            searchResults = offset == 0 ? rows : searchResults.Concat(rows).DistinctBy(ProjectIdentity).ToArray();
            resourceSearchBinding = binding;
            resourceOffset = offset + ResourcePageSize;
            Projects.ItemsSource = searchResults;
            SearchNotice.Text = searchResults.Count == 0 ? "没有兼容结果，可换关键词或参考版本再试。"
                : rows.Count == 0 ? $"已显示 {searchResults.Count} 个项目，当前结果已到底。" : $"已显示 {searchResults.Count} 个项目，可尝试加载下一批。";
            LoadMoreResourcesButton.IsVisible = rows.Count > 0;
            SearchRetry.IsVisible = searchResults.Count == 0;
            SearchRetry.Content = "重新搜索";
            Status.Text = "";
        }
        catch (Exception error)
        {
            if (revision != resourceRevision || disposed || binding != ResourceContext())
            {
                System.Diagnostics.Trace.TraceWarning("Discarded stale mobile catalog search: " + error.GetType().Name);
                return;
            }
            SearchNotice.Text = token.IsCancellationRequested ? "搜索已取消；上次结果仍保留。" : "更新失败；上次结果仍保留，可检查网络或切换来源后重试。";
            SearchRetry.Content = "重试搜索";
            SearchRetry.IsVisible = true;
            throw;
        }
    }, allowNavigation: true);

    private void ProjectSelected(object? sender, SelectionChangedEventArgs e) =>
        DetailsButton.IsEnabled = SearchBindingCurrent && Projects.SelectedItem is CatalogProject;
    private async void OpenDetails(object? sender, RoutedEventArgs e)
    {
        if (Projects.SelectedItem is not CatalogProject entry) return;
        if (!CanOpenProject(entry)) return;
        PresentProject(entry);
        await LoadReleases();
    }
    private void PresentProject(CatalogProject entry)
    {
        ++resourceRevision;
        resourceProjectBinding = ResourceContext();
        resourceListOffset = ResourcesPage.Offset;
        ResourcesPage.Offset = Avalonia.Vector.Zero;
        project = entry; original = false;
        DownloadNotice.Text = "";
        ReleaseInfo.Text = "";
        Releases.ItemsSource = Array.Empty<CatalogRelease>();
        DownloadButton.IsEnabled = false;
        ProjectIcon.Source = entry.Icon;
        ProjectTitle.Text = entry.Title;
        ProjectAuthor.Text = entry.Byline;
        SearchPanel.IsVisible = false;
        DetailsPanel.IsVisible = true;
        Description();
    }
    private bool CanOpenProject(CatalogProject entry)
    {
        if (!SearchBindingCurrent || !searchResults.Any(row => ProjectIdentity(row) == ProjectIdentity(entry)))
        {
            Status.Text = "搜索条件或目标实例已修改，请重新搜索后获取文件。";
            return false;
        }
        if (operation is null) return true;
        Status.Text = "另一个任务正在执行，可在通知铃铛查看进度或取消后再试。";
        return false;
    }
    private void Description()
    {
        if (project == null) return;
        ProjectDescription.Text = original ? project.OriginalSummary : project.PreviewSummary;
        if (string.IsNullOrWhiteSpace(ProjectDescription.Text)) ProjectDescription.Text = "上游暂无简介。";
        OriginalButton.IsVisible = project.Summary.Length > 0 && project.Summary != project.OriginalSummary;
        OriginalButton.Content = original ? "查看中文简介" : "查看原文";
    }
    private void ToggleOriginal(object? sender, RoutedEventArgs e) { original = !original; Description(); }
    private void BackToSearch(object? sender, RoutedEventArgs e)
    {
        SearchPanel.IsVisible = true; DetailsPanel.IsVisible = false;
        ++resourceRevision;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => ResourcesPage.Offset = resourceListOffset);
    }
    private async void RefreshReleases(object? sender, RoutedEventArgs e) => await LoadReleases();
    private Task LoadReleases() => Run(async token =>
    {
        RequireSelection();
        if (!ProjectBindingCurrent) { DownloadButton.IsEnabled = false; return; }
        var entry = project!;
        var binding = resourceProjectBinding!;
        var target = catalog.Target(binding.Instance, binding.Version);
        int revision = ++resourceRevision;
        Releases.ItemsSource = Array.Empty<CatalogRelease>();
        DownloadButton.IsEnabled = false;
        ReleaseInfo.Text = "正在获取兼容版本…";
        Worlds.IsVisible = entry.Kind == "datapack";
        Worlds.ItemsSource = Worlds.IsVisible ? catalog.WorldsForTarget(binding.Instance, binding.Version) : Array.Empty<string>();
        Worlds.SelectedIndex = -1;
        if (entry.Kind == "mod" && CatalogLoader.Normalize(binding.Loader).Length == 0)
        {
            ReleaseInfo.Text = "请在实例页选择 Fabric、Forge 等参考加载器，再搜索兼容模组。参考设置不会安装游戏内核。";
            return;
        }
        try
        {
            var releases = resourceReleasesPreview is { } read ? await read(token)
                : await Task.Run(() => catalog.Releases(entry, target, token), token);
            token.ThrowIfCancellationRequested();
            if (disposed || revision != resourceRevision || binding != ResourceContext()
                || project is null || ProjectIdentity(project) != ProjectIdentity(entry)) return;
            Releases.ItemsSource = releases;
            if (releases.Count > 0) Releases.SelectedIndex = 0;
            else ReleaseInfo.Text = "没有兼容文件，请调整实例参考版本。";
        }
        catch (Exception error)
        {
            if (disposed || revision != resourceRevision || binding != ResourceContext())
            {
                System.Diagnostics.Trace.TraceWarning("Discarded stale mobile catalog releases: " + error.GetType().Name);
                return;
            }
            ReleaseInfo.Text = "版本获取失败，可点击刷新版本重试。";
            throw;
        }
    }, allowNavigation: true);
    private void ReleaseSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (!ProjectBindingCurrent || Releases.SelectedItem is not CatalogRelease release) { DownloadButton.IsEnabled = false; return; }
        ReleaseInfo.Text = "已选择兼容文件（按日期推荐，可手动更换）\n" + release.Title + " · " + release.Date + "\n" + release.Compatibility + "\n" + release.Filename;
        DownloadNotice.Text = project!.Kind switch
        {
            "modpack" => "仅保存整合包原始文件，当前 APK 不安装加载器或运行游戏。",
            "mod" => $"仅保存模组文件，{release.RequiredDependencies} 项必需依赖需另行下载；Android 游戏运行尚未接入。",
            "shader" => "保存光影文件，之后需在支持光影的游戏运行环境启用。",
            "datapack" => "请选择已有存档，数据包将安装到该存档。",
            "world" => "导入世界到实例存档目录，同名世界自动保留副本。",
            _ => "将下载并校验到此实例的 resourcepacks 目录。"
        };
        DownloadButton.IsEnabled = selected.Length > 0 && (project.Kind != "datapack" || Worlds.SelectedItem is string);
        if (project.Kind == "datapack" && Worlds.ItemCount == 0) DownloadNotice.Text = "此实例暂无存档，请先下载世界后再安装数据包。";
    }
    private async void DownloadResource(object? sender, RoutedEventArgs e) => await Run(async token =>
    {
        RequireSelection();
        if (!ProjectBindingCurrent) throw new InvalidOperationException("目标实例或搜索条件已修改，请重新获取兼容文件。");
        if (project == null || Releases.SelectedItem is not CatalogRelease release) throw new InvalidOperationException("请选择文件版本。");
        string name = selected, version = reference, world = Worlds.SelectedItem as string ?? "";
        var entry = project;
        var binding = resourceProjectBinding;
        int revision = resourceRevision;
        var owner = operation;
        bool DetailCurrent() => !disposed && revision == resourceRevision && binding == ResourceContext()
            && project is not null && ProjectIdentity(project) == ProjectIdentity(entry);
        var progress = new Progress<InstallProgress>(update =>
        {
            if (disposed || !ReferenceEquals(operation, owner)) return;
            Status.Text = $"{entry.Title} · {release.Filename}\n{update.Message}"
                + (update.Total > 0 ? $"\n总体进度 {update.Completed:N0} / {update.Total:N0}" : "");
            if (DetailCurrent()) DownloadNotice.Text = Status.Text;
            if (currentPage == "tasks") PresentLocalTasks();
        });
        string message = await Task.Run(() => catalog.Download(entry, release, name, version, progress, token, world), token);
        if (disposed) return;
        Status.Text = message;
        if (DetailCurrent()) DownloadNotice.Text = message;
    }, allowNavigation: true);
}
