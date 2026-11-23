using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using HelL.Core;
using HelL.Core.Launcher;

namespace HelL.Desktop.Views;

public partial class SavesManagerView : UserControl
{
    public sealed record WorldRow(string Name, string FolderPath, string IconPath, string Meta);
    public sealed record BackupItemRow(string BackupName, string Detail);
    public sealed record LogEntryItem(string Label, string FullPath)
    {
        public override string ToString() => Label;
    }

    private MainWindow? host;
    private string currentInstance = "";
    private string currentTab = "worlds";
    private int logReadRevision;
    internal Func<string, Task<string>>? LogReadFixture { get; set; }

    public SavesManagerView() => InitializeComponent();

    internal void Initialize(MainWindow window) => host = window;
    private void ChooseVersion(object? sender, RoutedEventArgs e) => host?.ChooseContentVersion();

    internal void Reload(string? instanceName = null)
    {
        if (host == null) return;
        logReadRevision++;
        currentInstance = instanceName ?? host.GameLauncher.SelectedInstance;
        if (string.IsNullOrWhiteSpace(currentInstance) || !host.GameLauncher.InstancesList().Any(i => i.Name == currentInstance))
            currentInstance = host.GameLauncher.InstancesList().FirstOrDefault()?.Name ?? "";
        if (string.IsNullOrWhiteSpace(currentInstance))
        {
            SubtitleText.Text = "请先在左侧选择或创建一个实例";
            WorldsList.ItemsSource = Array.Empty<WorldRow>();
            BackupsList.ItemsSource = Array.Empty<BackupItemRow>();
            WorldsEmpty.IsVisible = BackupsEmpty.IsVisible = true;
            CountBadge.Text = "0 项";
            LogFilePicker.ItemsSource = Array.Empty<LogEntryItem>();
            LogViewer.Text = "请先选择一个实例后查看日志。";
            DiagnoseLogButton.IsEnabled = false;
            return;
        }
        SubtitleText.Text = host.ContentLabel(currentInstance) + " · 存档、备份与日志";
        var inst = host.ContentInstance(currentInstance, "saves");

        var worlds = Saves.ListSaves(inst).OfType<PyDict>().Select(row =>
        {
            string name = Py.S(row.Get("name"));
            string path = Py.S(row.Get("path"));
            string icon = Py.S(row.Get("icon"));
            long bytes = Py.Int(row.Get("bytes", 0L));
            long mtime = Py.Int(row.Get("mtime", 0L));
            string timeText = mtime > 0 ? DateTimeOffset.FromUnixTimeSeconds(mtime).LocalDateTime.ToString("yyyy-MM-dd HH:mm") : "未知时间";
            return new WorldRow(name, path, icon, $"{bytes / (1024.0 * 1024.0):F1} MB · 更新于 {timeText}");
        }).ToArray();
        WorldsList.ItemsSource = worlds;
        WorldsEmpty.IsVisible = worlds.Length == 0;

        var backups = Saves.ListBackups(inst).OfType<PyDict>().Select(row =>
        {
            string name = Py.S(row.Get("name", row.Get("file", "")));
            string world = Py.S(row.Get("save", ""));
            long bytes = Py.Int(row.Get("bytes", 0L));
            return new BackupItemRow(name, $"原存档：{world} · {bytes / (1024.0 * 1024.0):F1} MB");
        }).ToArray();
        BackupsList.ItemsSource = backups;
        BackupsEmpty.IsVisible = backups.Length == 0;

        var logContext = host.ContentInstance(currentInstance);
        var logs = Saves.ListMedia(logContext, "crash-reports").OfType<PyDict>()
            .Select(r => new LogEntryItem("[崩溃报告] " + Py.S(r.Get("name")), Py.S(r.Get("path"))))
            .Concat(Saves.ListMedia(logContext, "logs").OfType<PyDict>()
                .Where(r => !Py.S(r.Get("name")).EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
                .Select(r => new LogEntryItem("[运行日志] " + Py.S(r.Get("name")), Py.S(r.Get("path")))))
            .ToArray();
        LogFilePicker.ItemsSource = logs;
        if (logs.Length > 0) LogFilePicker.SelectedIndex = 0;
        else
        {
            LogViewer.Text = "当前实例尚未生成日志或崩溃报告。启动一次游戏后即可在此查看。";
            DiagnoseLogButton.IsEnabled = false;
        }

        UpdateTabCounts(worlds.Length, backups.Length, logs.Length);
    }

    private void UpdateTabCounts(int worlds, int backups, int logs)
    {
        CountBadge.Text = currentTab switch
        {
            "backups" => $"{backups} 个备份",
            "logs" => $"{logs} 份日志",
            _ => $"{worlds} 个世界"
        };
    }

    private void SwitchTab(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tab }) return;
        currentTab = tab;
        foreach (var btn in SectionTabs.Children.OfType<Button>())
            btn.Classes.Set("current", (string?)btn.Tag == tab);
        WorldsPanel.IsVisible = tab == "worlds";
        BackupsPanel.IsVisible = tab == "backups";
        LogsPanel.IsVisible = tab == "logs";
        Reload(currentInstance);
    }

    private void RefreshCurrent(object? sender, RoutedEventArgs e) => Reload(currentInstance);

    private async void BackupWorldRow(object? sender, RoutedEventArgs e)
    {
        if (host == null || string.IsNullOrEmpty(currentInstance) || sender is not Button { Tag: string worldName }) return;
        var content = host.ContentInstance(currentInstance, "saves");
        await host.RunTask(async token =>
        {
            var progress = host.TaskProgress();
            await Task.Run(() =>
            {
                Saves.BackupSave(content, worldName, on_progress: (msg, done, total) => progress.Report(new(msg, done, total)));
            }, token);
            Reload(currentInstance);
            host.SetStatus($"存档「{worldName}」已完成 ZIP 安全备份。");
        });
    }

    private async void DeleteWorldRow(object? sender, RoutedEventArgs e)
    {
        if (host == null || string.IsNullOrEmpty(currentInstance) || sender is not Button { Tag: string worldName }) return;
        string target = currentInstance;
        var content = host.ContentInstance(target, "saves");
        if (!await host.ConfirmAction("删除世界存档", $"实例：{target}\n世界：{worldName}\n删除后不能从游戏内恢复。请先确认已有可用备份。", "删除存档")) return;
        await host.RunTask(_ =>
        {
            Saves.DeleteSave(content, worldName);
            Reload(currentInstance);
            host.SetStatus($"已删除存档：{worldName}");
            return Task.CompletedTask;
        });
    }

    private async void RestoreBackupRow(object? sender, RoutedEventArgs e)
    {
        if (host == null || string.IsNullOrEmpty(currentInstance) || sender is not Button { Tag: string backupName }) return;
        string target = currentInstance;
        var content = host.ContentInstance(target, "saves");
        var worlds = Saves.ListSaves(content).OfType<PyDict>().Select(row => $"{Py.S(row.Get("name"))} · {Py.Int(row.Get("mtime", 0L))}");
        if (!await host.ConfirmAction("还原备份", $"目标实例：{target}\n备份：{backupName}\n当前世界（最后修改 Unix 时间）：\n{string.Join("\n", worlds)}\n同名世界会被覆盖；请先备份当前进度。", "确认还原")) return;
        await host.RunTask(async token =>
        {
            await Task.Run(() => Saves.RestoreBackup(content, backupName, overwrite: true), token);
            Reload(currentInstance);
            host.SetStatus($"备份「{backupName}」已还原到存档目录。");
        });
    }

    private async void DeleteBackupRow(object? sender, RoutedEventArgs e)
    {
        if (host == null || string.IsNullOrEmpty(currentInstance) || sender is not Button { Tag: string backupName }) return;
        string target = currentInstance;
        var content = host.ContentInstance(target, "saves");
        string detail = (BackupsList.ItemsSource as IEnumerable<BackupItemRow>)?.FirstOrDefault(row => row.BackupName == backupName)?.Detail ?? "";
        if (!await host.ConfirmAction("删除备份", $"实例：{target}\n备份：{backupName}\n{detail}\n删除可能移除唯一恢复点。", "删除备份")) return;
        await host.RunTask(_ =>
        {
            Saves.DeleteBackup(content, backupName);
            Reload(currentInstance);
            host.SetStatus($"已删除备份：{backupName}");
            return Task.CompletedTask;
        });
    }

    private async void OpenRowFolder(object? sender, RoutedEventArgs e)
    {
        if (host == null || sender is not Button { Tag: string path } || !Directory.Exists(path)) return;
        await host.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path));
    }

    private async void OpenSavesFolder(object? sender, RoutedEventArgs e)
    {
        if (host == null || string.IsNullOrEmpty(currentInstance)) return;
        string dir = Path.Combine(host.ContentInstance(currentInstance, "saves").path, "saves");
        Directory.CreateDirectory(dir);
        await host.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(dir));
    }

    private async void ChooseLogFile(object? sender, SelectionChangedEventArgs e)
    {
        if (LogFilePicker.SelectedItem is not LogEntryItem entry || !File.Exists(entry.FullPath))
        {
            logReadRevision++;
            DiagnoseLogButton.IsEnabled = false;
            return;
        }
        await ReadSelectedLogAsync(entry);
    }

    internal async Task ReadSelectedLogAsync(LogEntryItem entry)
    {
        int revision = ++logReadRevision;
        string instance = currentInstance;
        DiagnoseLogButton.IsEnabled = false;
        LogViewer.Text = "正在读取所选日志…";
        try
        {
            string excerpt = await (LogReadFixture?.Invoke(entry.FullPath) ?? Task.Run(() => HelL.Desktop.Services.LogPreview.ReadTail(entry.FullPath, 180)));
            if (!IsCurrentLog()) return;
            LogViewer.Text = excerpt.Length == 0 ? "（日志文件为空）" : excerpt;
            DiagnoseLogButton.IsEnabled = excerpt.Length > 0;
        }
        catch (Exception ex)
        {
            if (!IsCurrentLog()) return;
            LogViewer.Text = "读取日志失败，文件可能已轮换、移除或被占用。请刷新后重试。";
            DiagnoseLogButton.IsEnabled = false;
            System.Diagnostics.Trace.TraceWarning("Desktop selected log: " + ex.GetType().Name);
        }

        bool IsCurrentLog() => revision == logReadRevision && instance == currentInstance
            && (LogFilePicker.SelectedItem as LogEntryItem)?.FullPath == entry.FullPath;
    }

    private async void CopyLogOutput(object? sender, RoutedEventArgs e)
    {
        if (host?.Clipboard == null || string.IsNullOrWhiteSpace(LogViewer.Text)) return;
        await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(host.Clipboard, LogViewer.Text);
        host.SetStatus("已复制日志内容到剪贴板。");
    }

    private void SendLogToAi(object? sender, RoutedEventArgs e)
    {
        if (host == null || string.IsNullOrWhiteSpace(LogViewer.Text)) return;
        string snippet = LogViewer.Text.Length > 5500 ? LogViewer.Text[^5500..] : LogViewer.Text;
        host.AskAiWithContext($"请帮我诊断实例「{currentInstance}」的以下 Minecraft 日志/崩溃输出，指出原因与解决办法：\n\n```\n{snippet}\n```");
    }
}
