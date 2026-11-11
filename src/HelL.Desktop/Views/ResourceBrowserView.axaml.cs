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
    private string Kind => (KindPicker.SelectedItem as ComboBoxItem)?.Tag as string ?? "resourcepack";
    private string Source => (SourcePicker.SelectedItem as ComboBoxItem)?.Tag as string ?? "modrinth";
    private string Version => VersionPicker.SelectedItem as string ?? "";

    public ResourceBrowserView() => InitializeComponent();
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
        if (Kind == "world") SourcePicker.SelectedIndex = 1;
        InvalidateResults("类型已更新，请重新搜索。");
    }
    private void SourceChanged(object? sender, SelectionChangedEventArgs e)
    { if (host != null) InvalidateResults("来源已更新，请重新搜索。"); }

    private void InvalidateResults(string notice)
    {
        project = null;
        Results.ItemsSource = Array.Empty<CatalogProject>();
        ReleasePicker.ItemsSource = Array.Empty<CatalogRelease>();
        ResultNotice.Text = notice;
        BrowsePanel.IsVisible = true;
        SearchControls.IsVisible = true;
        DetailPanel.IsVisible = false;
        DetailsButton.IsEnabled = false;
        DownloadButton.IsEnabled = false;
    }

    private async void SearchResources(object? sender, RoutedEventArgs e) => await Search();
    private async void SearchKey(object? sender, KeyEventArgs e)
    { if (e.Key == Key.Enter) { e.Handled = true; await Search(); } }

    internal async Task Search()
    {
        if (host == null) return;
        await host.RunTask(async token =>
        {
            string kind = Kind, source = Source, query = QueryBox.Text?.Trim() ?? "";
            if (kind == "world" && source == "modrinth") throw new InvalidOperationException("世界资源请切换到 CurseForge。");
            var target = Target();
            InvalidateResults("正在搜索，可从底部取消…");
            try
            {
                var rows = await Task.Run(() => catalog.Search(kind, query, source, target, token), token);
                Results.ItemsSource = rows;
                ResultNotice.Text = rows.Count == 0 ? "没有匹配结果。可换关键词、来源或目标版本后重试。" : $"{rows.Count} 个项目 · 选择项目查看兼容版本";
                host.SetStatus("资源搜索完成。");
            }
            catch
            {
                ResultNotice.Text = token.IsCancellationRequested ? "搜索已取消，可以重新搜索。" : "搜索未完成，请检查网络或切换来源，再点搜索重试。";
                throw;
            }
        });
    }

    private void SelectProject(object? sender, SelectionChangedEventArgs e) => DetailsButton.IsEnabled = Results.SelectedItem is CatalogProject;
    private async void OpenProject(object? sender, RoutedEventArgs e)
    {
        if (Results.SelectedItem is not CatalogProject selected || host == null) return;
        PresentProject(selected);
        await LoadReleases();
    }

    internal void PresentProject(CatalogProject selected)
    {
        project = selected;
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
                ReleaseNotice.Text = "此实例是原版游戏。请先使用完整版安装模组加载器，再选择兼容的模组文件。";
                return;
            }
            try
            {
                var releases = await Task.Run(() => catalog.Releases(selected, target, token), token);
                ReleasePicker.ItemsSource = releases;
                if (releases.Count > 0) ReleasePicker.SelectedIndex = 0;
                else ReleaseNotice.Text = "没有与当前目标兼容的文件，请更换目标版本后重新搜索。";
            }
            catch
            {
                ReleaseNotice.Text = token.IsCancellationRequested ? "版本读取已取消。" : "版本读取失败，可点击“刷新版本”重试。";
                throw;
            }
        });
    }

    private void ReleaseChanged(object? sender, SelectionChangedEventArgs e)
    {
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
        DownloadButton.IsEnabled = instance.Length > 0 && (Version.Length > 0 || project.Kind == "modpack");
        if (!DownloadButton.IsEnabled) InstallNotice.Text = "请先从左侧选择实例并安装 Minecraft，再选择安装目标。";
        if (project.Kind == "datapack" && WorldPicker.SelectedItem is not string)
        {
            DownloadButton.IsEnabled = false;
            InstallNotice.Text = "请选择目标存档；若列表为空，请先在此实例中创建或导入世界。";
        }
    }

    private async void DownloadResource(object? sender, RoutedEventArgs e)
        => await DownloadSelected();

    internal async Task DownloadSelected()
    {
        if (host == null || project == null || ReleasePicker.SelectedItem is not CatalogRelease release) return;
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
