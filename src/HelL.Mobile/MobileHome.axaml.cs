using System.IO.Compression;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using HelL.Core;
using HelL.Core.Launcher;
using HelL.Portable;

namespace HelL.Mobile;

public sealed partial class MobileHome : UserControl, IDisposable
{
    private readonly PortableLauncher launcher;
    private readonly PortableCatalog catalog = new();
    private readonly MobileSession session;
    private readonly string root;
    private IReadOnlyList<LibraryInstance> instances = [];
    private CancellationTokenSource? operation;
    private string selected = "";
    private string reference = "";
    private CatalogProject? project;
    private bool original;
    private bool ready;
    private bool disposed;
    private string SelectedKind => (Kind.SelectedItem as ComboBoxItem)?.Tag as string ?? "resourcepack";
    private string SelectedProvider => (Provider.SelectedItem as ComboBoxItem)?.Tag as string ?? "modrinth";

    public MobileHome() : this(MobileRuntime.Root) { }
    public MobileHome(string directory)
    {
        InitializeComponent();
        SizeChanged += ArrangeWorkspace;
        Status.PropertyChanged += (_, change) =>
        {
            if (change.Property == TextBlock.TextProperty) StatusBar.IsVisible = !string.IsNullOrWhiteSpace(Status.Text);
        };
        root = Path.GetFullPath(directory);
        session = MobileSession.Open(root);
        launcher = session.Launcher;
        InitializeAppearance();
        InitializeDiagnostics();
        DataPath.Text = root;
        ready = true;
        Reload();
        ShowPage("home");
        SyncCategory();
        if (!MobileRuntime.Smoke && !MobileRuntime.VerifyRestart) AttachedToVisualTree += RestoreAccount;
        if (MobileRuntime.VerifyRestart)
        {
            var saved = instances.Single();
            if (saved.Name != "手机生存世界" || saved.Group != "移动工作区" || !saved.Pinned || launcher.ReferenceVersion(saved.Name) != "1.21.1")
                throw new IOException("Android restart did not restore the isolated instance.");
            File.WriteAllText(Path.Combine(MobileRuntime.ReportRoot, "mobile-restart.json"), "{\"ok\":true,\"restoredInstances\":1,\"referenceVersion\":\"1.21.1\"}");
        }
        if (MobileRuntime.Smoke) AttachedToVisualTree += RunSmoke;
    }

    private void Reload(string? select = null)
    {
        instances = launcher.LibraryList();
        selected = select ?? selected;
        var entry = instances.FirstOrDefault(row => row.Name == selected) ?? instances.FirstOrDefault();
        InstanceEditor.IsVisible = entry != null;
        if (entry != null) PresentInstance(entry);
        else UpdateHome(null);
        PresentInstances();
    }

    private void FilterInstances(object? sender, TextChangedEventArgs e) { if (ready) PresentInstances(); }
    private void SelectInstance(object? sender, SelectionChangedEventArgs e)
    {
        if (!ready || loadingLibrary || InstancesList.SelectedItem is not InstanceRow row) return;
        var entry = instances.First(item => item.Name == row.Name);
        PresentInstance(entry);
        PresentInstances();
    }

