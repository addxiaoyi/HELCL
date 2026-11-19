using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using HelL.Portable;

namespace HelL.Mobile;

public sealed partial class MobileHome
{
    private string versionFilter = "all";
    private bool wide;
    private bool loadingLibrary;
    private IReadOnlyList<CatalogProject> searchResults = [];
    private string resourceOrder = "relevance";
    private int coverRevision;

    private sealed record InstanceRow(string Name, string Detail, bool Current);

    private void ArrangeWorkspace(object? sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width <= 0) return;
        double width = e.NewSize.Width;
        wide = width >= 760;
        bool fullSidebar = width >= 1000;
        bool phone = width < 760;
        double railWidth = phone ? 0 : fullSidebar ? 188 : 60;
        NavigationRail.IsVisible = !phone;
        BottomBar.IsVisible = phone;
        Shell.ColumnDefinitions[0].Width = new GridLength(railWidth);
        RailCaption.IsVisible = fullSidebar;
        if (RailAboutLabel != null) RailAboutLabel.IsVisible = fullSidebar;
        NavigationRail.Padding = fullSidebar ? new Thickness(10, 12) : new Thickness(6, 10);
        foreach (Button button in RailNavigation.Children.OfType<Button>())
        {
            button.MinHeight = 44;
            button.Padding = fullSidebar ? new Thickness(11, 6) : new Thickness(9, 7);
            foreach (TextBlock label in button.GetLogicalDescendants().OfType<TextBlock>())
                label.IsVisible = fullSidebar;
        }
        double contentWidth = width - railWidth;
        bool compact = contentWidth < 350;
        LibraryTopCreateButton.IsVisible = wide;
        LibraryCreateLabel.IsVisible = !compact;
        LibrarySubtitle.IsVisible = ResourcesSubtitle.IsVisible = contentWidth >= 340;
        var margin = new Thickness(compact ? 14 : wide ? 36 : 20, 18, compact ? 14 : wide ? 36 : 20, 24);
        HomeLayout.Margin = margin;
        HomeHeader.IsVisible = !phone;
        HomeHeader.Height = 0;
        HomeLayout.RowSpacing = 0;
        HomeBoard.ColumnDefinitions = new ColumnDefinitions(wide ? "1.15*,*" : "*");
        HomeBoard.RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto");
        HomeBoard.RowSpacing = 16;
        Grid.SetRow(InstanceContent, wide ? 0 : 1);
        Grid.SetColumn(InstanceContent, wide ? 1 : 0);
        Grid.SetRowSpan(InstanceContent, wide ? 2 : 1);
        Grid.SetRow(HomeLibrary, wide ? 1 : 2);
        Grid.SetRow(QuickActions, wide ? 2 : 3);
        double titleSize = compact ? 22 : 26;
        originalFontSizes.Remove(HomeGameTitle);
        originalFontSizes.Add(HomeGameTitle, new FontSizeSnapshot(titleSize));
        HomeGameTitle.FontSize = titleSize * EffectiveTextScale;
        ResourcesLayout.Margin = margin;
        LibraryLayout.Margin = new Thickness(margin.Left, margin.Top, margin.Right, 84);
        LibraryAddDock.Margin = new Thickness(0, 0, margin.Right, 18);
        ReferenceFields.ColumnDefinitions = new ColumnDefinitions(compact ? "*" : "*,*");
        ReferenceFields.RowDefinitions = new RowDefinitions(compact ? "Auto,Auto" : "Auto");
        ReferenceFields.RowSpacing = compact ? 10 : 0;
        Grid.SetRow(ReferenceFields.Children[1], compact ? 1 : 0);
        Grid.SetColumn(ReferenceFields.Children[1], compact ? 0 : 1);
        ArrangeTextLayout();
    }

    private void ArrangeTextLayout()
    {
        bool stacked = !wide && EffectiveTextScale >= 1.5;
        InstancePickerLayout.ColumnDefinitions = new ColumnDefinitions(stacked ? "*,16" : "*,Auto,16");
        InstancePickerLayout.RowDefinitions = new RowDefinitions(stacked ? "Auto,Auto" : "Auto");
        InstancePickerLayout.RowSpacing = stacked ? 4 : 0;
        Grid.SetRow(InstancePickerDetails, stacked ? 1 : 0);
        Grid.SetColumn(InstancePickerDetails, stacked ? 0 : 1);
        InstancePickerDetails.HorizontalAlignment = stacked ? Avalonia.Layout.HorizontalAlignment.Stretch : Avalonia.Layout.HorizontalAlignment.Right;
        SelectedVersionDetail.HorizontalAlignment = SelectedVersion.HorizontalAlignment = stacked
            ? Avalonia.Layout.HorizontalAlignment.Left : Avalonia.Layout.HorizontalAlignment.Right;
        Grid.SetColumn(InstancePickerChevron, stacked ? 1 : 2);
        Grid.SetRowSpan(InstancePickerChevron, stacked ? 2 : 1);
        HomeAccountLayout.ColumnDefinitions = new ColumnDefinitions(stacked ? "*,16" : "*,Auto,16");
        HomeAccountLayout.RowDefinitions = new RowDefinitions(stacked ? "Auto,Auto" : "Auto");
        HomeAccountLayout.RowSpacing = stacked ? 4 : 0;
        Grid.SetRow(HomeAccountLabel, stacked ? 1 : 0);
        Grid.SetColumn(HomeAccountLabel, stacked ? 0 : 1);
        Grid.SetColumn(HomeAccountChevron, stacked ? 1 : 2);
        Grid.SetRowSpan(HomeAccountChevron, stacked ? 2 : 1);
        foreach (Button button in BottomNavigation.Children.OfType<Button>())
        {
            button.Padding = EffectiveTextScale >= 1.5 ? new Thickness(2, 4) : new Thickness(6, 4);
            button.Margin = EffectiveTextScale >= 1.5 ? new Thickness(0, 2) : new Thickness(4, 2);
            foreach (TextBlock label in button.GetLogicalDescendants().OfType<TextBlock>())
            {
                label.Classes.Add("single-line");
                label.TextWrapping = Avalonia.Media.TextWrapping.NoWrap;
            }
        }
    }

    private void UpdateHome(LibraryInstance? entry)
    {
        bool hasInstance = entry != null;
        string rawLoader = hasInstance ? launcher.ReferenceLoader(entry!.Name) : "vanilla";
        string version = hasInstance ? launcher.ReferenceVersion(entry!.Name) : "";
        string loader = hasInstance ? LoaderName(rawLoader) : "未配置";
        SelectedVersion.Text = version.Length > 0 ? $"{version} · {loader}" : "";
        SelectedVersionDetail.Text = entry?.Name ?? "新建实例";
        HomeGameTitle.Text = hasInstance ? entry!.Name : "HelL";
        HomeGameSubtitle.Text = version.Length > 0 ? $"Minecraft {version} · {loader}" : "轻点下方新建第一个实例";
        InstancePicker.Tag = hasInstance ? "library" : "create";
        HomeEmptyAction.IsVisible = !hasInstance;
        HomeInstances.ItemsSource = instances.Take(4).Select(item =>
        {
            string l = launcher.ReferenceLoader(item.Name);
            return new InstanceRow(item.Name,
                launcher.ReferenceVersion(item.Name) + " · " + LoaderName(l),
                item.Name == selected);
        }).ToArray();
        string cover = entry?.Cover ?? "";
        if (HomeCover.Source == cover) HomeCover.Source = null;
        HomeCover.Source = cover;
        HomeCover.IsVisible = false;
        int revision = ++coverRevision;
        if (cover.Length > 0 && File.Exists(cover)) _ = RevealCover(revision);
    }

    private async Task RevealCover(int revision)
    {
        DateTime timeout = DateTime.UtcNow.AddSeconds(6);
        while (revision == coverRevision && !HomeCover.ImageLoaded && DateTime.UtcNow < timeout)
            await Task.Delay(50);
        if (revision == coverRevision) HomeCover.IsVisible = HomeCover.ImageLoaded;
    }

    private static string LoaderName(string loader) => loader == "vanilla" ? "原版" : loader.Length == 0 ? "未指定" : char.ToUpperInvariant(loader[0]) + loader[1..];

    private void PresentInstances()
    {
        string query = LibrarySearch.Text?.Trim() ?? "";
        var matching = instances.Where(entry => entry.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || entry.Group.Contains(query, StringComparison.CurrentCultureIgnoreCase));
        if (versionFilter == "pinned") matching = matching.Where(entry => entry.Pinned);
        else if (versionFilter != "all") matching = matching.Where(entry => launcher.ReferenceLoader(entry.Name) == versionFilter);
        var rows = matching.Select(entry =>
        {
            string loader = launcher.ReferenceLoader(entry.Name);
            string version = launcher.ReferenceVersion(entry.Name);
            string detail = string.Join(" · ", new[] { version, LoaderName(loader), entry.Group }.Where(value => value.Length > 0));
            return new InstanceRow(entry.Name, detail, entry.Name == selected);
        }).ToArray();
        loadingLibrary = true;
        try
        {
            InstancesList.ItemsSource = rows;
            InstancesList.SelectedItem = rows.FirstOrDefault(row => row.Current);
        }
        finally { loadingLibrary = false; }
        LibraryNotice.IsVisible = rows.Length == 0;
        LibraryNotice.Text = instances.Count == 0 ? "暂无实例" : "无匹配结果";
        foreach (Button button in VersionFilters.Children.OfType<Button>()) button.Classes.Set("current", (string?)button.Tag == versionFilter);
    }

    private void FilterVersion(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string filter }) return;
        versionFilter = filter;
        PresentInstances();
    }

    private async void OpenCreate(object? sender, RoutedEventArgs e) => await NavigateCheckedAsync("create");
    private async void OpenManage(object? sender, RoutedEventArgs e)
    {
        if (selected.Length == 0) { await NavigateCheckedAsync("create"); return; }
        await NavigateCheckedAsync("manage");
    }

    private void ManageRow(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string name }) return;
        Reload(name);
        ShowPage("manage");
        e.Handled = true;
    }

    private void ExplainRuntime(object? sender, RoutedEventArgs e) =>
        Status.Text = "Android 运行层持续接入中。";
    private async void OpenNotifications(object? sender, RoutedEventArgs e) => await NavigateCheckedAsync("tasks");
    private void OpenServers(object? sender, RoutedEventArgs e) =>
        Status.Text = "暂无服务器。";
    private void OpenPackImport(object? sender, RoutedEventArgs e)
    {
        var choice = Kind.Items.OfType<ComboBoxItem>().Single(item => (string?)item.Tag == "modpack");
        Kind.SelectedItem = choice;
        ShowPage("resources");
        SearchFilters.IsVisible = true;
        Status.Text = "可搜索并下载整合包文件。";
    }

    private void ToggleSearch(object? sender, RoutedEventArgs e)
    {
        SearchFilters.IsVisible = !SearchFilters.IsVisible;
        if (SearchFilters.IsVisible) Query.Focus();
    }

    private void FocusSearch(object? sender, RoutedEventArgs e)
    {
        SearchPanel.IsVisible = true;
        DetailsPanel.IsVisible = false;
        SearchFilters.IsVisible = true;
        Query.Focus();
        Query.BringIntoView();
    }

    private void DismissStatus(object? sender, RoutedEventArgs e)
    {
        if (operation == null) Status.Text = "";
    }

    private void SearchKey(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key != Avalonia.Input.Key.Enter) return;
        e.Handled = true;
        SearchResources(sender, new RoutedEventArgs());
    }

    private void SelectCategory(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string kind }) return;
        Kind.SelectedItem = Kind.Items.OfType<ComboBoxItem>().Single(item => (string?)item.Tag == kind);
        ShowPage("resources");
        SyncCategory();
        if (!MobileRuntime.Smoke) SearchResources(sender, e);
    }

    private void SyncCategory()
    {
        foreach (Button button in ResourceCategories.Children.OfType<Button>())
            button.Classes.Set("current", (string?)button.Tag == SelectedKind);
    }

    private void SortResources(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string order }) return;
        resourceOrder = order;
        InvalidateResourceSelection();
        foreach (Button button in ResourceSortTabs.Children.OfType<Button>()) button.Classes.Set("current", (string?)button.Tag == order);
        Projects.ItemsSource = PortableCatalog.OrderProjects(searchResults, order);
        if (!MobileRuntime.Smoke) SearchResources(sender, e);
    }

    private async void OpenProjectRow(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: CatalogProject entry }) return;
        if (!CanOpenProject(entry)) return;
        Projects.SelectedItem = entry;
        PresentProject(entry);
        await LoadReleases();
        e.Handled = true;
    }
}
