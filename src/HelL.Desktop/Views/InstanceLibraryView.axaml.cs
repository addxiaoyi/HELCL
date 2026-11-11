using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using HelL.Portable;

namespace HelL.Desktop.Views;

public partial class InstanceLibraryView : UserControl
{
    private MainWindow? host;
    private IReadOnlyList<LibraryInstance> instances = [];
    private bool refreshing;
    private bool cards;
    private bool detailsExpanded;
    internal LibraryInstance? Selected => LibraryList.SelectedItem as LibraryInstance;

    public InstanceLibraryView()
    {
        InitializeComponent();
        LibraryList.ItemTemplate = (IDataTemplate)Resources["RowTemplate"]!;
        SizeChanged += (_, _) => { if (cards) SetItemsPanel(); UpdateDetails(); };
    }

    internal void Initialize(MainWindow window)
    {
        host = window;
        cards = HelL.Core.Py.Truthy(HelL.Core.Launcher.Config.CONFIG.Get("portable_library_cards"));
        ApplyView();
        Reload();
    }

    internal void Reload(string? selection = null)
    {
        if (host == null) return;
        string? selected = selection ?? Selected?.Name ?? host.GameLauncher.SelectedInstance;
        string? group = GroupFilter.SelectedItem as string;
        refreshing = true;
        try
        {
            instances = host.GameLauncher.LibraryList();
            GroupFilter.ItemsSource = new[] { "全部分组" }.Concat(instances.Select(row => row.Group).Where(value => value.Length > 0).Distinct().Order());
            GroupFilter.SelectedItem = group;
            if (GroupFilter.SelectedIndex < 0) GroupFilter.SelectedIndex = 0;
        }
        finally { refreshing = false; }
        Filter(selected);
    }

    private void Filter(string? selected = null)
    {
        if (host == null || refreshing) return;
        string query = SearchBox.Text?.Trim() ?? "";
        string group = GroupFilter.SelectedItem as string ?? "全部分组";
        var visible = instances.Where(row => (group == "全部分组" || row.Group == group) &&
            (PinnedOnly.IsChecked != true || row.Pinned) &&
            (row.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) || row.Group.Contains(query, StringComparison.CurrentCultureIgnoreCase))).ToArray();
        selected ??= Selected?.Name;
        LibraryList.ItemsSource = visible;
        LibraryList.SelectedItem = visible.FirstOrDefault(row => row.Name == selected) ?? visible.FirstOrDefault();
        CountLabel.Text = $"{visible.Length} / {instances.Count} 个实例";
        EmptyState.IsVisible = visible.Length == 0;
        EmptyTitle.Text = instances.Count == 0 ? "还没有实例" : "没有匹配的实例";
        Editor.IsEnabled = Selected != null;
    }

    private void ChooseInstance(object? sender, SelectionChangedEventArgs e)
    {
        if (Selected is not { } entry) { Editor.IsEnabled = false; return; }
        Editor.IsEnabled = true;
        SelectionTitle.Text = entry.Name;
        RenameBox.Text = entry.Name;
        GroupBox.Text = entry.Group;
        PinButton.Content = entry.Pinned ? "取消固定" : "固定实例";
    }

    private void FilterChanged(object? sender, TextChangedEventArgs e) => Filter();
    private void GroupChanged(object? sender, SelectionChangedEventArgs e) => Filter();
    private void PinnedChanged(object? sender, RoutedEventArgs e) => Filter();
    private async void RefreshLibrary(object? sender, RoutedEventArgs e) => await Edit(() => Reload());
    private void OpenInstance(object? sender, RoutedEventArgs e) { if (Selected is { } entry) host?.OpenInstance(entry.Name); }

    private async void SaveInstance(object? sender, RoutedEventArgs e)
    {
        if (Selected is not { } entry || host == null) return;
        string name = RenameBox.Text?.Trim() ?? "";
        string group = GroupBox.Text ?? "";
        await Edit(() =>
        {
            // Validate the group before renaming so an invalid group cannot leave a partial edit.
            HelL.Core.Launcher.InstanceGroups.Normalize(group);
            host.GameLauncher.RenameInstance(entry.Name, name);
            host.GameLauncher.GroupInstance(name, group);
            host.InstancesChanged(name);
            Reload(name);
            host.SetStatus("实例名称与分组已保存。");
        });
    }

    private async void PinInstance(object? sender, RoutedEventArgs e)
    {
        if (Selected is not { } entry || host == null) return;
        await Edit(() => { host.GameLauncher.PinInstance(entry.Name, !entry.Pinned); Reload(entry.Name); });
    }

    private async void ResetCover(object? sender, RoutedEventArgs e)
    {
        if (Selected is not { } entry || host == null) return;
        await Edit(() => { host.GameLauncher.ResetInstanceCover(entry.Name); Reload(entry.Name); });
    }

    private async void PickCover(object? sender, RoutedEventArgs e)
    {
        if (Selected is not { } entry || host == null) return;
        await host.RunTask(async token =>
        {
            var files = await host.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择实例封面", AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("PNG / JPEG 图片") { Patterns = ["*.png", "*.jpg", "*.jpeg"] }]
            });
            if (files.Count == 0) return;
            string file = files[0].TryGetLocalPath() ?? throw new IOException("请选择本机图片。");
            await Task.Run(() => host.GameLauncher.SetInstanceCover(entry.Name, file), token);
            Reload(entry.Name);
        });
    }

    private async void OpenFolder(object? sender, RoutedEventArgs e)
    {
        if (Selected is not { } entry || host == null) return;
        await host.RunTask(async _ =>
        {
            var folder = await host.StorageProvider.TryGetFolderFromPathAsync(entry.Folder) ?? throw new IOException("实例目录已不存在。");
            if (!await host.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(folder.TryGetLocalPath()!)))
                throw new IOException("无法打开文件管理器，请手动打开实例目录。");
        });
    }

    private Task Edit(Action action) => host?.RunTask(_ => { action(); return Task.CompletedTask; }) ?? Task.CompletedTask;
    private void ToggleDetails(object? sender, RoutedEventArgs e) { detailsExpanded = !detailsExpanded; UpdateDetails(); }
    private void UpdateDetails()
    {
        bool compact = Bounds.Height < 580 || Bounds.Width < 680;
        ManageButton.IsVisible = compact;
        ManageButton.Content = detailsExpanded ? "收起" : "管理";
        ManagementFields.IsVisible = !compact || detailsExpanded;
    }
    private void SwitchView(object? sender, RoutedEventArgs e)
    {
        cards = !cards;
        try
        {
            HelL.Core.Launcher.Config.CONFIG.Set("portable_library_cards", cards);
            HelL.Core.Launcher.Config.CONFIG.Save();
        }
        catch (Exception error) { host?.SetStatus("视图偏好未保存：" + error.Message); }
        ApplyView();
    }
    private void ApplyView()
    {
        ViewButton.Content = cards ? "紧凑列表" : "封面视图";
        LibraryList.ItemTemplate = (IDataTemplate)Resources[cards ? "CardTemplate" : "RowTemplate"]!;
        SetItemsPanel();
    }
    private void SetItemsPanel()
    {
        double width = Math.Max(220, (Bounds.Width - 64) / Math.Max(1, (int)((Bounds.Width - 56) / 248)) - 8);
        LibraryList.ItemsPanel = cards
            ? new FuncTemplate<Panel?>(() => new WrapPanel { ItemWidth = width })
            : new FuncTemplate<Panel?>(() => new VirtualizingStackPanel());
    }
}
