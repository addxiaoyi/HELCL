using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using HelL.Core;
using HelL.Core.Launcher;

namespace HelL.Desktop.Views;

public partial class ModsManagerView : UserControl
{
    public sealed record ContentRow(string FileName, string DisplayName, string SubLabel, bool Enabled, bool CanToggle)
    {
        public string StateBadge => !CanToggle ? "" : (Enabled ? "已启用" : "已禁用");
        public string ToggleText => Enabled ? "禁用" : "启用";
        public double RowOpacity => Enabled ? 1.0 : 0.48;
    }

    private MainWindow? host;
    private string currentInstance = "";
    private string currentKind = "mods";
    private IReadOnlyList<ContentRow> allRows = [];

    public ModsManagerView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DropEvent, OnDropFiles);
    }

    internal void Initialize(MainWindow window) => host = window;

    internal void Reload(string? instanceName = null)
    {
        if (host == null) return;
        currentInstance = instanceName ?? host.GameLauncher.SelectedInstance;
        if (string.IsNullOrWhiteSpace(currentInstance) || !host.GameLauncher.InstancesList().Any(i => i.Name == currentInstance))
            currentInstance = host.GameLauncher.InstancesList().FirstOrDefault()?.Name ?? "";

        if (string.IsNullOrWhiteSpace(currentInstance))
        {
            InstanceSubtitle.Text = "";
            allRows = [];
            ApplyFilter();
            return;
        }
        var inst = new Instance(currentInstance);
        InstanceSubtitle.Text = currentInstance;

        if (currentKind == "mods")
        {
            var entries = Mods.ListInstanceModEntries(inst).OfType<PyDict>().ToArray();
            allRows = entries.Select(row =>
            {
                string file = Py.S(row.Get("filename"));
                bool enabled = Py.Truthy(row.Get("enabled"));
                long bytes = Py.Int(row.Get("bytes", 0L));
                string clean = file.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) ? file[..^9] : file;
                return new ContentRow(file, clean, FormatSize(bytes), enabled, true);
            }).ToArray();
        }
        else
        {
            var files = Mods.ListContentFiles(inst, currentKind);
            string dir = Path.Combine(inst.path, currentKind);
            allRows = files.Select(file =>
            {
                string full = Path.Combine(dir, file);
                long bytes = File.Exists(full) ? new FileInfo(full).Length : 0;
                return new ContentRow(file, file, FormatSize(bytes), true, false);
            }).ToArray();
        }
        ApplyFilter();
    }

    private void SwitchKind(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string kind }) return;
        currentKind = kind;
        foreach (var btn in KindTabs.Children.OfType<Button>())
            btn.Classes.Set("current", (string?)btn.Tag == kind);
        Reload(currentInstance);
    }

    private void SearchChanged(object? sender, TextChangedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        string q = SearchBox.Text?.Trim() ?? "";
        var visible = allRows.Where(r => q.Length == 0 || r.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase)).ToArray();
        ContentList.ItemsSource = visible;
        EmptyPanel.IsVisible = visible.Length == 0;
        if (BatchActions != null) BatchActions.IsVisible = currentKind == "mods" && allRows.Count > 1;
        SummaryBadge.Text = currentKind == "mods"
            ? $"· {allRows.Count(r => r.Enabled)}/{allRows.Count}"
            : $"· {allRows.Count}";
    }

    private void EnableAllMods(object? sender, RoutedEventArgs e) => SetAllModsState(true);
    private void DisableAllMods(object? sender, RoutedEventArgs e) => SetAllModsState(false);

    private async void SetAllModsState(bool enable)
    {
        if (host == null || string.IsNullOrEmpty(currentInstance) || currentKind != "mods") return;
        await host.RunTask(_ =>
        {
            var inst = new Instance(currentInstance);
            string query = SearchBox.Text?.Trim() ?? "";
            var visible = allRows.Where(row => query.Length == 0 || row.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
            foreach (var row in visible.Where(r => r.Enabled != enable))
                Mods.SetModEnabled(inst, row.FileName, enable);
            Reload(currentInstance);
            host.SetStatus($"已{(enable ? "启用" : "禁用")}当前筛选的 {visible.Length} 个模组。");
            return Task.CompletedTask;
        });
    }

    private async void OnDropFiles(object? sender, DragEventArgs e)
    {
        if (host == null || string.IsNullOrEmpty(currentInstance)) return;
        var files = e.DataTransfer.TryGetFiles();
        if (files == null) return;
        string[] paths = files.Select(f => f.TryGetLocalPath()).Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p)).Cast<string>().ToArray();
        if (paths.Length == 0) return;
        await host.RunTask(_ =>
        {
            var inst = new Instance(currentInstance);
            string destDir = Path.Combine(inst.path, currentKind);
            Directory.CreateDirectory(destDir);
            int count = 0;
            foreach (string src in paths)
            {
                File.Copy(src, Path.Combine(destDir, Path.GetFileName(src)), overwrite: true);
                count++;
            }
            Reload(currentInstance);
            host.SetStatus($"已导入 {count} 个文件。");
            return Task.CompletedTask;
        });
    }

    private async void ToggleModRow(object? sender, RoutedEventArgs e)
    {
        if (host == null || string.IsNullOrEmpty(currentInstance) || sender is not Button { Tag: string fileName }) return;
        var row = allRows.FirstOrDefault(r => r.FileName == fileName);
        if (row == null) return;
        await host.RunTask(_ =>
        {
            var inst = new Instance(currentInstance);
            Mods.SetModEnabled(inst, fileName, !row.Enabled);
            Reload(currentInstance);
            host.SetStatus($"{(row.Enabled ? "已禁用" : "已启用")} {row.DisplayName}");
            return Task.CompletedTask;
        });
    }

    private async void DeleteContentRow(object? sender, RoutedEventArgs e)
    {
        if (host == null || string.IsNullOrEmpty(currentInstance) || sender is not Button { Tag: string fileName }) return;
        string target = currentInstance, kind = currentKind;
        if (!await host.ConfirmAction("删除资源文件", $"实例：{target}\n类型：{kind}\n文件：{fileName}\n删除不会自动恢复，请确认已保存原文件。", "删除文件")) return;
        await host.RunTask(_ =>
        {
            var inst = new Instance(target);
            if (kind == "mods") Mods.DeleteMod(inst, fileName);
            else Mods.DeleteContentFile(inst, kind, fileName);
            Reload(currentInstance);
            host.SetStatus($"已删除 {fileName}");
            return Task.CompletedTask;
        });
    }

    private async void ImportFiles(object? sender, RoutedEventArgs e)
    {
        if (host == null || string.IsNullOrEmpty(currentInstance)) return;
        string[] patterns = currentKind == "mods" ? ["*.jar", "*.jar.disabled"] : ["*.zip", "*.jar"];
        var files = await host.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "导入文件",
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("内容包") { Patterns = patterns }]
        });
        if (files.Count == 0) return;
        await host.RunTask(_ =>
        {
            var inst = new Instance(currentInstance);
            string destDir = Path.Combine(inst.path, currentKind);
            Directory.CreateDirectory(destDir);
            int copied = 0;
            foreach (var item in files)
            {
                string? src = item.TryGetLocalPath();
                if (string.IsNullOrEmpty(src) || !File.Exists(src)) continue;
                File.Copy(src, Path.Combine(destDir, Path.GetFileName(src)), overwrite: true);
                copied++;
            }
            Reload(currentInstance);
            host.SetStatus($"已导入 {copied} 个文件。");
            return Task.CompletedTask;
        });
    }

    private async void OpenContentFolder(object? sender, RoutedEventArgs e)
    {
        if (host == null || string.IsNullOrEmpty(currentInstance)) return;
        var inst = new Instance(currentInstance);
        string dir = Path.Combine(inst.path, currentKind);
        Directory.CreateDirectory(dir);
        var folder = await host.StorageProvider.TryGetFolderFromPathAsync(dir);
        if (folder != null) await host.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(dir));
    }

    private void JumpToDownload(object? sender, RoutedEventArgs e)
    {
        if (host == null) return;
        string catalogKind = currentKind switch
        {
            "resourcepacks" => "resourcepack",
            "shaderpacks" => "shader",
            "datapacks" => "datapack",
            _ => "mod"
        };
        host.OpenResourceCategory(catalogKind);
    }

    private static string FormatSize(long bytes) =>
        bytes >= 1024 * 1024 ? $"{bytes / (1024.0 * 1024.0):F1} MB" : $"{Math.Max(1, bytes / 1024)} KB";
}
