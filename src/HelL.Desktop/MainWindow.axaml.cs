using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using HelL.Design;
using HelL.Core;
using HelL.Core.Launcher;
using HelL.Portable;

namespace HelL.Desktop;

public sealed partial class MainWindow : Window
{
    private readonly PortableLauncher launcher;
    private CancellationTokenSource? task;
    private bool playing;
    private bool closing;
    private Models.DesktopTask? activeTask;
    private string? pendingQuickServer;
    private string? SelectedName => (InstanceList.SelectedItem as InstanceEntry)?.Name;

    public MainWindow() : this(Program.DataRoot) { }

    public MainWindow(string root)
    {
        InitializeComponent();
        DataDirectory.Text = root;
        launcher = new PortableLauncher(root);
        SettingsView.Initialize(this, root);
        InitializeAppearance();
        SizeChanged += (_, _) => ApplyFrontendLayout();
        TasksView.Initialize(() => CancelTask(this, new RoutedEventArgs()), this);
        Status.PropertyChanged += (_, change) =>
        {
            if (change.Property == TextBlock.TextProperty) activeTask?.Report(Status.Text ?? string.Empty);
        };
        ResourcesView.Initialize(this);
        LibraryView.Initialize(this);
        InstallerView.Initialize(this);
        ModsView.Initialize(this);
        SavesView.Initialize(this);
        ServersView.Initialize(this);
        AiView.Initialize(this);
        JavaView.Initialize(this);
        PropertyChanged += (_, change) =>
        {
            if (change.Property == ActualTransparencyLevelProperty || change.Property == ActualThemeVariantProperty)
                ApplyGlassBackdrop();
            if (change.Property == WindowStateProperty && MaxIcon != null)
            {
                ResizeHandles.IsVisible = WindowState == WindowState.Normal;
                MaxIcon.Kind = WindowState == WindowState.Maximized
                    ? "WindowRestore"
                    : "WindowMaximize";
                string action = WindowState == WindowState.Maximized ? "还原" : "最大化";
                Avalonia.Automation.AutomationProperties.SetName(MaximizeButton, action);
                ToolTip.SetTip(MaximizeButton, action);
            }
        };
        ReloadInstances(launcher.SelectedInstance);
        ShowPage("launch");
        Closing += OnClosing;
        Opened += RestoreAccount;
        Opened += (_, _) => ApplyGlassBackdrop();
        if (Program.UiSmoke) Opened += RunUiSmoke;
        if (Program.CatalogSmoke) Opened += RunCatalogSmoke;
        if (Program.UserJourneyCheck) Opened += RunUserJourneyCheck;
    }

