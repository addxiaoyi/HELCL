using System.IO.Compression;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using HelL.Core;
using HelL.Core.Launcher;
using HelL.Portable;

namespace HelL.Mobile;

public sealed partial class MobileHome : UserControl
{
    private readonly PortableLauncher launcher;
    private readonly PortableCatalog catalog = new();
    private readonly PortableHome lease;
    private readonly string root;
    private IReadOnlyList<LibraryInstance> instances = [];
    private CancellationTokenSource? operation;
    private string selected = "";
    private string reference = "";
    private CatalogProject? project;
    private bool original;
    private bool ready;
    private string SelectedKind => (Kind.SelectedItem as ComboBoxItem)?.Tag as string ?? "resourcepack";
    private string SelectedProvider => (Provider.SelectedItem as ComboBoxItem)?.Tag as string ?? "modrinth";

    public MobileHome() : this(MobileRuntime.Root) { }
    public MobileHome(string directory)
    {
        InitializeComponent();
        root = Path.GetFullPath(directory);
        lease = PortableHome.Open(root);
        launcher = new PortableLauncher(root);
        DataPath.Text = root;
        ready = true;
        Reload();
        ShowPage("library");
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
        string query = LibrarySearch.Text?.Trim() ?? "";
        var visible = instances.Where(row => row.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) || row.Group.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToArray();
        InstancesList.ItemsSource = visible;
        InstancesList.SelectedItem = visible.FirstOrDefault(row => row.Name == selected) ?? visible.FirstOrDefault();
        LibraryNotice.Text = visible.Length == 0 ? "暂无匹配实例。调整搜索或创建新实例。" : $"{visible.Length} 个实例 · 选择后可管理";
        InstanceEditor.IsVisible = InstancesList.SelectedItem != null;
    }

    private void FilterInstances(object? sender, TextChangedEventArgs e) { if (ready) Reload(); }
    private void SelectInstance(object? sender, SelectionChangedEventArgs e)
    {
        if (!ready || InstancesList.SelectedItem is not LibraryInstance entry) return;
        try
        {
            selected = entry.Name;
            launcher.SelectInstance(selected);
            reference = launcher.ReferenceVersion(selected);
            InstanceName.Text = selected;
            InstanceGroup.Text = entry.Group;
            ReferenceVersion.Text = reference;
            PinButton.Content = entry.Pinned ? "取消固定" : "固定实例";
            ResourceTarget.Text = $"{selected} · 参考 {reference}\n仅筛选资源，未安装游戏运行内核。";
            InstanceEditor.IsVisible = true;
            ClearProjects();
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
        Status.Text = "实例已创建，可以查找兼容资源。";
        await Task.CompletedTask;
    });

    private async void SaveInstance(object? sender, RoutedEventArgs e) => await Run(_ =>
    {
        RequireSelection();
        string name = InstanceName.Text?.Trim() ?? "";
        string group = InstanceGroups.Normalize(InstanceGroup.Text ?? "");
        string version = ValidateReference(ReferenceVersion.Text);
        launcher.RenameInstance(selected, name);
        selected = name;
        launcher.GroupInstance(name, group);
        launcher.SetReferenceVersion(name, version);
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
        LibraryPage.IsVisible = page == "library";
        ResourcesPage.IsVisible = page == "resources";
        AboutPage.IsVisible = page == "about";
        foreach (var button in Navigation.Children.OfType<Button>()) button.Classes.Set("current", (string?)button.Tag == page);
    }

    private void RequireSelection()
    { if (selected.Length == 0) throw new InvalidOperationException("请先创建或选择一个实例。"); }
    private static string ValidateReference(string? text)
    {
        string version = text?.Trim() ?? "";
        if (!System.Text.RegularExpressions.Regex.IsMatch(version, @"\A1\.\d+(?:\.\d+)?\z")) throw new ArgumentException("请输入正式版本号，例如 1.21.1。");
        return version;
    }

    private async Task Run(Func<CancellationToken, Task> action)
    {
        if (operation != null) return;
        using var cancellation = new CancellationTokenSource();
        operation = cancellation;
        Pages.IsEnabled = Navigation.IsEnabled = false;
        Progress.IsVisible = CancelButton.IsVisible = true;
        try { await action(cancellation.Token); }
        catch (Exception) when (cancellation.IsCancellationRequested) { Status.Text = "任务已取消。已完成文件保留，可以重试。"; }
        catch (Exception error) { Status.Text = "未完成：" + error.Message; }
        finally
        {
            operation = null;
            Pages.IsEnabled = Navigation.IsEnabled = true;
            Progress.IsVisible = CancelButton.IsVisible = false;
        }
    }
    private void Cancel(object? sender, RoutedEventArgs e) { operation?.Cancel(); Status.Text = "正在取消…"; }
}
