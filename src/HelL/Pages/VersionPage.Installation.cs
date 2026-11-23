using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HelL.Services;

namespace HelL.Pages;

public sealed partial class VersionPage
{
    private readonly TextBlock _selectedVersion = Ui.Txt("选择一个版本", 21, true).Wrap();
    private readonly TextBlock _versionHint = Ui.Txt("先在版本列表中选择要安装的游戏。", 13, fg: "B.InkMuted").Wrap();
    private readonly TextBlock _installHint = Ui.Txt("", 13, fg: "B.InkMuted").Wrap();
    private readonly TextBlock _installStatus = Ui.Txt("", 13).Wrap();
    private readonly ProgressBar _installProgress = Ui.Prog();
    private readonly SPanel _installActivity = Ui.V(10);
    private readonly Button _cancelInstall = Ui.Btn("取消安装", BtnKind.Ghost);
    private readonly Button _retryInstall = Ui.Btn("重试安装", BtnKind.Primary);
    private readonly Button _openGame = Ui.Btn("返回启动页", BtnKind.Primary);
    private readonly Button _chooseAnother = Ui.Btn("选择其他版本", BtnKind.Ghost);
    private readonly Button _retryLoader = Ui.Btn("重新检测兼容版本", BtnKind.Ghost);
    private readonly Expander _installedSection = new() { Header = "已安装版本", IsExpanded = false };
    private string _installTask = "", _installInstance = "", _installedVersion = "", _completedTask = "";
    private bool _installStarting, _loaderLoading;
    private string _loaderError = "";
    private int _loaderGeneration;
    public string VisibleInstallTask => _installTask;

    private SPanel BuildInstallPanel()
    {
        _loader.SelectedIndex = 0;
        _loaderVer.IsEnabled = false;
        var extras = Ui.H(16, _optifine, _lite); extras.Wrap = true;
        var advanced = new Expander { Header = "模组加载器与兼容组件", Content = Ui.V(12,
            Ui.Field("加载器", _loader, "原版游戏不需要选择加载器。", 84), Ui.Field("兼容版本", _loaderVer, "先选择加载器；列表仅显示所选 Minecraft 版本支持的兼容版本。", 84), extras).M(0, 12, 0, 0) };
        _selectedVersion.LineHeight = 28;
        _versionHint.LineHeight = _installHint.LineHeight = 20;
        _install.MinHeight = 44; _install.BorderThickness = new Thickness(0);
        _cancelInstall.Click += (_, _) => RunOnce(_cancelInstall, async () => { await Api.CallAsync("cancel_task", new { task_id = _installTask }); _installStatus.Text = "正在取消安装…"; });
        _retryInstall.Click += (_, _) => RunOnce(_retryInstall, RetryInstall);
        _retryLoader.Click += (_, _) => Run(LoadLoaderVersionsAsync);
        _openGame.Click += (_, _) => Run(async () =>
        {
            var versions = await Api.CallAsync<List<string>>("get_installed_versions", new { instance = _installInstance }) ?? new();
            const string completedPrefix = "版本安装完成: ";
            string completed = TaskStore.Get(_installTask)?.Status ?? "";
            string version = completed.StartsWith(completedPrefix, StringComparison.Ordinal) ? completed.Substring(completedPrefix.Length).Trim() : _installedVersion;
            if (!versions.Contains(version)) version = "";
            await InstancePage.SetDefaultAsync(_installInstance);
            if (Win?.GetPage("launch") is LaunchPage page) page.PreferInstance(_installInstance, version);
            Win?.Navigate("launch");
        });
        _chooseAnother.Click += (_, _) => { _installTask = ""; _installActivity.Visibility = Visibility.Collapsed; SyncInstallSummary(); _list.Focus(); };
        var actions = Ui.H(6, _cancelInstall, _retryInstall, _openGame, _chooseAnother, Ui.Btn("查看下载任务", BtnKind.Ghost, (_, _) => Win?.Navigate("tasks"))); actions.Wrap = true;
        _installProgress.Height = 5;
        _installActivity.Children.Add(_installStatus); _installActivity.Children.Add(_installProgress); _installActivity.Children.Add(actions);
        _installActivity.Visibility = Visibility.Collapsed; _retryLoader.Visibility = Visibility.Collapsed;
        System.Windows.Automation.AutomationProperties.SetName(_installProgress, "Minecraft 安装进度");
        var choices = Ui.V(14, Ui.Txt("2 · 确认安装", 13, true, "B.InkMuted"), Ui.V(5, _selectedVersion, _versionHint),
            Ui.Field("安装到", _inst, "游戏文件和存档会保存在此实例中。", 84), advanced, _installHint, _retryLoader, _install.Stretch(), _installActivity);
        Border surface = Ui.Card(choices, 18);
        surface.CornerRadius = new CornerRadius(12);
        return Ui.V(0, surface);
    }

    private void SelectRecommended()
    {
        _search.Text = ""; _release.IsChecked = true; _snapshot.IsChecked = _oldAlpha.IsChecked = _oldBeta.IsChecked = false;
        ApplyFilter();
        var latest = _all.FirstOrDefault(row => row.Type == "release");
        if (latest != null) { _list.SelectedItem = latest; _list.ScrollIntoView(latest); }
    }

