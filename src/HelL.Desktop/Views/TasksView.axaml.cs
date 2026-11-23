using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using HelL.Desktop.Models;

namespace HelL.Desktop.Views;

public partial class TasksView : UserControl
{
    private sealed record RetryRequest(string Operation, Func<CancellationToken, Task> Run);

    private readonly ObservableCollection<DesktopTask> tasks = [];
    private Action? cancelCurrent;
    private bool filtering;
    private DesktopTask? selected;
    private MainWindow? host;
    private readonly Dictionary<Guid, RetryRequest> retries = [];
    private string historyRoot = "";

    public TasksView()
    {
        InitializeComponent();
        UpdateLogToggle();
        SearchBox.PlaceholderText = "名称、状态、原因或任务 ID";
        SizeChanged += (_, _) => TaskList.MaxHeight = Math.Clamp(Bounds.Height * 0.4, 140, 300);
        SearchBox.PropertyChanged += (_, change) =>
        {
            if (change.Property == TextBox.TextProperty) Refresh();
        };
    }

    internal void Initialize(Action cancel, MainWindow? owner = null)
    {
        cancelCurrent = cancel; host = owner; historyRoot = owner?.DataRoot ?? "";
        if (historyRoot.Length == 0) return;
        try { foreach (DesktopTask task in Services.DesktopTaskHistory.Read(historyRoot)) { task.PropertyChanged += TaskChanged; tasks.Add(task); } Refresh(); }
        catch (Exception error) { owner?.SetStatus("任务历史读取失败，请检查诊断信息后重试。" ); System.Diagnostics.Trace.TraceWarning("Desktop task history: " + error.GetType().Name); }
    }
    internal void SetRetry(DesktopTask task, string operation, Func<CancellationToken, Task> retry) => retries[task.Id] = new(operation, retry);
    internal IReadOnlyCollection<DesktopTask> History => tasks;

    internal void Add(DesktopTask task)
    {
        if (tasks.Count == 100)
        {
            retries.Remove(tasks[^1].Id);
            tasks[^1].PropertyChanged -= TaskChanged;
            tasks.RemoveAt(tasks.Count - 1);
        }
        task.PropertyChanged += TaskChanged;
        tasks.Insert(0, task);
        Refresh(task.Id);
    }

