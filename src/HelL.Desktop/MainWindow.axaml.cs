using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using HelL.Portable;

namespace HelL.Desktop;

public sealed partial class MainWindow : Window
{
    private readonly PortableLauncher launcher;
    private CancellationTokenSource? task;
    private bool playing;
    private bool closing;
    private string? SelectedName => (InstanceList.SelectedItem as InstanceEntry)?.Name;

    public MainWindow() : this(Program.DataRoot) { }

    public MainWindow(string root)
    {
        InitializeComponent();
        DataDirectory.Text = root;
        launcher = new PortableLauncher(root);
        ResourcesView.Initialize(this);
        LibraryView.Initialize(this);
        ReloadInstances(launcher.SelectedInstance);
        ShowPage("launch");
        Closing += OnClosing;
        Opened += RestoreAccount;
        if (Program.UiSmoke) Opened += RunUiSmoke;
        if (Program.CatalogSmoke) Opened += RunCatalogSmoke;
    }

    private void ReloadInstances(string? selected)
    {
        var instances = launcher.InstancesList();
        InstanceList.ItemsSource = instances;
        InstanceList.SelectedItem = instances.FirstOrDefault(instance => instance.Name == selected) ?? instances.FirstOrDefault();
        Workspace.IsEnabled = InstanceList.SelectedItem != null && task == null;
    }

