using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using HelL.Core;
using HelL.Core.Launcher;

namespace HelL.Desktop.Views;

public partial class SettingsAppearanceView : UserControl
{
    private MainWindow? host;

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
        string source = Py.S(Config.CONFIG.Get("download_source", "auto"));
        MirrorCombo.SelectedIndex = source switch { "bmclapi" => 1, "official" => 2, _ => 0 };
        long threads = Py.Int(Config.CONFIG.Get("download_threads", 32L));
        ThreadsBox.Text = Math.Clamp(threads, 1, 64).ToString();
        if (host != null)
        {
            SyncThemeSelection(host.CurrentTheme);
            SyncGlassSelection(host.CurrentGlass);
        }
    }

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
        string source = (MirrorCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "auto";
        if (!int.TryParse(ThreadsBox.Text, out int threads) || threads is < 1 or > 64)
        {
            host.SetStatus("并发线程数请填写 1–64 之间的整数。");
            return;
        }
        Config.CONFIG.Set("download_source", source);
        Config.CONFIG.Set("download_threads", (long)threads);
        Config.CONFIG.Save();
        host.SetStatus("网络设置已保存。");
    }

    private async void OpenDataRoot(object? sender, RoutedEventArgs e)
    {
        if (host == null || string.IsNullOrWhiteSpace(DataRootText.Text) || !Directory.Exists(DataRootText.Text)) return;
        await host.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(DataRootText.Text));
    }
}