    private void SyncInstallSummary()
    {
        if (_install == null) return;
        var selected = _list.SelectedItem as Row;
        string latest = _all.FirstOrDefault(row => row.Type == "release")?.Version ?? "";
        _selectedVersion.Text = selected == null ? "选择一个版本" : "Minecraft " + selected.Version;
        _versionHint.Text = selected == null ? "先在版本列表中选择要安装的游戏。" :
            (selected.Type == "release" ? selected.Version == latest ? "最新正式版 · 推荐首次游玩" : "正式版 · 适合日常游玩" : "测试版本 · 新功能可能仍有问题") +
            (selected.Date.Length > 0 ? "\n发布于 " + selected.Date : "");
        _installHint.Text = _loaderLoading ? "正在检查加载器兼容性…" : _loaderError.Length > 0 ? _loaderError : "安装会显示下载与校验进度，完成后可返回启动页。";
        _retryLoader.Visibility = _loaderError.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        bool active = _installTask.Length > 0 && TaskStore.Get(_installTask)?.Finished != true;
        _install.Visibility = _installTask.Length > 0 ? Visibility.Collapsed : Visibility.Visible;
        _install.IsEnabled = selected != null && _inst.Str().Length > 0 && !_loaderLoading && _loaderError.Length == 0 && !_installStarting && !active;
        _inst.IsEnabled = _loader.IsEnabled = !active && !_installStarting;
        _list.IsEnabled = true;
        _search.IsEnabled = _release.IsEnabled = _snapshot.IsEnabled = _oldAlpha.IsEnabled = _oldBeta.IsEnabled = true;
        _optifine.IsEnabled = !active && (_loader.Str() == I18n.L("无") || _loader.Str() == "Forge");
        _optifine.ToolTip = _optifine.IsEnabled ? "为所选版本安装兼容 OptiFine" : active ? "当前安装参数已锁定" : "OptiFine 兼容选项仅适用于原版或 Forge";
        _loaderVer.ToolTip = _loader.Str() == I18n.L("无") ? "原版不需要加载器版本" : _loaderLoading ? "正在读取兼容版本" : "仅列出所选 Minecraft 版本支持的加载器版本";
        _lite.IsEnabled = !active;
        _loaderVer.IsEnabled = !active && !_loaderLoading && _loader.Str() != I18n.L("无");
        _install.Content = selected == null ? "选择版本后安装" : "安装 " + selected.Version;
        System.Windows.Automation.AutomationProperties.SetName(_install, _install.Content.ToString());
    }

    private void TrackInstall(string task, string instance, string version)
    {
        if (string.IsNullOrWhiteSpace(task)) throw new InvalidOperationException("安装任务未创建，请重试。");
        _installTask = task; _installInstance = instance; _installedVersion = version;
        _installActivity.Visibility = Visibility.Visible; SyncInstallProgress(); _installActivity.BringIntoView();
        Win?.RefreshTaskDock();
    }

    private void InstallChanged(TaskRow task) { if (task.Id == _installTask) SyncInstallProgress(); }

    private void SyncInstallProgress()
    {
        if (_installTask.Length == 0) return;
        var task = TaskStore.Get(_installTask);
        _installActivity.Visibility = Visibility.Visible;
        _installProgress.IsIndeterminate = task == null || task.Indeterminate;
        _installProgress.Value = task?.Progress ?? 0;
        _installStatus.Text = task == null ? "正在加入下载队列…" : task.Cancelled ? "安装已取消。" : task.Finished ? task.Success ? "安装完成，可以返回启动页选择账号。" : "安装未完成：" + task.Status :
            task.Status + (task.Indeterminate ? "" : " · " + task.Progress.ToString("0") + "%") + (task.Speed.Length > 0 ? " · " + task.Speed : "");
        _cancelInstall.Visibility = task?.Finished == true ? Visibility.Collapsed : Visibility.Visible;
        _retryInstall.Visibility = task?.Finished == true && !task.Success && DownloadRetries.Find(task.Id) != null ? Visibility.Visible : Visibility.Collapsed;
        _openGame.Visibility = task?.Success == true && task.Finished ? Visibility.Visible : Visibility.Collapsed;
        _chooseAnother.Visibility = task?.Finished == true ? Visibility.Visible : Visibility.Collapsed;
        _installProgress.SetResourceReference(Control.ForegroundProperty, task?.Cancelled == true ? "B.InkFaint" : task?.Finished == true && !task.Success ? "B.Danger" : "B.Accent");
        SyncInstallSummary();
        if (task?.Finished == true && task.Success && _completedTask != task.Id)
        {
            _completedTask = task.Id; Run(ReloadInstalledAsync);
        }
    }

    private async Task RetryInstall()
    {
        string previous = _installTask; var request = DownloadRetries.Find(previous);
        if (request == null || TaskStore.Get(previous)?.Finished != true || _installStarting) return;
        _installStarting = true;
        SyncInstallSummary();
        try
        {
            using var arguments = JsonDocument.Parse(request.ArgumentsJson);
            string next = await Api.StartTaskAsync(request.Method, arguments.RootElement);
            DownloadRetries.Remove(previous); TrackInstall(next, _installInstance, _installedVersion);
        }
        finally { _installStarting = false; SyncInstallSummary(); }
    }
}
