using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using HelL.Portable;

namespace HelL.Desktop.Views;

public partial class InstanceLibraryView : UserControl
{
    private const double NarrowInspectorMinHeight = 220;
    private const double NarrowInspectorMaxHeight = 320;
    private MainWindow? host;
    private IReadOnlyList<LibraryInstance> instances = [];
    private bool refreshing;
    private bool loadFailed;
    private bool cards;
    private bool detailsExpanded = true;
    internal LibraryInstance? Selected => LibraryList.SelectedItem as LibraryInstance;

    public InstanceLibraryView()
    {
        InitializeComponent();
        LibraryList.ItemTemplate = (IDataTemplate)Resources["RowTemplate"]!;
        SizeChanged += (_, _) =>
        {
            ArrangeResponsive();
            if (cards) SetItemsPanel();
        };
    }

    private void ArrangeResponsive()
    {
        bool narrow = Bounds.Width > 0 && Bounds.Width < 740;
        if (narrow)
        {
            WorkspaceSplit.ColumnDefinitions = new ColumnDefinitions("*");
            WorkspaceSplit.RowDefinitions = new RowDefinitions("*,Auto");
            Grid.SetColumn(InspectorCard, 0);
            Grid.SetRow(InspectorCard, 1);
            InspectorCard.Margin = new Avalonia.Thickness(0, 12, 0, 0);
            InspectorCard.MaxHeight = Math.Clamp(Bounds.Height / 2, NarrowInspectorMinHeight, NarrowInspectorMaxHeight);
        }
        else
        {
            WorkspaceSplit.ColumnDefinitions = new ColumnDefinitions("*,290");
            WorkspaceSplit.RowDefinitions = new RowDefinitions("*");
            Grid.SetColumn(InspectorCard, 1);
            Grid.SetRow(InspectorCard, 0);
            InspectorCard.Margin = new Avalonia.Thickness(0);
            InspectorCard.MaxHeight = double.PositiveInfinity;
        }
        ManageButton.IsVisible = narrow;
        ManagementFields.IsVisible = !narrow || detailsExpanded;
    }

    internal void Initialize(MainWindow window)
    {
        host = window;
        cards = HelL.Core.Py.Truthy(HelL.Core.Launcher.Config.CONFIG.Get("portable_library_cards"));
        ApplyView();
        Reload();
    }

    internal void Reload(string? selection = null) => Reload(selection, throwOnFailure: false);