    private void BeginTitleBarDrag(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void BeginWindowResize(object? sender, PointerPressedEventArgs e)
    {
        if (!CanResize || WindowState != WindowState.Normal || sender is not Border { Tag: WindowEdge edge }
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        e.Handled = true;
        BeginResizeDrag(edge, e);
    }

    private void ToggleMaximizeTitleBar(object? sender, TappedEventArgs e) => MaximizeRestoreWindow(sender, e);
    private void MinimizeWindow(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeRestoreWindow(object? sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseWindow(object? sender, RoutedEventArgs e) => Close();

    private void JumpToLibrary(object? sender, RoutedEventArgs e) => ShowPage("library");
    private void JumpToResources(object? sender, RoutedEventArgs e) => ShowPage("resources");
    private void JumpToTasks(object? sender, RoutedEventArgs e) => ShowPage("tasks");
    private void ContextOpenLaunch(object? sender, RoutedEventArgs e) => ShowPage("launch");
    private void ContextOpenMods(object? sender, RoutedEventArgs e) => ShowPage("mods");
    private void ContextOpenSaves(object? sender, RoutedEventArgs e) => ShowPage("saves");
    private async void ContextOpenFolder(object? sender, RoutedEventArgs e)
    {
        if (SelectedName == null) return;
        string folder = launcher.InstanceFolder(SelectedName);
        if (Directory.Exists(folder)) await Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(folder));
    }

    private void QuickCategoryJump(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string kind }) OpenResourceCategory(kind);
    }

    internal void OpenResourceCategory(string kind)
    {
        ResourcesView.SelectCategory(kind);
        ShowPage("resources");
    }

    internal void OpenInstanceSubpage(string name, string page)
    {
        ReloadInstances(name);
        ShowPage(page);
    }

    internal void AskAiWithContext(string prompt)
    {
        AiView.PrefillPrompt(prompt);
        ShowPage("ai");
    }

    internal void ApplyInstanceJava(string exePath)
    {
        string? name = SelectedName;
        JavaPath.Text = exePath;
        if (!string.IsNullOrWhiteSpace(name))
        {
            var inst = new Instance(name);
            inst.SetMeta("java_path", string.IsNullOrWhiteSpace(exePath) ? "auto" : exePath);
            UpdateEnvironmentSummary(name);
            SetStatus(string.IsNullOrWhiteSpace(exePath) ? $"已恢复「{name}」为自动匹配 Java。" : $"已更新「{name}」的 Java 路径。");
        }
    }

    internal void QuickConnectServer(string instanceName, string serverAddress)
    {
        ReloadInstances(instanceName);
        pendingQuickServer = serverAddress;
        ShowPage("launch");
        SetStatus($"已选定直连 {serverAddress}，确认版本后即可启动。");
        if (LaunchButton.IsEnabled) LaunchDemo(this, new RoutedEventArgs());
    }

    private void ReloadInstances(string? selected)
    {
        var instances = launcher.InstancesList();
        InstanceList.ItemsSource = instances;
        InstanceList.SelectedItem = instances.FirstOrDefault(instance => instance.Name == selected) ?? instances.FirstOrDefault();
        InstanceCount.Text = instances.Count.ToString();
        LibraryEmpty.IsVisible = instances.Count == 0;
        InstanceList.IsVisible = instances.Count > 0;
        WelcomePanel.IsVisible = instances.Count == 0;
        Workspace.IsVisible = instances.Count > 0;
        Workspace.IsEnabled = InstanceList.SelectedItem != null && task == null;
        if (instances.Count == 0)
        {
            TopInstanceLabel.Text = "未创建实例";
            InstanceTitle.Text = "准备开始";
            InstanceHint.Text = "暂无实例";
            HeroCover.IsVisible = false;
        }
        ResourcesView.SetTarget(InstanceList.SelectedItem as InstanceEntry);
        InstallerView.RefreshInstances();
    }

    private void SelectInstance(object? sender, SelectionChangedEventArgs e)
    {
        if (InstanceList.SelectedItem is not InstanceEntry instance)
        {
            ResourcesView.SetTarget(null);
            return;
        }
        try
        {
            launcher.SelectInstance(instance.Name);
            InstanceTitle.Text = instance.Name;
            TopInstanceLabel.Text = instance.Name;
            JavaPath.Text = launcher.JavaPreference(instance.Name);
            LoadLaunchOptions(instance.Name);
            UpdateEnvironmentSummary(instance.Name);
            UpdateHeroVisuals(instance);
            JavaChoices.ItemsSource = null;
            JavaChoices.IsVisible = false;
            InstallTarget.Text = "安装到 · " + instance.Name;
            InstalledVersions.ItemsSource = instance.Versions;
            InstalledVersions.SelectedIndex = instance.Versions.Length == 0 ? -1 : 0;
            UpdateLaunchButton();
            Workspace.IsEnabled = task == null;
            ResourcesView.SetTarget(instance);
            if (ModsView.IsVisible) ModsView.Reload(instance.Name);
            if (SavesView.IsVisible) SavesView.Reload(instance.Name);
            if (ServersView.IsVisible) ServersView.Reload(instance.Name);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void UpdateHeroVisuals(InstanceEntry entry)
    {
        try
        {
            var inst = new Instance(entry.Name);
            string cover = InstanceArtwork.CustomCover(inst);
            HeroCover.Source = cover;
            HeroCover.IsVisible = cover.Length > 0 && File.Exists(cover);
        }
        catch
        {
            HeroCover.IsVisible = false;
        }
    }

    private void UpdateEnvironmentSummary(string instanceName)
    {
        try
        {
            var inst = new Instance(instanceName);
            string loader = Mods.DetectLoader(inst);
            string mcVer = Mods.DetectMcVersion(inst);
            string loaderLabel = string.IsNullOrWhiteSpace(loader) || loader == "vanilla" ? "原版" : char.ToUpperInvariant(loader[0]) + loader[1..];
            string group = launcher.LibraryList().FirstOrDefault(i => i.Name == instanceName)?.Group ?? "";
            HeroLoaderBadge.Text = string.IsNullOrWhiteSpace(mcVer) ? loaderLabel : $"{mcVer} · {loaderLabel}";
            HeroGroupBadge.Text = string.IsNullOrWhiteSpace(group) ? "独立实例" : group;

            int modCount = Mods.ListInstanceModEntries(inst).Count;
            int shaderCount = Mods.ListContentFiles(inst, "shaderpacks").Count;
            int worldCount = Saves.ListSaves(inst).Count;
            int serverCount = Servers.ListServers(inst).Count;
            var ptDict = Playtime.GetAllPlaytime() as PyDict;
            long sec = ptDict != null && ptDict.Get(instanceName) is PyDict pRow ? Py.Int(pRow.Get("total", 0L)) : 0L;
            string ptLabel = sec > 0 ? Playtime.FormatDuration(sec) : "尚未记录";

            MetricModsValue.Text = shaderCount > 0 ? $"{modCount} 模组 · {shaderCount} 光影" : $"{modCount} 个模组";
            MetricSavesValue.Text = $"{worldCount} 个世界";
            MetricServersValue.Text = $"{serverCount} 个服务器";
            MetricPlaytimeValue.Text = ptLabel;

            string javaPref = launcher.JavaPreference(instanceName);
            HomeJavaLabel.Text = string.IsNullOrWhiteSpace(javaPref) ? "Java 运行时：自动按核心版本匹配" : $"Java 运行时：{Path.GetFileName(javaPref)}";
            if (int.TryParse(MemoryBox.Text, out int mem))
            {
                HomeMemoryBadge.Text = $"{mem} MB · {WindowWidthBox.Text}×{WindowHeightBox.Text}";
                HomeMemoryBar.Value = Math.Clamp(mem, 512, 16384);
            }

            string verHint = string.IsNullOrWhiteSpace(mcVer) ? "尚未安装核心版本" : $"Minecraft {mcVer} ({loaderLabel})";
            InstanceHint.Text = $"{verHint} · {modCount} 个模组 · {worldCount} 个存档 · {MemoryBox.Text} MB";
        }
        catch
        {
            InstanceHint.Text = $"{MemoryBox.Text} MB";
        }
    }

    private async void CreateInstance(object? sender, RoutedEventArgs e)
    {
        string name = (InstanceName.Text ?? "").Trim();
        string? error = InstanceNameError(name);
        SetInstanceError(error);
        if (error != null) { FocusInstanceName(sender, e); return; }
        await RunTask(_ =>
        {
            try
            {
                var instance = launcher.CreateInstance(name);
                ReloadInstances(instance.Name);
                LibraryView.Reload(instance.Name);
                InstanceName.Clear();
                Status.Text = "实例已创建。";
            }
            catch (Exception ex)
            {
                SetInstanceError(ex.Message);
                throw;
            }
            return Task.CompletedTask;
        });
        if (!InstanceError.IsVisible && SelectedName == name)
        {
            if (sender is Button { Tag: "install" }) ShowPage("install");
            else RefreshButton.Focus();
        }
    }

    private string? InstanceNameError(string name)
    {
        if (name.Length == 0) return "请填写实例名称。";
        if (name.Length > 48 || Instances.SanitizeInstanceName(name) != name || name == Instances.RootName())
            return "请使用 1–48 个合法字符。";
        if (InstanceList.ItemsSource?.OfType<InstanceEntry>().Any(instance => string.Equals(instance.Name, name, StringComparison.OrdinalIgnoreCase)) == true)
            return "同名实例已存在。";
        return null;
    }

    private void SetInstanceError(string? message)
    {
        InstanceError.Text = message ?? "";
        InstanceError.IsVisible = message != null;
        InstanceName.Classes.Set("invalid", message != null);
        WelcomeName.Classes.Set("invalid", message != null);
        Avalonia.Automation.AutomationProperties.SetHelpText(InstanceName, message ?? "最多 48 个字符。按 Enter 创建实例。");
        Avalonia.Automation.AutomationProperties.SetHelpText(WelcomeName, message ?? "最多 48 个字符。按 Enter 创建实例并选择游戏版本。");
    }

    private void EditInstanceName(object? sender, TextChangedEventArgs e)
    {
        if (InstanceError?.IsVisible == true) SetInstanceError(null);
    }

    private void SubmitInstanceName(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || task != null) return;
        e.Handled = true;
        CreateInstance(sender, new RoutedEventArgs());
    }

    private void FocusInstanceName(object? sender, RoutedEventArgs e)
    {
        if (WelcomePanel.IsVisible && LaunchView.IsVisible) WelcomeName.Focus();
        else InstanceName.Focus();
    }

    private void SubmitWelcomeName(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || task != null) return;
        e.Handled = true;
        CreateInstance(WelcomeCreateButton, new RoutedEventArgs());
    }

    internal void BeginInstanceCreation()
    {
        ShowPage("launch");
        FocusInstanceName(this, new RoutedEventArgs());
    }

    private void ToggleInstaller(object? sender, RoutedEventArgs e)
    {
        InstallerSheet.IsVisible = !InstallerSheet.IsVisible;
        if (InstallerSheet.IsVisible) InstallerSheet.BringIntoView();
    }

    private void SelectInstalledVersion(object? sender, SelectionChangedEventArgs e) => UpdateLaunchButton();

    private void SelectRemoteVersion(object? sender, SelectionChangedEventArgs e)
    {
        if (LoaderVersionPicker != null) ResetLoaderVersions();
    }

    private async void RefreshVersions(object? sender, RoutedEventArgs e) => await RunTask(async token =>
    {
        Status.Text = "正在获取版本列表…";
        RefreshButton.Content = "获取中…";
        VersionNotice.Text = "正在连接版本服务…";
        VersionNotice.Classes.Set("error", false);
        string? selected = RemoteVersions.SelectedItem as string;
        try
        {
            string[] versions = await Task.Run(() => launcher.RemoteVersions(token), token);
            token.ThrowIfCancellationRequested();
            RemoteVersions.ItemsSource = versions;
            RemoteVersions.SelectedItem = versions.Contains(selected) ? selected : versions.FirstOrDefault();
            VersionNotice.Text = versions.Length == 0 ? "未找到可用版本。" : $"共 {versions.Length} 个正式版本";
            Status.Text = versions.Length == 0 ? "未找到正式版本。" : $"已加载 {versions.Length} 个版本。";
        }
        catch (Exception) when (token.IsCancellationRequested)
        {
            VersionNotice.Text = "已取消获取。";
            throw;
        }
        catch (Exception)
        {
            VersionNotice.Text = "获取失败，请检查网络。";
            VersionNotice.Classes.Set("error", true);
            throw;
        }
        finally { RefreshButton.Content = "刷新列表"; }
    });

    private async void InstallVersion(object? sender, RoutedEventArgs e)
    {
        if (SelectedName is not { } name || RemoteVersions.SelectedItem is not string version) return;
        string loader = SelectedLoader;
        string? build = (LoaderVersionPicker.SelectedItem as LoaderVersion)?.Id;
        int revision = loaderSelectionRevision;
        await InstallWithConfirmationAsync(name, version, loader, build, () => SelectedName == name
            && revision == loaderSelectionRevision && (LoaderVersionPicker.SelectedItem as LoaderVersion)?.Id == build, installed =>
        {
            InstalledVersions.SelectedItem = installed;
            InstallerSheet.IsVisible = false;
        });
    }

    private async void DetectJava(object? sender, RoutedEventArgs e) => await RunTask(async token =>
    {
        Status.Text = "正在检测本机 Java…";
        string[] paths = await Task.Run(launcher.DetectJava, token);
        token.ThrowIfCancellationRequested();
        JavaChoices.ItemsSource = paths;
        JavaChoices.IsVisible = paths.Length != 0;
        Status.Text = paths.Length == 0 ? "未检测到本机 Java，保持自动匹配即可。" : $"检测到 {paths.Length} 个运行时。";
    });

    private void SelectJava(object? sender, SelectionChangedEventArgs e)
    {
        if (JavaChoices.SelectedItem is string path) ApplyInstanceJava(path);
    }

    private async void BrowseJava(object? sender, RoutedEventArgs e) => await RunTask(async _ =>
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择 Java 可执行文件", AllowMultiple = false
        });
        if (files.Count == 0) return;
        string selected = files[0].TryGetLocalPath() ?? throw new IOException("请选择本机文件。");
        ApplyInstanceJava(selected);
        Status.Text = "Java 路径已更新。";
    });

    private async void LaunchDemo(object? sender, RoutedEventArgs e) => await RunTask(async token =>
    {
        string name = SelectedName ?? throw new InvalidOperationException("请选择实例。");
        string version = InstalledVersions.SelectedItem as string ?? throw new InvalidOperationException("请先安装游戏版本。");
        string java = JavaPath.Text ?? "";
        if (!launchOptionsReady) throw new InvalidOperationException("请先保存有效的内存与窗口设置。");
        launcher.SaveLaunchOptions(name, ReadLaunchOptions());
        UpdateEnvironmentSummary(name);
        bool demo = DemoMode.IsChecked == true;
        Status.Text = "正在准备启动…";
        var progress = CreateProgress();
        var props = demo ? null : await Task.Run(() => account?.ForLaunch(token)
            ?? throw new InvalidOperationException("请先登录微软账号，或启用试玩模式。"), token);
        UpdateAccount();
        var launch = await Task.Run(() => launcher.PrepareLaunch(name, version, java, progress, token, props), token);
        token.ThrowIfCancellationRequested();
        playing = true;
        activeTask?.SetCancellable(false);
        CancelButton.IsVisible = false;
        Progress.IsVisible = false;
        var startedAt = DateTimeOffset.UtcNow;
        try
        {
            await launcher.RunDemoAsync(launch, pid => Dispatcher.UIThread.Post(() =>
                Status.Text = $"运行中 (PID {pid})"));
        }
        finally
        {
            long elapsed = (long)Math.Max(1, (DateTimeOffset.UtcNow - startedAt).TotalSeconds);
            Playtime.RecordSession(name, version, elapsed);
            pendingQuickServer = null;
        }
        Status.Text = "游戏已退出。";
    });

    private IProgress<InstallProgress> CreateProgress()
    {
        var owner = task;
        return new Progress<InstallProgress>(update =>
        {
            if (owner == null || task != owner || owner.IsCancellationRequested || playing) return;
            Status.Text = update.Message;
            Progress.IsIndeterminate = update.Total <= 0;
            Progress.Value = update.Percent;
            activeTask?.Report(update.Message, update.Total <= 0 ? null : update.Percent);
        });
    }

    internal async Task RunTask(Func<CancellationToken, Task> action, [System.Runtime.CompilerServices.CallerMemberName] string operation = "")
    {
        if (task != null) { SetStatus("正在执行“" + (activeTask?.Title ?? "当前任务") + "”，请等待完成或取消后再操作。"); return; }
        using var cancellation = new CancellationTokenSource();
        task = cancellation;
        activeTask = new Models.DesktopTask(TaskTitle(operation));
        TasksView.Add(activeTask);
        if (operation is "Search" or "LoadReleases" or "RefreshVersions" or "RefreshLoaderVersions" or "RefreshLibrary") TasksView.SetRetry(activeTask, operation, action);
        TaskState.Text = "进行中";
        TaskState.Classes.Set("error", false);
        Avalonia.Automation.AutomationProperties.SetHelpText(Status, "");
        ToolTip.SetTip(Status, null);
        SetBusy(true);
        try
        {
            await action(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            activeTask.Finish(Models.DesktopTaskState.Completed, Status.Text ?? string.Empty);
        }
        catch (Exception) when (cancellation.IsCancellationRequested)
        {
            TaskState.Text = "已取消";
            Status.Text = "任务已取消。已完成的文件会保留，可以重新尝试。";
            activeTask.Finish(Models.DesktopTaskState.Cancelled, Status.Text);
        }
        catch (Exception ex)
        {
            ShowError(ex);
            activeTask.Finish(Models.DesktopTaskState.Failed, ex.Message);
        }
        finally
        {
            playing = false;
            task = null;
            activeTask = null;
            SetBusy(false);
            if (!closing)
            {
                ResourcesView.ResumeDocuments();
                ShowPage(currentPage);
            }
            if (TaskState.Text == "进行中") TaskState.Text = "已完成";
            if (closing) Close();
        }
    }

    private void SetBusy(bool busy)
    {
        InstanceList.IsEnabled = !busy;
        InstanceName.IsEnabled = !busy;
        CreateButton.IsEnabled = !busy;
        WelcomeName.IsEnabled = WelcomeCreateButton.IsEnabled = !busy;
        Workspace.IsEnabled = !busy && SelectedName != null;
        AccountPanel.IsEnabled = !busy;
        LibraryView.IsEnabled = true;
        ResourcesView.IsEnabled = true;
        Navigation.IsEnabled = true;
        CancelButton.IsVisible = busy;
        CancelButton.IsEnabled = busy;
        Progress.IsVisible = busy;
        Progress.IsIndeterminate = busy;
        if (!busy)
        {
            UpdateLaunchButton();
            if (SelectedName != null) UpdateEnvironmentSummary(SelectedName);
        }
    }

    private void CancelTask(object? sender, RoutedEventArgs e)
    {
        activeTask?.RequestCancellation();
        task?.Cancel();
        CancelButton.IsEnabled = false;
        Status.Text = "正在取消…";
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (task == null) return;
        e.Cancel = true;
        if (playing) { Status.Text = "请先退出正在运行的 Minecraft。"; return; }
        closing = true;
        CancelTask(this, new RoutedEventArgs());
    }

    private void ShowError(Exception ex)
    {
        TaskState.Text = "未完成";
        TaskState.Classes.Set("error", true);
        Status.Text = ex.Message;
        Avalonia.Automation.AutomationProperties.SetHelpText(Status, ex.Message);
        ToolTip.SetTip(Status, ex.Message);
    }

    internal PortableLauncher GameLauncher => launcher;
    internal IProgress<InstallProgress> TaskProgress() => CreateProgress();
    internal void SetStatus(string message) => Status.Text = message;
    internal void InstancesChanged(string name) => ReloadInstances(name);

    internal void OpenInstance(string name)
    {
        ReloadInstances(name);
        ShowPage("launch");
    }

    private void Navigate(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string page }) return;
        ShowPage(page);
    }

    internal void ShowPage(string page)
    {
        bool enteringInstaller = page == "install" && currentPage != page;
        currentPage = page;
        if (task == null)
        {
            try
            {
                switch (page)
                {
                    case "launch": if (SelectedName != null) UpdateEnvironmentSummary(SelectedName); break;
                    case "library": LibraryView.Reload(SelectedName); break;
                    case "install": InstallerView.RefreshInstances(enteringInstaller ? SelectedName : null); break;
                    case "mods": ModsView.Reload(SelectedName); break;
                    case "saves": SavesView.Reload(SelectedName); break;
                    case "servers": ServersView.Reload(SelectedName); break;
                    case "java": JavaView.Reload(); break;
                    case "playtime": PlaytimeView.Reload(); break;
                    case "settings": SettingsView.Reload(); break;
                }
            }
            catch (Exception error) { ShowError(error); }
        }

        bool isInstanceGroup = page is "library" or "mods" or "saves" or "servers";
        bool isSettingsGroup = page is "settings" or "java" or "playtime" or "tasks";

        InstanceSegBar.IsVisible = false;
        SettingsSegBar.IsVisible = false;
        ChromeTitle.Text = task == null ? "" : "当前任务进行中 · 部分数据完成后刷新";
        ChromeTitle.IsVisible = task != null;
        TopInstanceLabel.IsVisible = isInstanceGroup || page is "install" or "java";
        TopInstanceLabel.Text = "操作实例：" + (SelectedName ?? "未选择");

        foreach (var btn in InstanceSegButtons.Children.OfType<Button>())
            btn.Classes.Set("current", (string?)btn.Tag == page);
        foreach (var btn in SettingsSegButtons.Children.OfType<Button>())
            btn.Classes.Set("current", (string?)btn.Tag == page);

        LaunchView.IsVisible = page == "launch";
        LibraryView.IsVisible = page == "library";
        InstallerView.IsVisible = page == "install";
        if (!closing) InstallerView.ResumeLoaderLookup();
        ModsView.IsVisible = page == "mods";
        SavesView.IsVisible = page == "saves";
        ResourcesView.IsVisible = page == "resources";
        ResourcesView.ResumeSearch();
        ServersView.IsVisible = page == "servers";
        AiView.IsVisible = page == "ai";
        JavaView.IsVisible = page == "java";
        PlaytimeView.IsVisible = page == "playtime";
        TasksView.IsVisible = page == "tasks";
        SettingsView.IsVisible = page == "settings";
        FooterSettingsButton.Classes.Set("current", page == "settings");
        SettingsIcon.Active = page == "settings";
        ApplyFrontendLayout();

        foreach (var button in Navigation.Children.OfType<Button>())
        {
            bool current = (string?)button.Tag == page;
            button.Classes.Set("current", current);
            foreach (var icon in button.GetLogicalDescendants().OfType<LauncherIcon>()) icon.Active = current;
        }

        if (!Program.UiSmoke && PageHost != null)
        {
            PageHost.Opacity = 0.78;
            Dispatcher.UIThread.Post(() => PageHost.Opacity = 1, DispatcherPriority.Render);
        }
    }
}
