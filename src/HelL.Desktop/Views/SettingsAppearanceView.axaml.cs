using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using HelL.Core;
using HelL.Core.Launcher;

namespace HelL.Desktop.Views;

public partial class SettingsAppearanceView : UserControl
{
    private MainWindow? host;
    private (string Source, string Threads)? savedNetwork;
    internal Action<PyDict>? CommitNetworkFixture { get; set; }

    private string NetworkSource => (MirrorCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "auto";
    private bool HasNetworkDraft => savedNetwork is { } saved
        && (NetworkSource != saved.Source || ThreadsBox.Text != saved.Threads);

    public SettingsAppearanceView() => InitializeComponent();

    internal void Initialize(MainWindow window, string root)
    {
        host = window;
        DataRootText.Text = root;
        if (Content is ScrollViewer { Content: StackPanel content })
        {
            var diagnostics = new Button { Content = "预览并导出诊断包", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
            diagnostics.Click += OpenDiagnostics;
            content.Children.Add(diagnostics);
        }
        Reload();
    }

    private async void OpenDiagnostics(object? sender, RoutedEventArgs e)
    {
        if (host is not null) await new DiagnosticWindow(host.DiagnosticIssues).ShowDialog(host);
    }

    internal void Reload()
    {
        if (!HasNetworkDraft) LoadNetworkSettings();
        if (host != null)
        {
            SyncThemeSelection(host.CurrentTheme);
            SyncGlassSelection(host.CurrentGlass);
        }
    }

    private void LoadNetworkSettings()
    {
        string source = Py.S(Config.CONFIG.Get("download_source", "auto"));
        if (source is not ("auto" or "bmclapi" or "official")) source = "auto";
        string threads = Math.Clamp(Py.Int(Config.CONFIG.Get("download_threads", 32L)), 1, 64).ToString();
        savedNetwork = (source, threads);
        MirrorCombo.SelectedIndex = source switch { "bmclapi" => 1, "official" => 2, _ => 0 };
        ThreadsBox.Text = threads;
        UpdateNetworkDraft();
    }

    private void EditNetworkSource(object? sender, SelectionChangedEventArgs e) => UpdateNetworkDraft();
    private void EditNetworkThreads(object? sender, TextChangedEventArgs e) => UpdateNetworkDraft();

    private void UpdateNetworkDraft()
    {
        if (savedNetwork is null || NetworkNotice == null || SaveNetworkButton == null || DiscardNetworkButton == null) return;
        bool edited = HasNetworkDraft;
        SaveNetworkButton.IsEnabled = edited;
        DiscardNetworkButton.IsVisible = edited;
        NetworkNotice.Classes.Set("error", false);
        NetworkNotice.Text = edited ? "有未保存的网络设置。切换页面或任务结束后仍会保留。" : "";
        NetworkNotice.IsVisible = edited;
    }

    private void DiscardNetworkSettings(object? sender, RoutedEventArgs e) => LoadNetworkSettings();

    internal void SyncThemeSelection(string theme)
    {
        if (ThemeCards == null) return;
        foreach (var btn in ThemeCards.Children.OfType<Button>())
            btn.Classes.Set("current", (string?)btn.Tag == theme);
    }

    internal void SyncGlassSelection(string glass)
    {
        if (GlassCards == null) return;
        foreach (var btn in GlassCards.Children.OfType<Button>())
            btn.Classes.Set("current", (string?)btn.Tag == glass);
    }

    private void PickThemeCard(object? sender, RoutedEventArgs e)
    {
        if (host == null || sender is not Button { Tag: string theme }) return;
        host.SelectTheme(theme);
    }

    private void PickGlassCard(object? sender, RoutedEventArgs e)
    {
        if (host == null || sender is not Button { Tag: string glass }) return;
        host.SelectGlassOpacity(glass);
    }

    private void SaveNetworkSettings(object? sender, RoutedEventArgs e)
    {
        if (host == null) return;
        string source = NetworkSource;
        if (!int.TryParse(ThreadsBox.Text, out int threads) || threads is < 1 or > 64)
        {
            ShowNetworkError("并发线程数请填写 1–64 之间的整数。");
            ThreadsBox.Focus();
            return;
        }
        try
        {
            var patch = new PyDict { ["download_source"] = source, ["download_threads"] = (long)threads };
            if (CommitNetworkFixture is { } commit) commit(patch);
            else Config.CONFIG.Commit(patch);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            System.Diagnostics.Trace.TraceWarning("SettingsAppearanceView.SaveNetworkSettings: " + error.GetType().Name);
            ShowNetworkError("网络设置保存失败，输入已保留。请检查数据目录后重试。");
            return;
        }
        LoadNetworkSettings();
        NetworkNotice.Text = "网络设置已保存。";
        NetworkNotice.IsVisible = true;
        host.SetStatus("网络设置已保存。");
    }

    private void ShowNetworkError(string message)
    {
        NetworkNotice.Text = message;
        NetworkNotice.IsVisible = true;
        NetworkNotice.Classes.Set("error", true);
        host?.SetStatus(message);
    }

    private async void OpenDataRoot(object? sender, RoutedEventArgs e)
    {
        if (host == null || string.IsNullOrWhiteSpace(DataRootText.Text) || !Directory.Exists(DataRootText.Text)) return;
        await host.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(DataRootText.Text));
    }
}
