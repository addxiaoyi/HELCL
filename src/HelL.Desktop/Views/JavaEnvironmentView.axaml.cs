using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using HelL.Core;
using HelL.Core.Launcher;

namespace HelL.Desktop.Views;

public partial class JavaEnvironmentView : UserControl
{
    public sealed record JavaRow(string MajorBadge, string Title, string ExePath);

    private MainWindow? host;

    public JavaEnvironmentView() => InitializeComponent();

    internal void Initialize(MainWindow window) => host = window;

    internal void Reload()
    {
        if (host == null) return;
        string? instName = host.GameLauncher.SelectedInstance;
        if (!string.IsNullOrWhiteSpace(instName) && host.GameLauncher.InstancesList().Any(i => i.Name == instName))
        {
            string pref = host.GameLauncher.JavaPreference(instName);
            CurrentBindingLabel.Text = string.IsNullOrWhiteSpace(pref)
                ? $"实例「{instName}」：自动按游戏版本匹配最佳 Java（推荐）"
                : $"实例「{instName}」已指定：{pref}";
        }
        else
        {
            CurrentBindingLabel.Text = "未选择实例 · 默认启动时自动匹配兼容的 Java 版本";
        }

        var rows = Java.CachedAllJavas().OfType<PyDict>().Select(ParseJavaRow).Where(r => r != null).Cast<JavaRow>().ToArray();
        JavaList.ItemsSource = rows;
        EmptyJava.IsVisible = rows.Length == 0;
        RuntimeCountLabel.Text = $"{rows.Length} 个运行时";
    }

    private static JavaRow? ParseJavaRow(PyDict dict)
    {
        string exe = Py.S(dict.Get("exe"));
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe)) return null;
        long major = Py.Int(dict.Get("major", 0L));
        string version = Py.S(dict.Get("version", "未知版本"));
        string arch = Py.S(dict.Get("arch", ""));
        string source = Py.S(dict.Get("source", "本机"));
        string badge = major > 0 ? $"J{major}" : "JVM";
        string title = string.Join(" · ", new[] { $"Java {(major > 0 ? major.ToString() : "")}".Trim(), version, arch, source }.Where(s => s.Length > 0));
        return new JavaRow(badge, title, exe);
    }

    private async void ScanSystemJava(object? sender, RoutedEventArgs e)
    {
        if (host == null) return;
        await host.RunTask(async token =>
        {
            host.SetStatus("正在全盘扫描已安装的 Java 运行时…");
            var rows = await Task.Run(() =>
            {
                Java.ResetCaches();
                return Java.AllJavas().OfType<PyDict>().Select(ParseJavaRow).Where(r => r != null).Cast<JavaRow>().ToArray();
            }, token);
            token.ThrowIfCancellationRequested();
            JavaList.ItemsSource = rows;
            EmptyJava.IsVisible = rows.Length == 0;
            RuntimeCountLabel.Text = $"{rows.Length} 个运行时";
            host.SetStatus($"扫描完成，共发现 {rows.Length} 个可用 Java 运行时。");
        }, "DetectJava");
    }

    private void BindJavaToInstance(object? sender, RoutedEventArgs e)
    {
        if (host == null || sender is not Button { Tag: string exePath }) return;
        host.ApplyInstanceJava(exePath);
        Reload();
    }

    private void ResetAutoJava(object? sender, RoutedEventArgs e)
    {
        if (host == null) return;
        host.ApplyInstanceJava("");
        Reload();
    }

    private async void BrowseManualJava(object? sender, RoutedEventArgs e)
    {
        if (host == null) return;
        var files = await host.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择 Java 可执行文件 (java / javaw.exe)",
            AllowMultiple = false
        });
        if (files.Count == 0) return;
        string? local = files[0].TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(local))
        {
            host.ApplyInstanceJava(local);
            Reload();
        }
    }

    private async void InstallVendorRuntime(object? sender, RoutedEventArgs e)
    {
        if (host == null || sender is not Button { Tag: string majorTag } || !long.TryParse(majorTag, out long major)) return;
        string vendor = (VendorCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "adoptium";
        await host.RunTask(async token =>
        {
            var progress = host.TaskProgress();
            string installedExe = await Task.Run(() =>
            {
                var dm = new DownloadManager(cancel: () => token.IsCancellationRequested);
                return Java.InstallJavaVendor(dm, major, vendor, (msg, done, total) => progress.Report(new(msg, done, total)), force: false);
            }, token);
            Java.ResetCaches();
            var rows = await Task.Run(() => Java.AllJavas().OfType<PyDict>().Select(ParseJavaRow).Where(r => r != null).Cast<JavaRow>().ToArray(), token);
            JavaList.ItemsSource = rows;
            EmptyJava.IsVisible = rows.Length == 0;
            RuntimeCountLabel.Text = $"{rows.Length} 个运行时";
            host.SetStatus($"Java {major} 已安装完成：{installedExe}");
        }, "InstallVersion");
    }
}
