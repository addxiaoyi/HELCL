using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;

namespace HelL.Services;

public enum FilePickerMode { Open, OpenMany, Save, Folder }

public sealed class FilePickerWindow : Window
{
    private sealed class Entry
    {
        public string Path { get; }
        public string Name => System.IO.Path.GetFileName(Path.TrimEnd(System.IO.Path.DirectorySeparatorChar));
        public bool IsDirectory { get; }
        public string Glyph => IsDirectory ? "\ue8b7" : "\ue8a5";
        public string Kind => IsDirectory ? I18n.L("文件夹") : System.IO.Path.GetExtension(Name).TrimStart('.').ToUpperInvariant();
        public Entry(string path, bool directory) { Path = path; IsDirectory = directory; }
    }

    private readonly FilePickerMode _mode;
    private readonly TextBox _path = Ui.Input(I18n.L("输入文件夹路径，然后按 Enter"));
    private readonly TextBox _search = Ui.Input(I18n.L("筛选当前文件夹"));
    private readonly TextBox _name = Ui.Input(I18n.L("文件名"));
    private readonly ComboBox _filter = Ui.Combo();
    private readonly ListBox _files = new ListBox { Style = Ui.S("PlainList") };
    private readonly TextBlock _status = Ui.Muted("");
    private readonly TextBlock _error = Ui.Txt("", 12, fg: "B.Danger").Wrap();
    private readonly ProgressBar _loading = Ui.Prog();
    private readonly Button _accept;
    private readonly Button _back;
    private readonly Button _up;
    private readonly Grid _overlays = new Grid { Visibility = Visibility.Collapsed };
    private readonly Stack<string> _history = new Stack<string>();
    private readonly IReadOnlyList<FileTypeFilter> _filters;
    private List<Entry> _entries = new List<Entry>();
    private CancellationTokenSource? _browseCancellation;
    private string _directory;
    private int _browseVersion;
    private bool _browsing;
    private bool _closed;
    private bool _selecting;
    private Action? _cancelOverlay;

    public string[]? SelectedPaths { get; private set; }

