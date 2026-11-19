using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace HelL.Desktop;

public sealed partial class MainWindow
{
    private async void RunUiSmoke(object? sender, EventArgs e)
    {
        int exitCode = 1;
        try
        {
            Services.ImageDecodeCheck.Run();
            await Task.Delay(600);
            if (InstanceList.ItemCount != 0) throw new InvalidOperationException("界面自检要求空数据目录。");
            if (!WelcomePanel.IsVisible || !LibraryEmpty.IsVisible || Workspace.IsVisible)
                throw new InvalidOperationException("空实例库没有显示创建引导。");
            CreateButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!InstanceError.IsVisible || InstanceList.ItemCount != 0)
                throw new InvalidOperationException("空名称应显示字段错误并保留实例列表。");
            await Capture(800, 600, "ui-empty-validation.png");
            InstanceName.Text = "中文生存世界 · 跨平台便携测试";
            CreateButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            if (InstanceList.ItemCount != 1 || !Workspace.IsEnabled || !InstanceTitle.Text!.StartsWith("中文生存世界"))
                throw new InvalidOperationException("实例创建后的界面状态不正确。");
            if (WelcomePanel.IsVisible || LibraryEmpty.IsVisible || !Workspace.IsVisible || LaunchButton.IsEnabled || InstallButton.IsEnabled)
                throw new InvalidOperationException("未安装版本时的引导或操作可用性不正确。");
            InstanceName.Text = InstanceTitle.Text;
            CreateButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!InstanceError.IsVisible || InstanceList.ItemCount != 1)
                throw new InvalidOperationException("重复名称应在输入框旁解释原因。");
            InstanceName.Clear();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            if (InstanceError.IsVisible) throw new InvalidOperationException("编辑名称后字段错误没有清除。");
            RemoteVersions.ItemsSource = new[] { "1.21.1", "1.20.1" };
            RemoteVersions.SelectedIndex = 0;
            if (!InstallButton.IsEnabled) throw new InvalidOperationException("选择版本后安装操作未启用。");
            LoaderPicker.SelectedIndex = 1;
            if (InstallButton.IsEnabled || !RefreshLoaderButton.IsEnabled)
                throw new InvalidOperationException("Loader installation must require an explicit loader build.");
            LoaderVersionPicker.ItemsSource = new[] { new HelL.Portable.LoaderVersion("0.16.5", "0.16.5", true) };
            LoaderVersionPicker.SelectedIndex = 0;
            if (!InstallButton.IsEnabled) throw new InvalidOperationException("Selected loader build did not enable installation.");
            RemoteVersions.SelectedIndex = 1;
            if (InstallButton.IsEnabled || LoaderVersionPicker.ItemCount != 0)
                throw new InvalidOperationException("Changing game version retained an incompatible loader build.");
            LoaderPicker.SelectedIndex = 0;
            MemoryBox.Text = "4096";
            WindowWidthBox.Text = "1280";
            WindowHeightBox.Text = "720";
            SaveLaunchOptions(this, new RoutedEventArgs());
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            if (launcher.LaunchOptions(SelectedName!) != new HelL.Portable.PortableLaunchOptions(4096, 1280, 720))
                throw new InvalidOperationException("Launch options were not saved from their actual fields.");
            RemoteVersions.SelectedIndex = -1;
            if (InstallButton.IsEnabled) throw new InvalidOperationException("清除版本选择后安装操作仍可用。");
            RemoteVersions.ItemsSource = null;
            await Capture(1060, 720);
            await Capture(800, 600);
            LibraryView.Reload(SelectedName);
            ShowPage("library");
            if (LibraryView.Selected?.Name != SelectedName || !LibraryView.IsVisible || LaunchView.IsVisible)
                throw new InvalidOperationException("实例库没有显示当前实例或导航状态不正确。");
            await Capture(1060, 720, "ui-library-1060.png");
            await Capture(800, 600, "ui-library-800.png");
            ShowPage("resources");
            if (!ResourcesView.IsVisible || ResourcesView.FindControl<TextBlock>("TargetLabel")?.Text != SelectedName)
                throw new InvalidOperationException("资源页面没有保留所选实例。");
            if (ResourcesView.FindControl<Button>("DownloadButton")?.IsEnabled != false)
                throw new InvalidOperationException("未选择兼容文件时下载按钮不应启用。");
            await Capture(1060, 720, "ui-resources-1060.png");
            await Capture(800, 600, "ui-resources-800.png");
            ShowPage("launch");
            IProgress<HelL.Portable.InstallProgress>? oldProgress = null;
            await RunTask(token =>
            {
                oldProgress = CreateProgress();
                return Task.CompletedTask;
            });
            bool staleProgressIgnored = false;
            await RunTask(async token =>
            {
                oldProgress!.Report(new("过期任务不应覆盖状态", 99, 100));
                await Task.Delay(50);
                staleProgressIgnored = Status.Text != "过期任务不应覆盖状态";
                CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                token.ThrowIfCancellationRequested();
            });
            if (!staleProgressIgnored) throw new InvalidOperationException("旧任务进度覆盖了新任务。");
            if (TaskState.Text != "已取消" || CancelButton.IsVisible || Progress.IsVisible || !Workspace.IsEnabled)
                throw new InvalidOperationException("取消后任务状态或工作区没有恢复。");
            if (TasksView.History.First().State != Models.DesktopTaskState.Cancelled)
                throw new InvalidOperationException("Task history did not record cancellation.");
            await RunTask(_ => throw new IOException("测试任务失败，请重试原操作。"), "InstallVersion");
            if (TasksView.History.First().State != Models.DesktopTaskState.Failed)
                throw new InvalidOperationException("Task history did not record failure.");
            ShowPage("tasks");
            var statePicker = TasksView.FindControl<ComboBox>("StatePicker")!;
            var taskList = TasksView.FindControl<ListBox>("TaskList")!;
            statePicker.SelectedIndex = 2;
            if (taskList.ItemCount != 1 || TasksView.History.First().Log.Count == 0)
                throw new InvalidOperationException("Failed-task filter or log is incorrect.");
            await Capture(800, 600, "ui-tasks-failed-800.png");
            TasksView.FindControl<TextBox>("SearchBox")!.Text = "没有匹配的任务";
            if (taskList.ItemCount != 0 || !TasksView.FindControl<TextBlock>("EmptyNotice")!.IsVisible)
                throw new InvalidOperationException("Task search did not show its empty state.");
            TasksView.FindControl<TextBox>("SearchBox")!.Clear();
            statePicker.SelectedIndex = 0;
            Task running = RunTask(async token =>
            {
                SetStatus("正在下载测试资源…");
                await Task.Delay(Timeout.Infinite, token);
            }, "DownloadSelected");
            if (!Navigation.IsEnabled || !TasksView.History.First().CanCancel)
                throw new InvalidOperationException("Task navigation or cancellation is unavailable.");
            await Capture(1060, 720, "ui-tasks-running-1060.png");
            TasksView.FindControl<Button>("CancelButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await running;
            if (TasksView.History.First().State != Models.DesktopTaskState.Cancelled)
                throw new InvalidOperationException("Task page did not cancel the active operation.");
            SelectTheme("light");
            if (Application.Current!.ActualThemeVariant != Avalonia.Styling.ThemeVariant.Light)
                throw new InvalidOperationException("Light theme did not apply.");
            ShowPage("launch");
            await Capture(1120, 740, "ui-light-1060.png");
            SelectTheme("dark");
            if (HelL.Core.Py.Str(HelL.Core.Launcher.Config.CONFIG.Get("portable_theme")) != "dark")
                throw new InvalidOperationException("Theme preference was not saved.");

            // Verify new full-featured modules with isolated fixtures
            string instRoot = launcher.InstanceFolder(SelectedName!);
            string modsFolder = Path.Combine(instRoot, "mods");
            Directory.CreateDirectory(modsFolder);
            File.WriteAllBytes(Path.Combine(modsFolder, "sodium-fabric-0.6.5.jar"), [0x50, 0x4B, 0x03, 0x04]);
            File.WriteAllBytes(Path.Combine(modsFolder, "xaeros-minimap-24.6.1.jar.disabled"), [0x50, 0x4B, 0x03, 0x04]);
            ShowPage("mods");
            if (!ModsView.IsVisible || ModsView.FindControl<ListBox>("ContentList")!.ItemCount != 2)
                throw new InvalidOperationException("Mods manager did not list installed mods.");
            await Capture(1180, 760, "ui-mods-1180.png");

            ShowPage("install");
            if (!InstallerView.IsVisible) throw new InvalidOperationException("Installer view did not open.");
            await Capture(1180, 760, "ui-install-1180.png");

            Directory.CreateDirectory(Path.Combine(instRoot, "saves", "樱花城堡生存存档"));
            File.WriteAllText(Path.Combine(instRoot, "saves", "樱花城堡生存存档", "level.dat"), "world fixture");
            ShowPage("saves");
            if (!SavesView.IsVisible || SavesView.FindControl<ListBox>("WorldsList")!.ItemCount != 1)
                throw new InvalidOperationException("Saves view did not list world save.");
            await Capture(1180, 760, "ui-saves-1180.png");

            HelL.Core.Launcher.Servers.AddServer(new HelL.Core.Launcher.Instance(SelectedName!), "HelsteraMC 联机测试服", "mc.example.org", 25565L, "1.21.1 生存联机");
            ShowPage("servers");
            if (!ServersView.IsVisible || ServersView.FindControl<ListBox>("ServerList")!.ItemCount != 1)
                throw new InvalidOperationException("Servers view did not persist and display server entry.");
            await Capture(1180, 760, "ui-servers-1180.png");

            HelL.Core.Launcher.Playtime.RecordSession(SelectedName!, "1.21.1", 12600);
            HelL.Core.Launcher.Playtime.RecordSession("1.20.1 Forge · 机械动力", "1.20.1", 7200);
            HelL.Core.Launcher.Playtime.RecordSession("1.20.4 整合包探险", "1.20.4", 3600);
            ShowPage("playtime");
            if (!PlaytimeView.IsVisible || PlaytimeView.FindControl<ListBox>("RankList")!.ItemCount != 3)
                throw new InvalidOperationException("Playtime view did not display recorded sessions.");
            await Capture(1180, 760, "ui-playtime-1180.png");

            ShowPage("ai");
            if (!AiView.IsVisible) throw new InvalidOperationException("AI view did not open.");
            await Capture(1180, 760, "ui-ai-1180.png");

            ShowPage("java");
            if (!JavaView.IsVisible) throw new InvalidOperationException("Java view did not open.");
            await Capture(1180, 760, "ui-java-1180.png");

            ShowPage("settings");
            if (!SettingsView.IsVisible) throw new InvalidOperationException("Settings view did not open.");
            await Capture(1180, 760, "ui-settings-1180.png");

            ShowPage("launch");
            await Capture(1180, 760, "ui-1180.png");
            if (Program.AuthUiSmoke)
            {
                RememberAccount.IsChecked = false;
                LoginButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Until(() => AuthorizationPanel.IsVisible);
                await Capture(800, 600, "ui-auth-code.png");
                await Until(() => task == null);
                if (account?.DisplayName != "TestPlayer" || AuthorizationPanel.IsVisible || DemoMode.IsChecked == true)
                    throw new InvalidOperationException("登录后的界面状态不正确。");
                AccountPanel.BringIntoView();
                await Capture(800, 600, "ui-auth-signed-in.png");
                RefreshAccountButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Until(() => task == null);
                if (Status.Text != "授权刷新完成。") throw new InvalidOperationException("刷新授权的界面状态不正确。");
                LogoutButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Until(() => task == null);
                if (account.IsSignedIn || DemoMode.IsChecked != true) throw new InvalidOperationException("退出登录后状态未清理。");
            }
            VerifyCorruptLibraryStartup();
            File.WriteAllText(Path.Combine(Program.DataRoot, "ui-smoke.json"), JsonSerializer.Serialize(new
            {
                ok = true, platform = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier,
                widths = new[] { 1060, 800 }, instanceCreation = true, selection = true,
                emptyState = true, inlineValidation = true, duplicateName = true,
                versionSelection = true, cancellation = true, staleProgress = true,
                authenticationReplay = Program.AuthUiSmoke,
                libraryNavigation = true, resourceTarget = true, unavailableDownloadDisabled = true,
                corruptLibraryStartup = true,
                imageDecodeBounds = true,
                taskHistory = true, taskFiltering = true, taskCancellation = true,
                darkAndLightThemes = true, themePersistence = true,
                launchOptions = true, loaderSelection = true, staleLoaderSelection = true
            }));
            exitCode = 0;
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(Program.DataRoot, "ui-smoke-error.txt"), ex.ToString()); }
        finally { (Application.Current!.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)!.Shutdown(exitCode); }

        async Task Capture(int width, int height, string? file = null)
        {
            Width = width;
            Height = height;
            await Task.Delay(400);
            using var bitmap = new RenderTargetBitmap(new PixelSize((int)Bounds.Width, (int)Bounds.Height), new Vector(96, 96));
            bitmap.Render(this);
            bitmap.Save(Path.Combine(Program.DataRoot, file ?? $"ui-{width}.png"), new PngBitmapEncoderOptions());
        }

        async Task Until(Func<bool> completed)
        {
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (!completed())
            {
                if (DateTime.UtcNow >= deadline) throw new TimeoutException("账号界面自检超时：" + Status.Text);
                await Task.Delay(50);
            }
        }
    }

    private void VerifyCorruptLibraryStartup()
    {
        string healthy = SelectedName ?? throw new InvalidOperationException("实例库自检缺少健康实例。");
        string broken = "损坏元数据验收";
        launcher.CreateInstance(broken);
        string metadata = Path.Combine(launcher.InstanceFolder(broken), ".instance.json");
        byte[] original = File.ReadAllBytes(metadata);
        MainWindow? probe = null;
        try
        {
            File.WriteAllText(metadata, "not json");
            probe = new MainWindow(Program.DataRoot);
            if (probe.InstanceList.ItemCount != 2 || !probe.LibraryView.FindControl<TextBlock>("LibraryError")!.IsVisible ||
                probe.LibraryView.FindControl<StackPanel>("Editor")!.IsEnabled)
                throw new InvalidOperationException("损坏实例应只阻止实例库编辑，不应阻止主窗口初始化。");
            probe.OpenInstance(healthy);
            if (probe.SelectedName != healthy || !probe.Workspace.IsEnabled || File.ReadAllText(metadata) != "not json")
                throw new InvalidOperationException("健康实例应仍可选择，损坏元数据不得被覆盖。");
            File.WriteAllBytes(metadata, original);
            probe.LibraryView.Reload(healthy);
            if (probe.LibraryView.FindControl<TextBlock>("LibraryError")!.IsVisible || probe.LibraryView.Selected?.Name != healthy)
                throw new InvalidOperationException("元数据修复后实例库刷新未恢复。");
        }
        finally
        {
            File.WriteAllBytes(metadata, original);
            probe?.Close();
        }
    }
}