    private void Reload(string? selection, bool throwOnFailure)
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
            loadFailed = false;
            LibraryError.IsVisible = false;
        }
        catch (Exception error)
        {
            loadFailed = true;
            instances = [];
            LibraryList.ItemsSource = Array.Empty<LibraryInstance>();
            Editor.IsEnabled = false;
            EmptyState.IsVisible = false;
            CountLabel.Text = "读取未完成";
            LibraryError.Text = "实例库读取失败：" + error.Message + "\n修复后点击刷新。仍可从左侧选择其他实例。";
            LibraryError.IsVisible = true;
            System.Diagnostics.Trace.TraceError("InstanceLibraryView.Reload: " + error);
            if (throwOnFailure) throw;
            return;
        }
        finally { refreshing = false; }
        Filter(selected);
    }

    private void Filter(string? selected = null)
    {
        if (host == null || refreshing || loadFailed) return;
        string query = SearchBox.Text?.Trim() ?? "";
        string group = GroupFilter.SelectedItem as string ?? "全部分组";
        var visible = instances.Where(row => (group == "全部分组" || row.Group == group) &&
            (PinnedOnly.IsChecked != true || row.Pinned) &&
            (row.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) || row.Group.Contains(query, StringComparison.CurrentCultureIgnoreCase))).ToArray();
        selected ??= Selected?.Name;
        LibraryList.ItemsSource = visible;
        LibraryList.SelectedItem = visible.FirstOrDefault(row => row.Name == selected) ?? visible.FirstOrDefault();
        CountLabel.Text = $"显示 {visible.Length} / 共 {instances.Count} 个实例";
        EmptyState.IsVisible = visible.Length == 0;
        EmptyTitle.Text = instances.Count == 0 ? "还没有实例" : "没有匹配的实例";
        Editor.IsEnabled = Selected != null;
    }

    private void ChooseInstance(object? sender, SelectionChangedEventArgs e)
    {
        if (Selected is not { } entry)
        {
            Editor.IsEnabled = false;
            InspectorCover.IsVisible = false;
            return;
        }
        Editor.IsEnabled = true;
        SelectionTitle.Text = entry.Name;
        SelectionMeta.Text = entry.Detail;
        RenameBox.Text = entry.Name;
        GroupBox.Text = entry.Group;
        PinButton.Content = entry.Pinned ? "取消固定" : "固定实例";
        InspectorCover.Source = entry.Cover;
        InspectorCover.IsVisible = entry.Cover.Length > 0;
    }

    private void FilterChanged(object? sender, TextChangedEventArgs e) => Filter();
    private void GroupChanged(object? sender, SelectionChangedEventArgs e) => Filter();
    private void PinnedChanged(object? sender, RoutedEventArgs e) => Filter();
    private async void RefreshLibrary(object? sender, RoutedEventArgs e) => await Edit(() =>
    {
        Reload(null, throwOnFailure: true);
        host?.SetStatus("实例库已刷新。");
    });
    private void OpenInstance(object? sender, RoutedEventArgs e) { if (Selected is { } entry) host?.OpenInstance(entry.Name); }
    private void JumpToInstaller(object? sender, RoutedEventArgs e) => host?.ShowPage("install");

    private async void QuickPinRow(object? sender, RoutedEventArgs e)
    {
        if (host == null || sender is not Button { Tag: string name }) return;
        e.Handled = true;
        var row = instances.FirstOrDefault(i => i.Name == name);
        if (row != null) await Edit(() => { host.GameLauncher.PinInstance(row.Name, !row.Pinned); Reload(row.Name); });
    }

    private void QuickOpenRow(object? sender, RoutedEventArgs e)
    {
        if (host == null || sender is not Button { Tag: string name }) return;
        e.Handled = true;
        host.OpenInstance(name);
    }

    private void QuickModsRow(object? sender, RoutedEventArgs e)
    {
        if (host == null || sender is not Button { Tag: string name }) return;
        e.Handled = true;
        host.OpenInstanceSubpage(name, "mods");
    }

    private void QuickSavesRow(object? sender, RoutedEventArgs e)
    {
        if (host == null || sender is not Button { Tag: string name }) return;
        e.Handled = true;
        host.OpenInstanceSubpage(name, "saves");
    }

    private void OpenSelectedMods(object? sender, RoutedEventArgs e)
    {
        if (Selected is { } entry) host?.OpenInstanceSubpage(entry.Name, "mods");
    }

    private void OpenSelectedSaves(object? sender, RoutedEventArgs e)
    {
        if (Selected is { } entry) host?.OpenInstanceSubpage(entry.Name, "saves");
    }

    private async void SaveInstance(object? sender, RoutedEventArgs e)
    {
        if (Selected is not { } entry || host == null) return;
        string name = RenameBox.Text?.Trim() ?? "";
        string group = GroupBox.Text ?? "";
        await Edit(() =>
        {
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

    private Task Edit(Action action, [System.Runtime.CompilerServices.CallerMemberName] string operation = "")
        => host?.RunTask(_ => { action(); return Task.CompletedTask; }, operation) ?? Task.CompletedTask;

    private void ToggleDetails(object? sender, RoutedEventArgs e)
    {
        detailsExpanded = !detailsExpanded;
        ManageButton.Content = detailsExpanded ? "收起" : "编辑";
        ArrangeResponsive();
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
        LibraryList.Classes.Set("rows", !cards);
        LibraryList.ItemTemplate = (IDataTemplate)Resources[cards ? "CardTemplate" : "RowTemplate"]!;
        SetItemsPanel();
    }

    private void SetItemsPanel()
    {
        double areaWidth = Math.Max(300, Bounds.Width - (Bounds.Width >= 740 ? 310 : 0));
        double width = Math.Max(210, (areaWidth - 48) / Math.Max(1, (int)((areaWidth - 40) / 230)) - 8);
        LibraryList.ItemsPanel = cards
            ? new FuncTemplate<Panel?>(() => new WrapPanel { ItemWidth = width })
            : new FuncTemplate<Panel?>(() => new VirtualizingStackPanel());
    }
}
