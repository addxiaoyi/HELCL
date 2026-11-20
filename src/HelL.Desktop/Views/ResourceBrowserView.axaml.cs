using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using HelL.Core.Launcher;
using HelL.Portable;

namespace HelL.Desktop.Views;

public partial class ResourceBrowserView : UserControl
{
    private readonly PortableCatalog catalog = new();
    private MainWindow? host;
    private CatalogProject? project;
    private string instance = "";
    private bool settingTarget;
    private bool original;
    private int pageIndex;
    private int displayedPage;
    private int? lastPageIndex;
    internal const int PageSize = 30;
    internal const int MaximumPageIndex = PortableCatalog.MaximumSearchOffset / PageSize;
    private bool hasSearched;
    private int generation;
    private int projectGeneration;
    private string projectTarget = "";
    private bool resultsCurrent;
    private string BindingKey => instance + "\n" + Version + "\n" + Kind + "\n" + Source + "\n" + Order + "\n" + (QueryBox.Text ?? "");
    private string? documentSelection;
    private int documentGeneration;
    private bool documentsPending;
    private bool documentsQueued;
    private IReadOnlyList<CatalogProject> lastResults = [];
    internal Func<int, CancellationToken, Task<IReadOnlyList<CatalogProject>>>? SearchFixture { get; set; }
    internal Func<CancellationToken, Task<IReadOnlyList<CatalogRelease>>>? ReleasesFixture { get; set; }
    internal Func<CancellationToken, Task<CatalogProjectDetails>>? DetailsFixture { get; set; }
    private string Kind => (KindPicker.SelectedItem as ComboBoxItem)?.Tag as string ?? "resourcepack";
    private string Source => (SourcePicker.SelectedItem as ComboBoxItem)?.Tag as string ?? "modrinth";
    private string Order => (OrderPicker.SelectedItem as ComboBoxItem)?.Tag as string ?? "relevance";
    private string Version => VersionPicker.SelectedItem as string ?? "";