    private void TaskChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DesktopTask.State))
        {
            Refresh();
            if (historyRoot.Length > 0) try
            {
                string? archived = Services.DesktopTaskHistory.Save(historyRoot, tasks);
                if (archived is not null) host?.SetStatus("新的任务历史已保存。损坏的旧记录已保留为：" + archived);
            }
            catch (Exception error) { host?.SetStatus("任务结果已完成，但历史保存失败。" ); System.Diagnostics.Trace.TraceWarning(error.GetType().Name); }
        }
        else if (e.PropertyName == nameof(DesktopTask.Message) && !string.IsNullOrWhiteSpace(SearchBox.Text))
            Refresh();
        if (ReferenceEquals(sender, selected)) UpdateDetails();
    }

    private void SearchTasks(object? sender, TextChangedEventArgs e) => Refresh();
    private void FilterTasks(object? sender, SelectionChangedEventArgs e) => Refresh();
    private void ChooseTask(object? sender, SelectionChangedEventArgs e)
    {
        if (filtering) return;
        SelectCurrent();
    }

    private void SelectCurrent()
    {
        if (selected != null) selected.Log.CollectionChanged -= LogChanged;
        selected = TaskList.SelectedItem as DesktopTask;
        if (selected != null) selected.Log.CollectionChanged += LogChanged;
        UpdateDetails();
    }

    private void LogChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateDetails();

    private void Refresh(Guid? selection = null)
    {
        if (TaskList == null || StatePicker == null || SearchBox == null) return;
        string filter = (StatePicker.SelectedItem as ComboBoxItem)?.Tag as string ?? "all";
        string query = SearchBox.Text?.Trim() ?? "";
        selection ??= selected?.Id;
        var visible = tasks.Where(task => Matches(task, filter) &&
            (task.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
             task.StateLabel.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
             task.Message.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
             task.Id.ToString("D").Contains(query, StringComparison.CurrentCultureIgnoreCase))).ToArray();
        filtering = true;
        try
        {
            TaskList.ItemsSource = visible;
            TaskList.SelectedItem = visible.FirstOrDefault(task => task.Id == selection) ?? visible.FirstOrDefault();
        }
        finally { filtering = false; }
        SelectCurrent();
        CountLabel.Text = tasks.Count == 0 ? "暂无任务" : $"{visible.Length} / {tasks.Count} 个任务 · 最近记录";
        EmptyNotice.IsVisible = visible.Length == 0;
        EmptyPanel.IsVisible = visible.Length == 0;
        TaskList.IsVisible = visible.Length > 0;
        ClearFiltersButton.IsVisible = visible.Length == 0 && tasks.Count > 0;
        EmptyNotice.Text = tasks.Count == 0 ? "尚无任务。创建实例、安装版本或下载资源后，进度与结果会显示在这里。"
            : "没有符合筛选条件的任务。请更换关键词或状态。";
    }

    private static bool Matches(DesktopTask task, string filter) => filter switch
    {
        "active" => task.IsActive,
        "failed" => task.State == DesktopTaskState.Failed,
        "completed" => task.State == DesktopTaskState.Completed,
        "cancelled" => task.State == DesktopTaskState.Cancelled,
        _ => true
    };

    private void UpdateDetails()
    {
        LogDetails.IsVisible = selected != null;
        DetailTitle.Text = selected?.Title ?? "选择任务查看记录";
        LogText.Text = selected == null ? "最近 100 条已结束任务会保存在本机；选择任务查看执行日志。" : string.Join(Environment.NewLine, selected.Log);
        CancelButton.IsVisible = selected?.CanCancel == true;
        RetryButton.IsVisible = selected?.State is DesktopTaskState.Failed or DesktopTaskState.Cancelled && retries.ContainsKey(selected.Id);
    }

    private void ClearFilters(object? sender, RoutedEventArgs e)
    {
        SearchBox.Clear();
        StatePicker.SelectedIndex = 0;
        TaskList.Focus();
    }

    private async void RetrySelected(object? sender, RoutedEventArgs e)
    {
        if (host == null || selected is not { State: DesktopTaskState.Failed or DesktopTaskState.Cancelled } task
            || !retries.TryGetValue(task.Id, out var retry)) return;
        if (await host.ConfirmAction("重试安全任务", $"将重新执行条件仍然有效的任务：{task.Title}\n不会重试删除、覆盖、登录或游戏启动操作。", "重试"))
            await host.RunTask(retry.Run, retry.Operation);
    }
    private async void CopyLog(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) throw new IOException("Clipboard unavailable.");
            await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, LogText.Text ?? "");
            host?.SetStatus("已复制任务日志。");
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { host?.SetStatus("日志复制失败，请检查系统剪贴板后重试。"); System.Diagnostics.Trace.TraceWarning(error.GetType().Name); }
    }
    private void ExpandLog(object? sender, RoutedEventArgs e)
    {
        LogDetails.MaxHeight = double.IsPositiveInfinity(LogDetails.MaxHeight) ? 280 : double.PositiveInfinity;
        UpdateLogToggle();
    }

    private void UpdateLogToggle()
    {
        string label = double.IsPositiveInfinity(LogDetails.MaxHeight) ? "收起日志" : "展开日志";
        ExpandLogButton.Content = label;
        AutomationProperties.SetName(ExpandLogButton, label);
    }
    private void CancelSelected(object? sender, RoutedEventArgs e)
    {
        if (selected?.CanCancel == true) cancelCurrent?.Invoke();
    }
}
