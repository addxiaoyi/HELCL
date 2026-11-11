using Avalonia.Controls;
using Avalonia.Interactivity;
using HelL.Core.Launcher;
using HelL.Portable;

namespace HelL.Mobile;

public sealed partial class MobileHome
{
    private void FiltersChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!ready) return;
        if (SelectedKind == "world" && Provider.SelectedIndex == 0) Provider.SelectedIndex = 1;
        ClearProjects();
    }
    private void ClearProjects()
    {
        project = null;
        Projects.ItemsSource = Array.Empty<CatalogProject>();
        SearchPanel.IsVisible = true;
        DetailsPanel.IsVisible = false;
        DetailsButton.IsEnabled = DownloadButton.IsEnabled = false;
        SearchNotice.Text = "条件已更新，可重新搜索。";
    }
    private async void SearchResources(object? sender, RoutedEventArgs e) => await Run(async token =>
    {
        RequireSelection();
        var target = catalog.Target(selected, reference);
        string kind = SelectedKind, provider = SelectedProvider, query = Query.Text?.Trim() ?? "";
        ClearProjects(); SearchNotice.Text = "正在搜索…";
        try
        {
            var rows = await Task.Run(() => catalog.Search(kind, query, provider, target, token), token);
            Projects.ItemsSource = rows;
            SearchNotice.Text = rows.Count == 0 ? "没有兼容结果，可换关键词或参考版本重试。" : $"找到 {rows.Count} 个项目";
            Status.Text = "资源搜索完成。";
        }
        catch { SearchNotice.Text = "搜索未完成，检查网络或切换来源后重试。"; throw; }
    });

    private void ProjectSelected(object? sender, SelectionChangedEventArgs e) => DetailsButton.IsEnabled = Projects.SelectedItem is CatalogProject;
    private async void OpenDetails(object? sender, RoutedEventArgs e)
    {
        if (Projects.SelectedItem is not CatalogProject entry) return;
        PresentProject(entry);
        await LoadReleases();
    }
    private void PresentProject(CatalogProject entry)
    {
        project = entry; original = false;
        ProjectIcon.Source = entry.Icon;
        ProjectTitle.Text = entry.Title;
        ProjectAuthor.Text = entry.Byline;
        SearchPanel.IsVisible = false;
        DetailsPanel.IsVisible = true;
        Description();
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
    private void BackToSearch(object? sender, RoutedEventArgs e) { SearchPanel.IsVisible = true; DetailsPanel.IsVisible = false; }
    private async void RefreshReleases(object? sender, RoutedEventArgs e) => await LoadReleases();
    private Task LoadReleases() => Run(async token =>
    {
        RequireSelection();
        if (project == null) return;
        var entry = project;
        Releases.ItemsSource = Array.Empty<CatalogRelease>();
        DownloadButton.IsEnabled = false;
        ReleaseInfo.Text = "正在获取兼容版本…";
        Worlds.IsVisible = entry.Kind == "datapack";
        Worlds.ItemsSource = Worlds.IsVisible ? catalog.WorldsForTarget(selected, reference) : Array.Empty<string>();
        Worlds.SelectedIndex = -1;
        if (entry.Kind == "mod")
        {
            ReleaseInfo.Text = "此版本尚未接入 Android 模组加载器，可查看项目信息；安装将在后续开放。";
            return;
        }
        try
        {
            var releases = await Task.Run(() => catalog.Releases(entry, catalog.Target(selected, reference), token), token);
            Releases.ItemsSource = releases;
            if (releases.Count > 0) Releases.SelectedIndex = 0;
            else ReleaseInfo.Text = "没有兼容文件，请调整实例参考版本。";
        }
        catch { ReleaseInfo.Text = "版本获取失败，可点击刷新版本重试。"; throw; }
    });
    private void ReleaseSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (project == null || Releases.SelectedItem is not CatalogRelease release) { DownloadButton.IsEnabled = false; return; }
        ReleaseInfo.Text = release.Compatibility + "\n" + release.Filename;
        DownloadNotice.Text = project.Kind switch
        {
            "modpack" => "仅保存整合包原始文件，当前 APK 不安装加载器或运行游戏。",
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
        if (project == null || Releases.SelectedItem is not CatalogRelease release) throw new InvalidOperationException("请选择文件版本。");
        string name = selected, version = reference, world = Worlds.SelectedItem as string ?? "";
        var entry = project;
        var progress = new Progress<InstallProgress>(update => { if (operation != null) Status.Text = update.Message; });
        string message = await Task.Run(() => catalog.Download(entry, release, name, version, progress, token, world), token);
        Status.Text = DownloadNotice.Text = message;
    });
}
