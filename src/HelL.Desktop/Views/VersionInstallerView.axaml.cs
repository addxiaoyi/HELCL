using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using HelL.Core;
using HelL.Core.Launcher;
using HelL.Portable;

namespace HelL.Desktop.Views;

public partial class VersionInstallerView : UserControl
{
    public sealed record RemoteVersionRow(string Id, string Type, string ReleaseDate)
    {
        public string TypeLabel => Type switch
        {
            "release" => "正式版",
            "snapshot" => "快照",
            "old_beta" => "Beta",
            "old_alpha" => "Alpha",
            _ => Type
        };
    }

    private MainWindow? host;
    private static readonly RemoteVersionRow[] DefaultPresets =
    [
        new("1.21.4", "release", "2024-12-03"),
        new("1.21.1", "release", "2024-08-08"),
        new("1.20.4", "release", "2023-12-07"),
        new("1.20.1", "release", "2023-06-12"),
        new("1.19.4", "release", "2023-03-14"),
        new("1.18.2", "release", "2022-02-28"),
        new("1.16.5", "release", "2021-01-15"),
        new("1.12.2", "release", "2017-09-18")
    ];
    private IReadOnlyList<RemoteVersionRow> allVersions = DefaultPresets;
    private bool fetchedRemote;
    private bool preparing;
    private string activeChannel = "release";

    public VersionInstallerView()
    {
        InitializeComponent();
        ApplyFilter();
    }

    internal void Initialize(MainWindow window)
    {
        host = window;
        RefreshInstances();
    }

    internal void RefreshInstances(string? preferred = null)
    {
        if (host == null) return;
        var entries = host.GameLauncher.InstancesList();
        string? target = preferred ?? (InstancePicker.SelectedItem as InstanceEntry)?.Name ?? host.GameLauncher.SelectedInstance;
        InstancePicker.ItemsSource = entries;
        InstancePicker.SelectedItem = entries.FirstOrDefault(item => item.Name == target) ?? entries.FirstOrDefault();
        UpdateInstallState();
    }