    public ResourceBrowserView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Reflow();
        QueryBox.PropertyChanged += (_, change) =>
        {
            if (change.Property != TextBox.TextProperty) return;
            generation++; resultsCurrent = false; DetailsButton.IsEnabled = false; DownloadButton.IsEnabled = false;
            MarkResultsStale();
            ResultNotice.Text = hasSearched
                ? host?.IsWorking == true
                    ? $"搜索条件已改变，仍显示上一条件的第 {displayedPage + 1} 页；当前请求结束后请重新搜索。"
                    : $"搜索条件已改变，仍显示上一条件的第 {displayedPage + 1} 页；点击搜索后更新。"
                : "搜索条件已改变，点击搜索后显示匹配的资源。";
        };
    }
    private void Reflow()
    {
        bool narrow = Bounds.Width < 760;
        DiscoveryGrid.Columns = narrow ? 2 : 4;
        foreach (Control control in new Control[] { SourcePicker, OrderPicker }) Grid.SetRow(control, narrow ? 1 : 0);
        Grid.SetColumn(SourcePicker, narrow ? 0 : 1);
        Grid.SetColumn(OrderPicker, narrow ? 1 : 2);
        Grid.SetColumnSpan(QueryBox, narrow ? 3 : 1);
    }
    internal void Initialize(MainWindow window) => host = window;

    internal void SetTarget(InstanceEntry? selected)
    {
        settingTarget = true;
        try
        {
            string prior = Version;
            bool changed = instance != selected?.Name;
            instance = selected?.Name ?? "";
            TargetLabel.Text = selected?.Name ?? "请从左侧选择实例";
            VersionPicker.ItemsSource = selected?.Versions ?? [];
            VersionPicker.SelectedItem = changed ? null : prior;
            if (VersionPicker.SelectedIndex < 0 && selected?.Versions.Length > 0) VersionPicker.SelectedIndex = 0;
        }
        finally { settingTarget = false; }
        InvalidateResults("目标已更新，请搜索匹配此实例的资源。");
    }

    private CatalogTarget? Target() => Version.Length == 0 || instance.Length == 0 ? null : catalog.Target(instance, Version);
    private void TargetChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!settingTarget && host != null) InvalidateResults("游戏版本已更新，请重新搜索匹配资源。");
    }
    private void KindChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (host == null) return;
        SyncCategoryButtons();
        if (Kind == "world") SourcePicker.SelectedIndex = 1;
        InvalidateResults("类型已更新，请重新搜索。");
    }

    internal void SelectCategory(string kind)
    {
        var match = KindPicker.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string?)i.Tag == kind);
        if (match != null) KindPicker.SelectedItem = match;
        SyncCategoryButtons();
    }

    private void PickCategoryChip(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string kind }) return;
        SelectCategory(kind);
    }

    private void SyncCategoryButtons()
    {
        if (CategoryGrid == null) return;
        string active = Kind;
        foreach (var btn in CategoryGrid.Children.OfType<Button>())
            btn.Classes.Set("current", (string?)btn.Tag == active);
    }

    private async void OpenRowProject(object? sender, RoutedEventArgs e)
    {
        if (host == null || sender is not Button { DataContext: CatalogProject row }) return;
        if (!resultsCurrent) { ResultNotice.Text = "这些结果来自旧条件，请重新搜索后选择。"; return; }
        Results.SelectedItem = row;
        PresentProject(row);
        await LoadReleases();
    }
    private void SourceChanged(object? sender, SelectionChangedEventArgs e)
    { if (host != null) InvalidateResults("来源已更新，请重新搜索。"); }
    private void OrderChanged(object? sender, SelectionChangedEventArgs e)
    { if (host != null) InvalidateResults("排序已更新，请重新搜索。"); }

    private async void QuickPresetSearch(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string spec }) return;
        string[] parts = spec.Split('|', 2);
        SelectCategory(parts[0]);
        QueryBox.Text = parts.Length > 1 ? parts[1] : "";
        await Search();
    }

    private void InvalidateResults(string notice)
    {
        generation++; resultsCurrent = false;
        ResetDocuments();
        project = null;
        MarkResultsStale();
        if (!hasSearched) Results.ItemsSource = Array.Empty<CatalogProject>();
        ReleasePicker.ItemsSource = Array.Empty<CatalogRelease>();
        ResultNotice.Text = notice;
        BrowsePanel.IsVisible = true;
        DiscoveryPanel.IsVisible = lastResults.Count == 0;
        SearchControls.IsVisible = true;
        DetailPanel.IsVisible = false;
        DetailsButton.IsEnabled = false;
        DownloadButton.IsEnabled = false;
        if (hasSearched && host is { IsWorking: false }) Avalonia.Threading.Dispatcher.UIThread.Post(async () => await Search());
    }

    private async void SearchResources(object? sender, RoutedEventArgs e) => await Search();
    private async void PreviousPage(object? sender, RoutedEventArgs e) { if (!resultsCurrent || pageIndex == 0 || host?.IsWorking == true) return; pageIndex--; await Search(true); }
    private async void NextPage(object? sender, RoutedEventArgs e) { if (!resultsCurrent || pageIndex >= MaximumPageIndex || host?.IsWorking == true) return; pageIndex++; await Search(true); }
    private async void SearchKey(object? sender, KeyEventArgs e)
    { if (e.Key == Key.Enter) { e.Handled = true; await Search(); } }

    internal async Task Search(bool paging = false)
    {
        if (host == null) return;
        await host.RunTask(async token =>
        {
            string kind = Kind, source = Source, query = QueryBox.Text?.Trim() ?? "", order = Order;
            int revision = ++generation;
            string binding = BindingKey;
            if (kind == "world" && source == "modrinth") throw new InvalidOperationException("世界资源请切换到 CurseForge。");
            var target = Target();
            if (!paging)
            {
                pageIndex = 0;
                lastPageIndex = null;
            }
            ResultNotice.Text = "正在搜索，保留上次结果，可从底部取消。";
            DetailPanel.IsVisible = false; BrowsePanel.IsVisible = SearchControls.IsVisible = true;
            DiscoveryPanel.IsVisible = false;
            try
            {
                int offset = pageIndex * PageSize;
                var rows = SearchFixture is { } fixture ? await fixture(offset, token)
                    : await Task.Run(() => catalog.Search(kind, query, source, target, token, order, offset, PageSize), token);
                if (revision != generation || BindingKey != binding) { ResultNotice.Text = "搜索条件已改变，已丢弃旧结果，请重新搜索。"; return; }
                if (paging && rows.Count == 0 && lastResults.Count > 0)
                {
                    lastPageIndex = displayedPage;
                    pageIndex = displayedPage;
                    resultsCurrent = true;
                    PageLabel.Text = $"第 {displayedPage + 1} 页 · 每页最多 {PageSize} 项";
                    NextPageButton.IsEnabled = false;
                    DiscoveryPanel.IsVisible = false;
                    ResultNotice.Text = $"已到末页，保留第 {displayedPage + 1} 页的 {lastResults.Count} 个结果。";
                    host.SetStatus("资源列表已到末页。");
                    return;
                }
                resultsCurrent = true;
                hasSearched = true;
                lastResults = rows;
                displayedPage = pageIndex;
                Results.ItemsSource = rows;
                PageLabel.Text = $"第 {pageIndex + 1} 页 · 每页最多 {PageSize} 项";
                PreviousPageButton.IsEnabled = pageIndex > 0;
                // Aliases and source deduplication can shorten a page without exhausting the upstream feed.
                NextPageButton.IsEnabled = rows.Count > 0 && pageIndex < MaximumPageIndex
                    && (!lastPageIndex.HasValue || pageIndex < lastPageIndex.Value);
                DiscoveryPanel.IsVisible = rows.Count == 0;
                ResultNotice.Text = lastPageIndex == pageIndex
                    ? $"已到末页，当前第 {pageIndex + 1} 页有 {rows.Count} 个结果。"
                    : rows.Count == 0 ? "没有匹配结果。可换关键词、来源或目标版本后重试。"
                    : pageIndex == MaximumPageIndex
                        ? $"已达到可翻页范围上限，最多可浏览前 {MaximumPageIndex * PageSize + PageSize:N0} 项。"
                        : $"{rows.Count} 个项目 · 选择项目查看兼容版本";
                host.SetStatus("资源搜索完成。");
            }
            catch
            {
                if (revision != generation || BindingKey != binding) return;
                pageIndex = displayedPage;
                ResultNotice.Text = token.IsCancellationRequested ? "搜索已取消，可以重新搜索。" : "搜索未完成，请检查网络或切换来源，再点搜索重试。";
                throw;
            }
        });
    }

    private void MarkResultsStale()
    {
        pageIndex = displayedPage;
        lastPageIndex = null;
        PreviousPageButton.IsEnabled = false;
        NextPageButton.IsEnabled = false;
        PageLabel.Text = hasSearched
            ? $"上次结果：第 {displayedPage + 1} 页 · 每页最多 {PageSize} 项"
            : $"第 1 页 · 每页最多 {PageSize} 项";
    }

    private void SelectProject(object? sender, SelectionChangedEventArgs e) => DetailsButton.IsEnabled = resultsCurrent && Results.SelectedItem is CatalogProject;
    private async void OpenProject(object? sender, RoutedEventArgs e)
    {
        if (Results.SelectedItem is not CatalogProject selected || host == null || !resultsCurrent) return;
        PresentProject(selected);
        await LoadReleases();
    }

    internal void PresentProject(CatalogProject selected)
    {
        project = selected;
        projectGeneration = generation; projectTarget = BindingKey;
        ResetDocuments();
        original = false;
        DetailPicture.Source = selected.Icon;
        ProjectTitle.Text = selected.Title;
        ProjectAuthor.Text = selected.Byline;
        ShowSummary();
        BrowsePanel.IsVisible = false;
        SearchControls.IsVisible = false;
        DetailPanel.IsVisible = true;
    }

    private async void RefreshReleases(object? sender, RoutedEventArgs e) => await LoadReleases();
    internal async Task LoadReleases()
    {
        if (project == null || host == null) return;
        await host.RunTask(async token =>
        {
            var selected = project;
            int revision = projectGeneration;
            string binding = projectTarget;
            if (revision != generation || binding != BindingKey) { InstallNotice.Text = "目标或筛选已改变，请重新搜索并选择项目。"; return; }
            var target = Target();
            ReleasePicker.ItemsSource = Array.Empty<CatalogRelease>();
            WorldPicker.IsVisible = selected.Kind == "datapack";
            if (WorldPicker.IsVisible)
            {
                WorldPicker.ItemsSource = target == null ? Array.Empty<string>() : catalog.WorldsForTarget(instance, Version);
                WorldPicker.SelectedIndex = -1;
            }
            DownloadButton.IsEnabled = false;
            ReleaseNotice.Text = "正在读取上游版本信息…";
            if (selected.Kind == "mod" && target != null && CatalogLoader.Normalize(target.Loader).Length == 0)
            {
                ReleaseNotice.Text = "此实例为原版游戏。请返回启动页，选择并安装兼容的模组加载器后再浏览模组。";
                return;
            }
            try
            {
                var releases = ReleasesFixture is { } fixture ? await fixture(token)
                    : await Task.Run(() => catalog.Releases(selected, target, token), token);
                if (project != selected || revision != generation || BindingKey != binding) return;
                ReleasePicker.ItemsSource = releases;
                if (releases.Count > 0) ReleasePicker.SelectedIndex = 0;
                else ReleaseNotice.Text = "没有与当前目标兼容的文件，请更换目标版本后重新搜索。";
                await LoadDocuments(selected, ReleasePicker.SelectedItem as CatalogRelease, token);
            }
            catch
            {
                if (project != selected || revision != generation || BindingKey != binding) return;
                ReleaseNotice.Text = token.IsCancellationRequested ? "版本读取已取消。" : "版本读取失败，可点击“刷新版本”重试。";
                throw;
            }
        });
    }

    private async Task LoadDocuments(CatalogProject selected, CatalogRelease? release, CancellationToken cancellation)
    {
        int revision = projectGeneration;
        string binding = projectTarget;
        int request = ++documentGeneration;
        string releaseId = release?.Id ?? "";
        bool IsCurrent() => project == selected && revision == generation && BindingKey == binding
            && request == documentGeneration && ((ReleasePicker.SelectedItem as CatalogRelease)?.Id ?? "") == releaseId;
        if (!IsCurrent()) return;
        documentsPending = false;
        try
        {
            var details = DetailsFixture is { } fixture ? await fixture(cancellation)
                : await Task.Run(() => catalog.Details(selected, release, cancellation), cancellation);
            if (!IsCurrent()) return;
            ProjectDocuments.Children.Clear();
            if (details.Gallery.Length > 0)
            {
                var gallery = new WrapPanel();
                foreach (string image in details.Gallery.Take(12)) gallery.Children.Add(new HelL.Desktop.Controls.ProjectPicture { Source = image, Width = 180, Height = 110, Margin = new Avalonia.Thickness(0,0,8,8) });
                ProjectDocuments.Children.Add(new Expander { Header = "项目图库", Content = gallery });
            }
            ProjectDocuments.Children.Add(new Expander { Header = "完整介绍", Content = new ScrollViewer { MaxHeight = 400,
                Content = new HelL.Desktop.Controls.MessageDocument { Text = details.Body.Length == 0 ? "上游未提供完整介绍。" : details.Body } } });
            ProjectDocuments.Children.Add(new Expander { Header = "所选文件更新日志", Content = new ScrollViewer { MaxHeight = 300,
                Content = new HelL.Desktop.Controls.MessageDocument { Text = details.Changelog.Length == 0 ? "上游未提供此文件的更新日志。" : details.Changelog } } });
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (IsCurrent()) ShowRetry("详情读取已取消，重新读取");
            throw;
        }
        catch (Exception error)
        {
            if (!IsCurrent()) return;
            ShowRetry("详情读取失败，重新读取");
            System.Diagnostics.Trace.TraceWarning("Catalog details: " + error.GetType().Name);
        }

        void ShowRetry(string label)
        {
            ProjectDocuments.Children.Clear();
            var retry = new Button { Content = label, Classes = { "soft" } };
            retry.Click += (_, _) => { documentsPending = true; ResumeDocuments(); };
            ProjectDocuments.Children.Add(retry);
        }
    }

    private void ResetDocuments()
    {
        documentGeneration++;
        documentSelection = null;
        documentsPending = false;
        ProjectDocuments.Children.Clear();
    }

    internal void ResumeDocuments()
    {
        if (documentsQueued || !CanLoadDocuments()) return;
        documentsQueued = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
        {
            documentsQueued = false;
            if (!CanLoadDocuments()) return;
            var selected = project!;
            var release = ReleasePicker.SelectedItem as CatalogRelease;
            await host!.RunTask(token => LoadDocuments(selected, release, token), "LoadReleases");
        });
    }

    private bool CanLoadDocuments() => documentsPending && host is { IsWorking: false } && project != null
        && projectGeneration == generation && projectTarget == BindingKey && DetailPanel.IsVisible;

    private void ReleaseChanged(object? sender, SelectionChangedEventArgs e)
    {
        string selectedId = (ReleasePicker.SelectedItem as CatalogRelease)?.Id ?? "";
        if (documentSelection != selectedId)
        {
            documentSelection = selectedId;
            documentGeneration++;
            documentsPending = project != null && ReleasePicker.SelectedItem is CatalogRelease;
            ProjectDocuments.Children.Clear();
            if (documentsPending) ProjectDocuments.Children.Add(new TextBlock { Text = "正在读取所选文件的详情…" });
        }
        if (ReleasePicker.SelectedItem is not CatalogRelease release || project == null) { DownloadButton.IsEnabled = false; return; }
        ReleaseNotice.Text = release.Compatibility + "\n" + release.Filename;
        InstallNotice.Text = project.Kind switch
        {
            "mod" => $"{release.RequiredDependencies} 项必需依赖需另行安装。模组文件写入所选版本目录；加载器启动仍需完整版。",
            "modpack" => "先下载原始整合包文件到实例 downloads 目录，整合包安装与启动仍待迁移。",
            "datapack" => "请选择已有存档，数据包将直接写入该存档的 datapacks 文件夹。",
            "shader" => "光影文件下载后，还需要在游戏中安装兼容的光影加载器并启用。",
            "world" => "下载并校验后导入存档；不会覆盖同名世界。",
            _ => "将写入所选版本的 resourcepacks 目录，在游戏中手动启用。"
        };
        DownloadButton.Content = project.Kind is "modpack" or "datapack" ? "下载到此实例" : "安装到此实例";
        DownloadButton.IsEnabled = projectGeneration == generation && projectTarget == BindingKey && instance.Length > 0 && (Version.Length > 0 || project.Kind == "modpack");
        if (!DownloadButton.IsEnabled) InstallNotice.Text = "请先从左侧选择实例并安装 Minecraft，再选择安装目标。";
        if (project.Kind == "datapack" && WorldPicker.SelectedItem is not string)
        {
            DownloadButton.IsEnabled = false;
            InstallNotice.Text = "请选择目标存档；若列表为空，请先在此实例中创建或导入世界。";
        }
        ResumeDocuments();
    }

    private async void DownloadResource(object? sender, RoutedEventArgs e)
        => await DownloadSelected();

    internal async Task DownloadSelected()
    {
        if (host == null || project == null || ReleasePicker.SelectedItem is not CatalogRelease release) return;
        if (projectGeneration != generation || projectTarget != BindingKey) { DownloadButton.IsEnabled = false; InstallNotice.Text = "安装目标或条件已改变，请重新搜索并选择文件。"; return; }
        var selected = project;
        string name = instance, version = Version;
        string world = WorldPicker.SelectedItem as string ?? "";
        await host.RunTask(async token =>
        {
            var progress = host.TaskProgress();
            string message = await Task.Run(() => catalog.Download(selected, release, name, version, progress, token, world), token);
            host.SetStatus(message);
            InstallNotice.Text = message;
        });
    }

    private void BackToResults(object? sender, RoutedEventArgs e) { BrowsePanel.IsVisible = true; DetailPanel.IsVisible = false; SearchControls.IsVisible = true; }
    private void ToggleOriginal(object? sender, RoutedEventArgs e) { original = !original; ShowSummary(); }
    private void ShowSummary()
    {
        if (project == null) return;
        bool hasChinese = project.Summary.Length > 0;
        ProjectSummary.Text = original || !hasChinese ? project.OriginalSummary : project.Summary;
        if (string.IsNullOrWhiteSpace(ProjectSummary.Text)) ProjectSummary.Text = "上游暂未提供项目简介。";
        OriginalButton.IsVisible = hasChinese && project.OriginalSummary.Length > 0 && project.Summary != project.OriginalSummary;
        OriginalButton.Content = original ? "查看中文简介" : "查看原文";
    }

    private async void OpenWebsite(object? sender, RoutedEventArgs e)
    {
        if (project == null || host == null) return;
        var selected = project;
        string category = selected.Kind switch { "mod" => "mc-mods", "resourcepack" => "texture-packs", "shader" => "shaders", "modpack" => "modpacks", "datapack" => "data-packs", _ => "worlds" };
        string url = selected.Source == "modrinth" ? $"https://modrinth.com/{selected.Kind}/{Uri.EscapeDataString(selected.Slug)}"
            : $"https://www.curseforge.com/minecraft/{category}/{Uri.EscapeDataString(selected.Slug)}";
        await host.RunTask(async _ => { if (!await host.Launcher.LaunchUriAsync(new Uri(url))) throw new IOException("系统浏览器未能打开项目主页。"); });
    }
}
