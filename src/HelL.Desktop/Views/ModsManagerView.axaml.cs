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
    private void ChooseVersion(object? sender, RoutedEventArgs e) => host?.ChooseContentVersion();

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
        var inst = host.ContentInstance(currentInstance, currentKind);
        InstanceSubtitle.Text = host.ContentLabel(currentInstance);

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
                string filename = Path.GetFileName(file);
                return new ContentRow(filename, filename, FormatSize(bytes), true, false);
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
            var inst = host.ContentInstance(currentInstance, currentKind);
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
        string target = currentInstance, kind = currentKind;
        var files = e.DataTransfer.TryGetFiles();
        if (files == null) return;
        string[] paths = files.Select(file => file.TryGetLocalPath()).Where(path => !string.IsNullOrWhiteSpace(path)).Cast<string>().ToArray();
        try { await ImportPathsAsync(target, kind, paths); }
        catch (Exception error) when (error is not OutOfMemoryException)
        { host.SetStatus("导入未完成，请检查原文件和目标实例后重试。"); System.Diagnostics.Trace.TraceWarning("Desktop import: " + error.GetType().Name); }
    }

    private async void ToggleModRow(object? sender, RoutedEventArgs e)
    {
        if (host == null || string.IsNullOrEmpty(currentInstance) || sender is not Button { Tag: string fileName }) return;
        var row = allRows.FirstOrDefault(r => r.FileName == fileName);
        if (row == null) return;
        await host.RunTask(_ =>
        {
            var inst = host.ContentInstance(currentInstance, currentKind);
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
        var content = host.ContentInstance(target, kind);
        if (!await host.ConfirmAction("删除资源文件", $"目标：{host.ContentLabel(target)}\n类型：{kind}\n文件：{fileName}\n删除不会自动恢复，请确认已保存原文件。", "删除文件")) return;
        await host.RunTask(_ =>
        {
            if (kind == "mods") Mods.DeleteMod(content, fileName);
            else Mods.DeleteContentFile(content, kind, fileName);
            Reload(currentInstance);
            host.SetStatus($"已删除 {fileName}");
            return Task.CompletedTask;
        });
    }

    private async void ImportFiles(object? sender, RoutedEventArgs e)
    {
        if (host == null || string.IsNullOrEmpty(currentInstance)) return;
        string target = currentInstance, kind = currentKind;
        try
        {
            string[] patterns = kind == "mods" ? ["*.jar", "*.jar.disabled"] : ["*.zip", "*.jar"];
            var files = await host.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "导入文件到“" + target + "”",
                AllowMultiple = true,
                FileTypeFilter = [new FilePickerFileType("内容包") { Patterns = patterns }]
            });
            if (files.Count == 0) return;
            string[] paths = files.Select(file => file.TryGetLocalPath()).Where(path => !string.IsNullOrWhiteSpace(path)).Cast<string>().ToArray();
            await ImportPathsAsync(target, kind, paths);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { host.SetStatus("导入未完成，请检查原文件和目标实例后重试。"); System.Diagnostics.Trace.TraceWarning("Desktop import: " + error.GetType().Name); }
    }

    internal async Task ImportPathsAsync(string target, string kind, IReadOnlyList<string> paths)
    {
        if (host == null || paths.Count == 0) return;
        if (kind is not ("mods" or "resourcepacks" or "shaderpacks" or "datapacks")) throw new ArgumentException("资源类型无效，请重新选择。");
        string folder = RequireImportFolder(target, kind);
        var plan = paths.Select(path => (Source: Path.GetFullPath(path), Destination: Mods._mod_file_at(folder, Path.GetFileName(path)))).ToArray();
        if (plan.Select(item => item.Destination).Distinct(StringComparer.OrdinalIgnoreCase).Count() != plan.Length)
            throw new ArgumentException("所选文件有重名项，请每次只导入一个同名文件。");
        foreach (var item in plan)
        {
            if (!File.Exists(item.Source)) throw new FileNotFoundException("原文件已移动或不可读，请重新选择。");
            bool accepted = kind == "mods" ? item.Source.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || item.Source.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)
                : item.Source.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || item.Source.EndsWith(".jar", StringComparison.OrdinalIgnoreCase);
            if (!accepted) throw new ArgumentException("文件格式与所选资源类型不符，请检查后重试。");
            DependencyTarget.RequireLocalPath(item.Source);
            DependencyTarget.RequireLocalPath(item.Destination);
        }
        var replacements = plan.Where(item => File.Exists(item.Destination)).Select(item => item.Destination).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (replacements.Count > 0 && !await host.ConfirmAction("确认覆盖资源文件", $"固定目标实例：{target}\n类型：{kind}\n以下同名文件将被替换：\n{string.Join("\n", replacements.Select(Path.GetFileName))}\n取消会保留全部原文件。", "覆盖这些文件")) return;
        await host.RunTask(_ =>
        {
            if (RequireImportFolder(target, kind) != folder) throw new IOException("目标游戏版本已改变，请重新确认导入位置。");
            Directory.CreateDirectory(folder);
            foreach (var item in plan)
            {
                DependencyTarget.RequireLocalPath(item.Destination);
                PromoteImport(item.Source, item.Destination, replacements.Contains(item.Destination));
            }
            Reload(currentInstance);
            host.SetStatus($"已导入 {plan.Length} 个文件到实例“{target}”。");
            return Task.CompletedTask;
        }, "ImportFiles");
    }

    private string RequireImportFolder(string target, string kind)
    {
        if (host is null || !host.GameLauncher.InstancesList().Any(entry => entry.Name == target))
            throw new IOException("原目标实例已不存在，请重新选择实例。");
        var instance = host.ContentInstance(target, kind);
        if (!Directory.Exists(instance.path)) throw new IOException("原目标目录已不存在，请刷新实例后重试。");
        string folder = Path.Combine(instance.path, kind);
        DependencyTarget.RequireLocalPath(folder);
        return folder;
    }

    private static void PromoteImport(string source, string destination, bool replace)
    {
        string temporary = Path.Combine(Path.GetDirectoryName(destination)!, ".hell-import-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { input.CopyTo(output); output.Flush(true); }
            DependencyTarget.RequireLocalPath(destination);
            if (replace && File.Exists(destination)) File.Replace(temporary, destination, null);
            else File.Move(temporary, destination);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private async void OpenContentFolder(object? sender, RoutedEventArgs e)
    {
        if (host == null || string.IsNullOrEmpty(currentInstance)) return;
        var inst = host.ContentInstance(currentInstance, currentKind);
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