    public FilePickerWindow(FilePickerMode mode, string title, string filter = "", string filename = "", string? directory = null)
    {
        _mode = mode;
        _filters = FilePickerRules.ParseFilters(filter);
        _directory = directory ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrEmpty(_directory)) _directory = Environment.CurrentDirectory;
        if (Path.IsPathRooted(filename))
        {
            _directory = Path.GetDirectoryName(filename) ?? _directory;
            filename = Path.GetFileName(filename);
        }
        Title = title;
        Width = 820;
        Height = 600;
        MinWidth = 660;
        MinHeight = 460;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        UseLayoutRounding = true;
        SetResourceReference(FontFamilyProperty, "F.Ui");
        FontSize = UiMetrics.BodySize;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
        SetResourceReference(BackgroundProperty, "B.Paper");
        SetResourceReference(ForegroundProperty, "B.Ink");
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0) });
        _accept = Ui.Btn(mode == FilePickerMode.Save ? I18n.L("保存") : mode == FilePickerMode.Folder ? I18n.L("选择此文件夹") : I18n.L("打开"), BtnKind.Primary, Accept);
        _back = Ui.IconBtn("\ue72b", I18n.L("返回上一文件夹"), Back);
        _up = Ui.IconBtn("\ue74a", I18n.L("上一级文件夹"), Up);
        _name.Text = filename;
        _filter.ItemsSource = _filters;
        _filter.SelectedIndex = 0;
        _files.SelectionMode = mode == FilePickerMode.OpenMany ? SelectionMode.Extended : SelectionMode.Single;
        _files.ItemTemplate = EntryTemplate();
        var rowStyle = new Style(typeof(ListBoxItem), Ui.S("ListItemRow"));
        rowStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 10, 12, 10)));
        rowStyle.Setters.Add(new Setter(AutomationProperties.NameProperty, new Binding("Name")));
        rowStyle.Setters.Add(new Setter(ToolTipProperty, new Binding("Path")));
        _files.ItemContainerStyle = rowStyle;
        _error.Visibility = Visibility.Collapsed;
        _loading.Height = 3;
        _loading.IsIndeterminate = true;
        _filter.Visibility = mode == FilePickerMode.Folder ? Visibility.Collapsed : Visibility.Visible;
        _name.Visibility = mode == FilePickerMode.Folder ? Visibility.Collapsed : Visibility.Visible;

        Content = BuildContent(title);
        AutomationProperties.SetName(_path, I18n.L("文件夹路径"));
        AutomationProperties.SetName(_files, I18n.L("文件列表"));
        AutomationProperties.SetName(_search, I18n.L("筛选当前文件夹"));
        AutomationProperties.SetName(_name, I18n.L("文件名"));
        AutomationProperties.SetName(_filter, I18n.L("文件类型"));
        _path.KeyDown += PathKey;
        _search.TextChanged += (_, _) => RenderEntries();
        _filter.SelectionChanged += (_, _) => RenderEntries();
        _files.SelectionChanged += SelectEntry;
        _files.MouseDoubleClick += OpenEntry;
        _files.KeyDown += FileKey;
        _name.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Accept(this, e); } };
        PreviewKeyDown += WindowKey;
        Loaded += async (_, _) => await BrowseAsync(_directory, false);
        Closed += (_, _) =>
        {
            _closed = true;
            _cancelOverlay?.Invoke();
            _browseVersion++;
            _browseCancellation?.Cancel();
            _browseCancellation?.Dispose();
        };
    }

    private UIElement BuildContent(string title)
    {
        var header = Ui.G(null, "*,Auto");
        header.Add(Ui.H2(title).VCenter(), 0);
        header.Add(Ui.IconBtn("\ue711", I18n.L("关闭"), (_, _) => Close()), 0, 1);
        header.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 1 && !Ui.FromButton(e.OriginalSource as DependencyObject)) DragMove(); };

        var navigation = Ui.G(null, "Auto,Auto,*,Auto,Auto");
        navigation.Add(_back.M(0, 0, 6, 0), 0);
        navigation.Add(_up.M(0, 0, 10, 0), 0, 1);
        navigation.Add(_path, 0, 2);
        navigation.Add(Ui.IconBtn("\ue72c", I18n.L("刷新文件夹"), async (_, _) => await BrowseAsync(_directory, false)).M(8, 0, 0, 0), 0, 3);
        navigation.Add(Ui.IconBtn("\ue8f4", I18n.L("新建文件夹"), (_, _) => NewFolder()).M(6, 0, 0, 0), 0, 4);

        var places = Ui.V(2, Ui.Small(I18n.L("位置")).M(10, 0, 0, 6)).M(0, 0, 4, 14);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Place(string label, string path, string glyph)
        {
            if (string.IsNullOrWhiteSpace(path) || !seen.Add(path)) return;
            Button shortcut = Ui.Btn(label, BtnKind.Ghost, async (_, _) => await BrowseAsync(path), glyph);
            shortcut.MinHeight = 32;
            shortcut.Padding = new Thickness(10, 4, 8, 4);
            shortcut.HorizontalContentAlignment = HorizontalAlignment.Left;
            shortcut.ToolTip = path;
            places.Children.Add(shortcut);
        }
        Place(I18n.L("桌面"), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "\ue8fc");
        Place(I18n.L("文档"), Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "\ue8a5");
        Place(I18n.L("下载"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"), "\ue896");
        Place(I18n.L("用户目录"), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "\ue77b");
        places.Children.Add(Ui.Sep().M(6, 6, 6, 6));
        foreach (DriveInfo drive in DriveInfo.GetDrives()) Place(drive.Name, drive.Name, "\ueda2");

        var content = Ui.G("Auto,Auto,*");
        content.Add(_search.M(0, 0, 0, 10), 0);
        content.Add(_loading, 1);
        content.Add(_files, 2);
        var browser = Ui.G(null, "144,20,*");
        browser.Add(Ui.Scroll(places), 0);
        browser.Add(content, 0, 2);

        var footer = Ui.V(8, _error,
            _mode == FilePickerMode.Folder ? null : Ui.Field(I18n.L("文件名"), _name, labelWidth: 72),
            _mode == FilePickerMode.Folder ? null : Ui.Field(I18n.L("文件类型"), _filter, labelWidth: 72));
        Button cancel = Ui.Btn(I18n.L("取消"), BtnKind.Normal, (_, _) => Close());
        var actions = Ui.G(null, "*,Auto");
        actions.Add(_status.VCenter(), 0);
        actions.Add(Ui.H(8, cancel, _accept), 0, 1);
        footer.Children.Add(actions.M(0, 4, 0, 0));

        var layout = Ui.G("Auto,Auto,*,Auto");
        layout.Add(header.M(0, 0, 0, 18), 0);
        layout.Add(navigation.M(0, 0, 0, 16), 1);
        layout.Add(browser, 2);
        layout.Add(footer.M(0, 12, 0, 0), 3);
        var root = new Grid();
        root.Children.Add(new Border { Child = layout, Padding = new Thickness(24), BorderThickness = new Thickness(1), BorderBrush = Ui.Res("B.Line") });
        root.Children.Add(_overlays);
        return root;
    }

    private async Task BrowseAsync(string path, bool remember = true)
    {
        int version = ++_browseVersion;
        _browseCancellation?.Cancel();
        _browseCancellation?.Dispose();
        _browseCancellation = new CancellationTokenSource();
        CancellationToken cancellation = _browseCancellation.Token;
        _browsing = true;
        _accept.IsEnabled = _files.IsEnabled = false;
        _loading.Visibility = Visibility.Visible;
        _status.Text = I18n.L("正在读取文件夹…");
        _error.Visibility = Visibility.Collapsed;
        try
        {
            string full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')));
            List<Entry> entries = await Task.Run(() => ReadDirectory(full, cancellation), cancellation);
            if (_closed || version != _browseVersion) return;
            if (remember && !string.Equals(full, _directory, StringComparison.OrdinalIgnoreCase)) _history.Push(_directory);
            _directory = full;
            _path.Text = full;
            _entries = entries;
            _files.UnselectAll();
            if (_mode != FilePickerMode.Save) _name.Clear();
            _search.Clear();
            RenderEntries();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException || ex is System.Security.SecurityException)
        {
            if (_closed || version != _browseVersion) return;
            _entries.Clear();
            _files.ItemsSource = null;
            _status.Text = I18n.L("无法读取文件夹");
            ShowError(I18n.L("无法打开这个位置，请检查路径或访问权限。{0}", ex.Message));
        }
        finally
        {
            if (!_closed && version == _browseVersion)
            {
                _browsing = false;
                _loading.Visibility = Visibility.Collapsed;
                _accept.IsEnabled = _error.Visibility != Visibility.Visible;
                _files.IsEnabled = true;
                _back.IsEnabled = _history.Count > 0;
                _up.IsEnabled = Directory.GetParent(_directory) != null;
            }
        }
    }

    private List<Entry> ReadDirectory(string directory, CancellationToken cancellation)
    {
        var entries = new List<Entry>();
        foreach (string path in Directory.EnumerateDirectories(directory))
        {
            cancellation.ThrowIfCancellationRequested();
            entries.Add(new Entry(path, true));
        }
        if (_mode != FilePickerMode.Folder)
        {
            foreach (string path in Directory.EnumerateFiles(directory))
            {
                cancellation.ThrowIfCancellationRequested();
                entries.Add(new Entry(path, false));
            }
        }
        return entries.OrderByDescending(entry => entry.IsDirectory).ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private void RenderEntries()
    {
        FileTypeFilter filter = _filter.SelectedItem as FileTypeFilter ?? _filters[0];
        string search = _search.Text.Trim();
        _files.ItemsSource = _entries.Where(entry => (entry.IsDirectory || filter.Matches(entry.Name))
            && entry.Name.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0).ToList();
        _status.Text = _files.Items.Count == 0 ? I18n.L("没有匹配的文件或文件夹") : I18n.L("{0} 项", _files.Items.Count);
    }

    private static DataTemplate EntryTemplate()
    {
        var row = new FrameworkElementFactory(typeof(DockPanel));
        var icon = new FrameworkElementFactory(typeof(TextBlock));
        icon.SetBinding(TextBlock.TextProperty, new Binding("Glyph"));
        icon.SetValue(TextBlock.FontFamilyProperty, Ui.GlyphFont);
        icon.SetValue(TextBlock.FontSizeProperty, 16.0);
        icon.SetValue(WidthProperty, 28.0);
        icon.SetResourceReference(TextBlock.ForegroundProperty, "B.AccentText");
        row.AppendChild(icon);
        var kind = new FrameworkElementFactory(typeof(TextBlock));
        kind.SetValue(DockPanel.DockProperty, Dock.Right);
        kind.SetValue(MarginProperty, new Thickness(12, 0, 0, 0));
        kind.SetBinding(TextBlock.TextProperty, new Binding("Kind"));
        kind.SetResourceReference(TextBlock.ForegroundProperty, "B.InkMuted");
        row.AppendChild(kind);
        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty, new Binding("Name"));
        name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        name.SetResourceReference(TextBlock.ForegroundProperty, "B.Ink");
        row.AppendChild(name);
        return new DataTemplate { VisualTree = row };
    }

    private void SelectEntry(object sender, SelectionChangedEventArgs e)
    {
        List<Entry> selected = Selection();
        if (selected.Count == 1 && !selected[0].IsDirectory) _name.Text = selected[0].Name;
        else if (selected.Count > 1) _name.Clear();
        if (selected.Count > 1) _status.Text = I18n.L("已选择 {0} 项", selected.Count);
    }

    private List<Entry> Selection() => _files.SelectedItems.Cast<Entry>().ToList();
    private void PathKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        BrowseFromPath();
    }
    private async void BrowseFromPath() => await BrowseAsync(_path.Text);
    private async void Back(object sender, RoutedEventArgs e)
    {
        if (_history.Count > 0) await BrowseAsync(_history.Pop(), false);
    }
    private async void Up(object sender, RoutedEventArgs e)
    {
        string? parent = Directory.GetParent(_directory)?.FullName;
        if (parent != null) await BrowseAsync(parent);
    }
    private void WindowKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (_cancelOverlay != null) _cancelOverlay(); else Close();
        }
        else if (_cancelOverlay != null) return;
        else if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            _path.Focus();
            _path.SelectAll();
        }
        else if (e.Key == Key.F5) { e.Handled = true; BrowseFromPath(); }
        else if (e.SystemKey == Key.Up && Keyboard.Modifiers == ModifierKeys.Alt) { e.Handled = true; Up(sender, e); }
        else if (e.SystemKey == Key.Left && Keyboard.Modifiers == ModifierKeys.Alt) { e.Handled = true; Back(sender, e); }
    }
    private void FileKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        ActivateSelection();
    }
    private void OpenEntry(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(_files, e.OriginalSource as DependencyObject) == null) return;
        e.Handled = true;
        ActivateSelection();
    }
    private async void ActivateSelection()
    {
        List<Entry> selected = Selection();
        if (selected.Count == 1 && selected[0].IsDirectory) await BrowseAsync(selected[0].Path);
        else Accept(this, new RoutedEventArgs());
    }

    private async void Accept(object sender, RoutedEventArgs e)
    {
        if (_browsing || _selecting || _closed || !_accept.IsEnabled) return;
        _selecting = true;
        try
        {
            List<Entry> selected = Selection();
            if (_mode == FilePickerMode.Folder)
            {
                string folder = selected.Count == 1 ? selected[0].Path : _directory;
                if (!Directory.Exists(folder)) throw new IOException(I18n.L("这个文件夹已不存在，请刷新后重试"));
                Finish(new[] { folder });
                return;
            }
            if (_mode != FilePickerMode.Save && selected.Count == 1 && selected[0].IsDirectory)
            {
                await BrowseAsync(selected[0].Path);
                return;
            }
            if (_mode == FilePickerMode.Save)
            {
                string target = FilePickerRules.SavePath(_directory, _name.Text, (FileTypeFilter)_filter.SelectedItem);
                if (Directory.Exists(target)) throw new IOException(I18n.L("已存在同名文件夹，请换一个文件名"));
                if (File.Exists(target) && !await ConfirmOverwrite(target)) return;
                Finish(new[] { target });
                return;
            }
            string[] paths = _mode == FilePickerMode.OpenMany && selected.Count > 1
                ? selected.Where(entry => !entry.IsDirectory).Select(entry => entry.Path).ToArray()
                : new[] { Path.Combine(_directory, FilePickerRules.ValidateName(_name.Text)) };
            if (paths.Length == 0) throw new IOException(I18n.L("请选择至少一个文件"));
            if (paths.Any(path => !File.Exists(path))) throw new IOException(I18n.L("所选文件已不存在，请刷新后重新选择"));
            var filter = (FileTypeFilter)_filter.SelectedItem;
            if (paths.Any(path => !filter.Matches(Path.GetFileName(path)))) throw new IOException(I18n.L("文件类型不匹配，请调整筛选条件"));
            Finish(paths);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
        {
            ShowError(ex.Message);
            _name.Focus();
        }
        finally { _selecting = false; }
    }

    private Task<bool> ConfirmOverwrite(string path)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var mask = new Border { Background = Ui.Res("B.Mask") };
        Button cancel = Ui.Btn(I18n.L("取消"));
        Button replace = Ui.Btn(I18n.L("替换文件"), BtnKind.Danger);
        var card = Ui.Card(Ui.V(16, Ui.H2(I18n.L("文件已存在")),
            Ui.Muted(I18n.L("“{0}”已存在。保存后将替换原文件。", Path.GetFileName(path))), Ui.H(8, cancel, replace).Right()), 24);
        card.MaxWidth = 430;
        card.Margin = new Thickness(24);
        card.HorizontalAlignment = HorizontalAlignment.Center;
        card.VerticalAlignment = VerticalAlignment.Center;
        _overlays.Children.Add(mask);
        _overlays.Children.Add(card);
        _overlays.Visibility = Visibility.Visible;
        KeyboardNavigation.SetTabNavigation(card, KeyboardNavigationMode.Cycle);
        void Complete(bool overwrite)
        {
            _overlays.Children.Clear();
            _overlays.Visibility = Visibility.Collapsed;
            _cancelOverlay = null;
            _name.Focus();
            completion.TrySetResult(overwrite);
        }
        _cancelOverlay = () => Complete(false);
        cancel.Click += (_, _) => Complete(false);
        replace.Click += (_, _) => Complete(true);
        mask.MouseLeftButtonDown += (_, _) => Complete(false);
        card.Loaded += (_, _) => cancel.Focus();
        return completion.Task;
    }

    private void ShowError(string message)
    {
        _error.Text = message;
        _error.Visibility = Visibility.Visible;
    }

    private void NewFolder()
    {
        if (_browsing || _cancelOverlay != null) return;
        var name = Ui.Input(I18n.L("文件夹名称"));
        var error = Ui.Txt("", 12, fg: "B.Danger").Wrap();
        error.Visibility = Visibility.Collapsed;
        Button cancel = Ui.Btn(I18n.L("取消"));
        Button create = Ui.Btn(I18n.L("创建"), BtnKind.Primary);
        bool creating = false;
        var card = Ui.Card(Ui.V(16, Ui.H2(I18n.L("新建文件夹")), Ui.Field(I18n.L("名称"), name, labelWidth: 60), error, Ui.H(8, cancel, create).Right()), 24);
        card.Width = 420;
        card.HorizontalAlignment = HorizontalAlignment.Center;
        card.VerticalAlignment = VerticalAlignment.Center;
        _overlays.Children.Add(new Border { Background = Ui.Res("B.Mask") });
        _overlays.Children.Add(card);
        _overlays.Visibility = Visibility.Visible;
        KeyboardNavigation.SetTabNavigation(card, KeyboardNavigationMode.Cycle);
        void Cancel()
        {
            if (creating && !_closed) return;
            _overlays.Children.Clear();
            _overlays.Visibility = Visibility.Collapsed;
            _cancelOverlay = null;
            _path.Focus();
        }
        async void Create(object sender, RoutedEventArgs e)
        {
            if (!create.IsEnabled) return;
            try
            {
                string folder = Path.Combine(_directory, FilePickerRules.ValidateName(name.Text));
                if (Directory.Exists(folder) || File.Exists(folder)) throw new IOException(I18n.L("已存在同名项目，请换一个名称"));
                creating = true;
                create.IsEnabled = cancel.IsEnabled = name.IsEnabled = false;
                create.Content = I18n.L("正在创建…");
                await Task.Run(() => Directory.CreateDirectory(folder));
                if (_closed) return;
                creating = false;
                Cancel();
                await BrowseAsync(folder);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
            {
                error.Text = ex.Message;
                error.Visibility = Visibility.Visible;
                name.IsEnabled = true;
                name.Focus();
            }
            finally
            {
                creating = false;
                create.IsEnabled = cancel.IsEnabled = name.IsEnabled = true;
                create.Content = I18n.L("创建");
            }
        }
        _cancelOverlay = Cancel;
        cancel.Click += (_, _) => Cancel();
        create.Click += Create;
        name.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Create(name, e); } };
        card.Loaded += (_, _) => name.Focus();
    }

    private void Finish(string[] paths)
    {
        SelectedPaths = paths;
        DialogResult = true;
    }
}