    private string SelectedLoader => (LoaderCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "none";

    private async void FetchManifest(object? sender, RoutedEventArgs e)
    {
        if (host == null) return;
        await host.RunTask(async token =>
        {
            host.SetStatus("正在获取官方版本清单…");
            var rows = await Task.Run(() =>
            {
                var dm = new DownloadManager(cancel: () => token.IsCancellationRequested);
                var dict = Manifest.ListRemoteVersions(dm);
                return dict.Values.OfType<PyDict>()
                    .OrderByDescending(row => Py.S(row.Get("releaseTime")), StringComparer.Ordinal)
                    .Select(row =>
                    {
                        string rawTime = Py.S(row.Get("releaseTime"));
                        string date = rawTime.Length >= 10 ? rawTime[..10] : rawTime;
                        return new RemoteVersionRow(Py.S(row.Get("id")), Py.S(row.Get("type")), date);
                    })
                    .Where(row => row.Id.Length > 0)
                    .ToArray();
            }, token);
            token.ThrowIfCancellationRequested();
            allVersions = rows;
            fetchedRemote = true;
            ApplyFilter();
            host.SetStatus($"已加载 {rows.Length} 个版本。");
        }, "RefreshVersions");
    }

    private void SelectChannel(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string channel }) return;
        activeChannel = channel;
        foreach (var btn in ChannelTabs.Children.OfType<Button>())
            btn.Classes.Set("current", (string?)btn.Tag == channel);
        ApplyFilter();
    }

    private void FilterVersions(object? sender, TextChangedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        string query = SearchBox.Text?.Trim() ?? "";
        var filtered = allVersions.Where(row =>
            (activeChannel == "all" ||
             (activeChannel == "release" && row.Type == "release") ||
             (activeChannel == "snapshot" && row.Type == "snapshot") ||
             (activeChannel == "legacy" && row.Type is "old_beta" or "old_alpha")) &&
            (query.Length == 0 || row.Id.Contains(query, StringComparison.OrdinalIgnoreCase))).ToArray();
        VersionList.ItemsSource = filtered;
        if (VersionList.SelectedItem == null && filtered.Length > 0) VersionList.SelectedIndex = 0;
        VersionCountLabel.Text = !fetchedRemote ? $"常用 {filtered.Length} 版" : $"{filtered.Length} 个版本";
        EmptyState.IsVisible = filtered.Length == 0;
        EmptyText.Text = !fetchedRemote && activeChannel != "release"
            ? "点击右上角「刷新清单」获取快照与远古版本"
            : "暂无匹配版本";
        UpdateInstallState();
    }

    private void ChooseVersion(object? sender, SelectionChangedEventArgs e)
    {
        LoaderBuildCombo.ItemsSource = Array.Empty<LoaderVersion>();
        if (VersionList.SelectedItem is RemoteVersionRow row)
        {
            SelectedVersionLabel.Text = $"Minecraft {row.Id}";
            SelectedVersionHint.Text = $"{row.TypeLabel} · {row.ReleaseDate}";
            if (SelectedLoader != "none") FetchLoaderBuilds(this, new RoutedEventArgs());
        }
        UpdateInstallState();
    }

    private void TargetInstanceChanged(object? sender, SelectionChangedEventArgs e) => UpdateInstallState();

    private void LoaderKindChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (LoaderBuildSection == null || LoaderBuildCombo == null) return;
        bool hasLoader = SelectedLoader != "none";
        LoaderBuildSection.IsVisible = hasLoader;
        LoaderBuildCombo.ItemsSource = Array.Empty<LoaderVersion>();
        if (hasLoader && VersionList.SelectedItem is RemoteVersionRow)
            FetchLoaderBuilds(this, new RoutedEventArgs());
        UpdateInstallState();
    }

    private void LoaderBuildChanged(object? sender, SelectionChangedEventArgs e) => UpdateInstallState();

    private async void FetchLoaderBuilds(object? sender, RoutedEventArgs e)
    {
        if (host == null || VersionList.SelectedItem is not RemoteVersionRow row || SelectedLoader == "none") return;
        string loader = SelectedLoader;
        await host.RunTask(async token =>
        {
            host.SetStatus($"正在查询 {row.Id} 的 {loader} 构建…");
            var builds = await Task.Run(() => host.GameLauncher.LoaderVersions(row.Id, loader, token), token);
            token.ThrowIfCancellationRequested();
            LoaderBuildCombo.ItemsSource = builds;
            LoaderBuildCombo.SelectedIndex = builds.Count > 0 ? 0 : -1;
            InstallHintText.Text = builds.Count > 0 ? $"共 {builds.Count} 个兼容构建。" : $"此版本暂无可用的 {loader} 构建。";
            host.SetStatus(InstallHintText.Text);
        }, "RefreshLoaderVersions");
        UpdateInstallState();
    }

    private void UpdateInstallState()
    {
        if (StartInstallButton == null || VersionList == null || InstancePicker == null) return;
        bool hasInstance = InstancePicker.SelectedItem is InstanceEntry;
        bool hasVersion = VersionList.SelectedItem is RemoteVersionRow;
        bool loaderReady = SelectedLoader == "none" || LoaderBuildCombo.SelectedItem is LoaderVersion;
        StartInstallButton.IsEnabled = !preparing && hasInstance && hasVersion && loaderReady;
        if (InstancePicker.SelectedItem is InstanceEntry target && VersionList.SelectedItem is RemoteVersionRow selected)
            InstallHintText.Text = BuildInstallSummary(target.Name, selected.Id, SelectedLoader);
    }

    private async void StartInstall(object? sender, RoutedEventArgs e)
    {
        if (host == null || InstancePicker.SelectedItem is not InstanceEntry instance || VersionList.SelectedItem is not RemoteVersionRow version) return;
        if (preparing) return;
        preparing = true;
        UpdateInstallState();
        try
        {
        string loader = SelectedLoader;
        string? loaderBuild = (LoaderBuildCombo.SelectedItem as LoaderVersion)?.Id;
        InstallationPlan? plan = null;
        using (var planning = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
        {
            try { plan = await Task.Run(() => InstallationPlan.ForVersion(new Instance(instance.Name), version.Id, loader, planning.Token), planning.Token); }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                System.Diagnostics.Trace.TraceWarning("Installation plan: " + error.GetType().Name);
                if (!await host.ConfirmAction("无法读取下载量", BuildInstallSummary(instance.Name, version.Id, loader) + "\n清单读取失败。取消后可重试；继续会按实际下载校验。", "明确继续")) return;
            }
        }
        if (plan?.SpaceKnownInsufficient == true) { host.SetStatus(plan.Summary); return; }
        if (!await host.ConfirmAction("确认安装计划", plan?.Summary ?? BuildInstallSummary(instance.Name, version.Id, loader), "开始安装")) return;
        await host.RunTask(async token =>
        {
            var progress = host.TaskProgress();
            string installed = await Task.Run(() => host.GameLauncher.Install(instance.Name, version.Id, loader, loaderBuild, progress, token), token);
            host.InstancesChanged(instance.Name);
            RefreshInstances(instance.Name);
            host.SetStatus($"已安装 {installed}。");
        }, "InstallVersion");
        }
        finally { preparing = false; UpdateInstallState(); }
    }

    private static string BuildInstallSummary(string instance, string version, string loader)
    {
        string path = new Instance(instance).path;
        string free;
        try { free = $"{new DriveInfo(Path.GetPathRoot(path)!).AvailableFreeSpace / 1073741824d:F1} GB"; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { free = "无法读取"; }
        bool installed = File.Exists(Path.Combine(path, "versions", version, version + ".json"));
        return $"目标实例：{instance}\n目标目录：{path}\n版本：{version} · 加载器：{loader}\n磁盘可用：{free}\n下载量与额外占用：清单解析前未知，不显示估算值。\n已有版本：{(installed ? "已存在，将校验并复用可用文件，修复缺失内容" : "新安装，已有其它版本保留")}";
    }

    private async void ImportModpack(object? sender, RoutedEventArgs e)
    {
        if (host == null) return;
        var files = await host.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择整合包文件 (.mrpack / .zip)",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Minecraft 整合包") { Patterns = ["*.mrpack", "*.zip"] }]
        });
        if (files.Count == 0) return;
        string? localPath = files[0].TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(localPath)) return;
        string targetName = (InstancePicker.SelectedItem as InstanceEntry)?.Name ?? Path.GetFileNameWithoutExtension(localPath);
        await host.RunTask(async token =>
        {
            var existing = host.GameLauncher.InstancesList().Any(i => i.Name == targetName);
            if (!existing)
            {
                string safeName = Instances.SanitizeInstanceName(targetName);
                if (string.IsNullOrWhiteSpace(safeName)) safeName = "整合包实例";
                targetName = safeName.Length > 40 ? safeName[..40] : safeName;
                if (!host.GameLauncher.InstancesList().Any(i => i.Name == targetName))
                    host.GameLauncher.CreateInstance(targetName);
            }
            var progress = host.TaskProgress();
            await Task.Run(() =>
            {
                var inst = new Instance(targetName);
                var dm = new DownloadManager(cancel: () => token.IsCancellationRequested);
                Action<string, long, long> cb = (msg, done, total) => progress.Report(new(msg, done, total));
                if (localPath.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase) || Modpack._has_mrpack_index(localPath))
                    Modpack.InstallMrpack(dm, localPath, inst, cb, () => token.IsCancellationRequested);
                else
                    Modpack.InstallCfZip(dm, localPath, inst, cb, () => token.IsCancellationRequested);
            }, token);
            host.InstancesChanged(targetName);
            RefreshInstances(targetName);
            host.SetStatus($"已导入至「{targetName}」。");
        }, "InstallVersion");
    }
}