    private void SelectInstance(object? sender, SelectionChangedEventArgs e)
    {
        if (InstanceList.SelectedItem is not InstanceEntry instance) return;
        try
        {
            launcher.SelectInstance(instance.Name);
            InstanceTitle.Text = instance.Name;
            InstanceHint.Text = instance.Versions.Length == 0 ? "安装一个版本，开始新的冒险。" : "选择版本和 Java，准备进入世界。";
            JavaPath.Text = launcher.JavaPreference(instance.Name);
            InstalledVersions.ItemsSource = instance.Versions;
            InstalledVersions.SelectedIndex = instance.Versions.Length == 0 ? -1 : 0;
            UpdateLaunchButton();
            Workspace.IsEnabled = task == null;
            ResourcesView.SetTarget(instance);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async void CreateInstance(object? sender, RoutedEventArgs e) => await RunTask(async token =>
    {
        var instance = launcher.CreateInstance(InstanceName.Text ?? "");
        ReloadInstances(instance.Name);
        LibraryView.Reload(instance.Name);
        InstanceName.Clear();
        Status.Text = "实例已创建。获取版本列表后即可安装。";
        await Task.CompletedTask;
    });

    private async void RefreshVersions(object? sender, RoutedEventArgs e) => await RunTask(async token =>
    {
        Status.Text = "正在获取 Minecraft 正式版本…";
        string[] versions = await Task.Run(() => launcher.RemoteVersions(token), token);
        RemoteVersions.ItemsSource = versions;
        RemoteVersions.SelectedIndex = versions.Length == 0 ? -1 : 0;
        InstallButton.IsEnabled = versions.Length > 0;
        Status.Text = versions.Length == 0 ? "没有找到正式版本，请稍后重试。" : $"已加载 {versions.Length} 个正式版本。";
    });

    private async void InstallVersion(object? sender, RoutedEventArgs e) => await RunTask(async token =>
    {
        string name = SelectedName ?? throw new InvalidOperationException("请先创建或选择实例。");
        string version = RemoteVersions.SelectedItem as string ?? throw new InvalidOperationException("请获取并选择要安装的版本。");
        var progress = CreateProgress();
        await Task.Run(() => launcher.Install(name, version, progress, token), token);
        ReloadInstances(name);
        InstalledVersions.SelectedItem = version;
        Status.Text = $"Minecraft {version} 安装完成，可以启动试玩。";
    });

    private async void DetectJava(object? sender, RoutedEventArgs e) => await RunTask(async token =>
    {
        Status.Text = "正在检测本机 Java…";
        string[] paths = await Task.Run(launcher.DetectJava, token);
        token.ThrowIfCancellationRequested();
        JavaChoices.ItemsSource = paths;
        JavaChoices.IsVisible = paths.Length != 0;
        Status.Text = paths.Length == 0 ? "未找到 Java。可保持自动选择，启动时下载兼容版本。" : $"找到 {paths.Length} 个 Java，请选择；留空则自动匹配。";
    });

    private void SelectJava(object? sender, SelectionChangedEventArgs e)
    {
        if (JavaChoices.SelectedItem is string path) JavaPath.Text = path;
    }

    private async void BrowseJava(object? sender, RoutedEventArgs e) => await RunTask(async token =>
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择 Java 可执行文件（java / java.exe）", AllowMultiple = false
        });
        if (files.Count == 0) return;
        JavaPath.Text = files[0].TryGetLocalPath() ?? throw new IOException("请选择本机文件。");
        Status.Text = "已选择 Java，启动前会验证兼容性。";
    });

    private async void LaunchDemo(object? sender, RoutedEventArgs e) => await RunTask(async token =>
    {
        string name = SelectedName ?? throw new InvalidOperationException("请选择实例。");
        string version = InstalledVersions.SelectedItem as string ?? throw new InvalidOperationException("请先安装游戏版本。");
        string java = JavaPath.Text ?? "";
        bool demo = DemoMode.IsChecked == true;
        Status.Text = "正在检查 Java 与启动文件…";
        var progress = CreateProgress();
        var props = demo ? null : await Task.Run(() => account?.ForLaunch(token)
            ?? throw new InvalidOperationException("请先登录微软账号，或选择试玩模式。"), token);
        UpdateAccount();
        var launch = await Task.Run(() => launcher.PrepareLaunch(name, version, java, progress, token, props), token);
        token.ThrowIfCancellationRequested();
        playing = true;
        CancelButton.IsVisible = false;
        Progress.IsVisible = false;
        await launcher.RunDemoAsync(launch, pid => Dispatcher.UIThread.Post(() =>
            Status.Text = $"Minecraft 正在运行（进程 {pid}）。请在游戏中正常退出。"));
        Status.Text = "游戏已正常退出。";
    });

    private IProgress<InstallProgress> CreateProgress() => new Progress<InstallProgress>(update =>
    {
        if (task == null || task.IsCancellationRequested || playing) return;
        Status.Text = update.Message;
        Progress.IsIndeterminate = update.Total <= 0;
        Progress.Value = update.Percent;
    });

    internal async Task RunTask(Func<CancellationToken, Task> action)
    {
        if (task != null) return;
        using var cancellation = new CancellationTokenSource();
        task = cancellation;
        SetBusy(true);
        try { await action(cancellation.Token); }
        catch (Exception) when (cancellation.IsCancellationRequested)
        { Status.Text = "任务已取消。已完成的文件会保留，可以重新尝试。"; }
        catch (Exception ex) { ShowError(ex); }
        finally
        {
            playing = false;
            task = null;
            SetBusy(false);
            if (closing) Close();
        }
    }

    private void SetBusy(bool busy)
    {
        InstanceList.IsEnabled = !busy;
        InstanceName.IsEnabled = !busy;
        CreateButton.IsEnabled = !busy;
        Workspace.IsEnabled = !busy && SelectedName != null;
        AccountPanel.IsEnabled = !busy;
        Navigation.IsEnabled = !busy;
        LibraryView.IsEnabled = !busy;
        ResourcesView.IsEnabled = !busy;
        CancelButton.IsVisible = busy;
        CancelButton.IsEnabled = busy;
        Progress.IsVisible = busy;
        Progress.IsIndeterminate = busy;
    }

    private void CancelTask(object? sender, RoutedEventArgs e)
    {
        task?.Cancel();
        CancelButton.IsEnabled = false;
        Status.Text = "正在取消，请稍候…";
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (task == null) return;
        e.Cancel = true;
        if (playing) { Status.Text = "请先在 Minecraft 中退出游戏，再关闭启动器。"; return; }
        closing = true;
        CancelTask(this, new RoutedEventArgs());
    }

    private void ShowError(Exception ex) => Status.Text = "未完成：" + ex.Message;

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
        if (sender is Button { Tag: string page })
        {
            if (page == "library")
            {
                try { LibraryView.Reload(); }
                catch (Exception error) { ShowError(error); }
            }
            ShowPage(page);
        }
    }
    internal void ShowPage(string page)
    {
        LaunchView.IsVisible = page == "launch";
        LibraryView.IsVisible = page == "library";
        ResourcesView.IsVisible = page == "resources";
        foreach (var button in Navigation.Children.OfType<Button>())
            button.Classes.Set("current", (string?)button.Tag == page);
    }
}