    private void PresentInstance(LibraryInstance entry)
    {
        try
        {
            selected = entry.Name;
            launcher.SelectInstance(selected);
            reference = launcher.ReferenceVersion(selected);
            InstanceName.Text = selected;
            InstanceGroup.Text = entry.Group;
            ReferenceVersion.Text = reference;
            ReferenceLoader.SelectedItem = ReferenceLoader.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string?)item.Tag == launcher.ReferenceLoader(selected));
            PinButtonText.Text = entry.Pinned ? "取消固定" : "固定实例";
            ResourceTarget.Text = $"{selected} · 参考 {reference} / {launcher.ReferenceLoader(selected)}\n仅筛选资源，未安装游戏运行内核。";
            InstanceEditor.IsVisible = true;
            ClearProjects();
            UpdateHome(entry);
        }
        catch (Exception error) { Status.Text = "读取实例失败：" + error.Message; }
    }

    private async void CreateInstance(object? sender, RoutedEventArgs e) => await Run(async token =>
    {
        string name = NewName.Text?.Trim() ?? "";
        string version = ValidateReference(NewVersion.Text);
        launcher.CreateInstance(name);
        launcher.SetReferenceVersion(name, version);
        NewName.Clear();
        Reload(name);
        ShowPage("library");
        Status.Text = "实例已创建，可以查找兼容资源。";
        await Task.CompletedTask;
    });

    private async void SaveInstance(object? sender, RoutedEventArgs e) => await Run(_ =>
    {
        RequireSelection();
        string name = InstanceName.Text?.Trim() ?? "";
        string group = InstanceGroups.Normalize(InstanceGroup.Text ?? "");
        string version = ValidateReference(ReferenceVersion.Text);
        string loader = (ReferenceLoader.SelectedItem as ComboBoxItem)?.Tag as string ?? "vanilla";
        launcher.RenameInstance(selected, name);
        selected = name;
        launcher.GroupInstance(name, group);
        launcher.SetReferenceVersion(name, version, loader);
        Reload(name);
        Status.Text = "实例设置已保存。";
        return Task.CompletedTask;
    });

    private async void PinInstance(object? sender, RoutedEventArgs e) => await Run(_ =>
    {
        RequireSelection();
        bool pinned = instances.First(row => row.Name == selected).Pinned;
        launcher.PinInstance(selected, !pinned);
        Reload(selected);
        Status.Text = pinned ? "已取消固定。" : "实例已固定。";
        return Task.CompletedTask;
    });

    private async void ResetCover(object? sender, RoutedEventArgs e) => await Run(_ =>
    {
        RequireSelection(); launcher.ResetInstanceCover(selected); Reload(selected);
        Status.Text = "已恢复实例截图或存档图标。";
        return Task.CompletedTask;
    });

    private async void ChooseCover(object? sender, RoutedEventArgs e) => await Run(async token =>
    {
        RequireSelection();
        var top = TopLevel.GetTopLevel(this) ?? throw new IOException("窗口尚未就绪。");
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "选择实例封面", AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("图片") { MimeTypes = ["image/png", "image/jpeg"], Patterns = ["*.png", "*.jpg", "*.jpeg"] }] });
        if (files.Count == 0) return;
        string temporary = Path.Combine(root, ".cover-import-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            await using (var source = await files[0].OpenReadAsync())
            await using (var output = File.Create(temporary))
            {
                byte[] buffer = new byte[32768];
                int count;
                while ((count = await source.ReadAsync(buffer, token)) > 0)
                {
                    if (output.Length + count > 32 * 1024 * 1024) throw new IOException("图片超过 32 MB，请选择较小文件。");
                    await output.WriteAsync(buffer.AsMemory(0, count), token);
                }
            }
            await Task.Run(() => launcher.SetInstanceCover(selected, temporary), token);
            Reload(selected); Status.Text = "实例封面已保存。";
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    });

    private async void ExportInstance(object? sender, RoutedEventArgs e) => await Run(async token =>
    {
        RequireSelection();
        var top = TopLevel.GetTopLevel(this) ?? throw new IOException("窗口尚未就绪。");
        var destination = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            { Title = "导出实例备份", SuggestedFileName = selected + "-backup.zip", DefaultExtension = "zip", ShowOverwritePrompt = true });
        if (destination == null) return;
        string folder = launcher.InstanceFolder(selected);
        await using var stream = await destination.OpenWriteAsync();
        if (stream.CanSeek) stream.SetLength(0);
        await Task.Run(() =>
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
            var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false };
            foreach (string path in Directory.EnumerateFiles(folder, "*", options))
            {
                token.ThrowIfCancellationRequested();
                if (path.EndsWith(".part", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
                archive.CreateEntryFromFile(path, Path.GetRelativePath(folder, path).Replace('\\', '/'), CompressionLevel.Fastest);
            }
        }, token);
        Status.Text = "实例备份已导出，不含账号和系统凭据。";
    });

    private void Navigate(object? sender, RoutedEventArgs e) { if (sender is Button { Tag: string page }) ShowPage(page); }
    private void GoResources(object? sender, RoutedEventArgs e) => ShowPage("resources");
    private void ShowPage(string page)
    {
        ChromeTitle.Text = page switch
        {
            "diagnostics" => "诊断包",
            "home" => "首页", "library" => "实例库", "resources" => "下载资源",
            "create" => "创建实例", "manage" => "实例设置", "account" => "账号",
            "about" => "关于", "ai" => "AI 助手", "remote" => "远程电脑", _ => "我的"
        };
        if (operation == null) Status.Text = "";
        HomePage.IsVisible = page == "home";
        LibraryWorkspace.IsVisible = page == "library";
        ResourcesPage.IsVisible = page == "resources";
        if (page == "resources") ResourcesPage.Offset = Avalonia.Vector.Zero;
        AboutPage.IsVisible = page == "about";
        CreatePage.IsVisible = page == "create";
        ManagePage.IsVisible = page == "manage";
        AccountPage.IsVisible = page == "account";
        MorePage.IsVisible = page == "more";
        AiPage.IsVisible = page == "ai";
        RemotePage.IsVisible = page == "remote";
        if (diagnosticPage is not null) diagnosticPage.IsVisible = page == "diagnostics";
        DiagnosticPageChanged(page == "diagnostics");
        if (page == "remote") InitializeRemote();
        SetRemotePageActive(page == "remote");
        if (page == "ai") InitializeAi();
        string tab = page is "create" or "manage" ? "library" : page is "about" or "account" or "remote" or "diagnostics" ? "more" : page;
        foreach (var button in RailNavigation.Children.OfType<Button>().Concat(BottomNavigation.Children.OfType<Button>()))
        {
            bool active = (string?)button.Tag == tab;
            button.Classes.Set("current", active);
            if (button.Content is StackPanel panel && panel.Children[0] is Material.Icons.Avalonia.MaterialIcon icon)
                icon.Kind = ((string?)button.Tag, active) switch
                {
                    ("home", true) => Material.Icons.MaterialIconKind.Home,
                    ("home", false) => Material.Icons.MaterialIconKind.HomeOutline,
                    ("library", true) => Material.Icons.MaterialIconKind.ViewGrid,
                    ("library", false) => Material.Icons.MaterialIconKind.ViewGridOutline,
                    ("resources", _) => Material.Icons.MaterialIconKind.DownloadOutline,
                    ("ai", true) => Material.Icons.MaterialIconKind.Chat,
                    ("ai", false) => Material.Icons.MaterialIconKind.ChatOutline,
                    ("account", true) => Material.Icons.MaterialIconKind.Account,
                    ("account", false) => Material.Icons.MaterialIconKind.AccountOutline,
                    ("more", true) => Material.Icons.MaterialIconKind.Account,
                    ("more", false) => Material.Icons.MaterialIconKind.AccountOutline,
                    _ => Material.Icons.MaterialIconKind.ViewGridOutline
                };
        }
        if (page == "library") PresentInstances();
        if (page == "resources" && searchResults.Count == 0 && !MobileRuntime.Smoke && selected.Length > 0 && operation == null)
            SearchResources(this, new RoutedEventArgs());
    }

    private void RequireSelection()
    { if (selected.Length == 0) throw new InvalidOperationException("请先创建或选择一个实例。"); }
    private static string ValidateReference(string? text)
    {
        string version = text?.Trim() ?? "";
        if (!System.Text.RegularExpressions.Regex.IsMatch(version, @"\A1\.\d+(?:\.\d+)?\z")) throw new ArgumentException("请输入正式版本号，例如 1.21.1。");
        return version;
    }

    private async Task Run(Func<CancellationToken, Task> action, bool allowNavigation = false)
    {
        if (disposed || operation != null) return;
        using var cancellation = new CancellationTokenSource();
        bool acquired = false;
        operation = cancellation;
        DismissStatusButton.IsVisible = false;
        if (!allowNavigation) Pages.IsEnabled = RailNavigation.IsEnabled = BottomNavigation.IsEnabled = false;
        UpdateAccount();
        UpdateAi();
        Progress.IsVisible = CancelButton.IsVisible = true;
        try
        {
            await session.Operations.WaitAsync(cancellation.Token);
            acquired = true;
            cancellation.Token.ThrowIfCancellationRequested();
            await action(cancellation.Token);
        }
        catch (Exception) when (cancellation.IsCancellationRequested) { Status.Text = "操作已取消，可以重新开始。"; }
        catch (Exception error) { RememberDiagnosticIssue(error); Status.Text = "未完成：" + error.Message; }
        finally
        {
            if (acquired) session.Operations.Release();
            operation = null;
            DismissStatusButton.IsVisible = true;
            Pages.IsEnabled = RailNavigation.IsEnabled = BottomNavigation.IsEnabled = true;
            UpdateAccount();
            UpdateAi();
            Progress.IsVisible = CancelButton.IsVisible = false;
        }
    }
    private void Cancel(object? sender, RoutedEventArgs e) { operation?.Cancel(); Status.Text = "正在取消…"; }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        ++coverRevision;
        operation?.Cancel();
        DisposeDiagnostics();
        ClearAuthorization();
        SizeChanged -= ArrangeWorkspace;
        AttachedToVisualTree -= RestoreAccount;
        AttachedToVisualTree -= RunSmoke;
        DetachRemote();
    }
}
