using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using HelL.Desktop.Models;

namespace HelL.Desktop.Views;

public partial class TasksView : UserControl
{
    private readonly ObservableCollection<DesktopTask> tasks = [];
    private Action? cancelCurrent;
    private bool filtering;
    private DesktopTask? selected;

    public TasksView()
    {
        InitializeComponent();
        SearchBox.PropertyChanged += (_, change) =>
        {
            if (change.Property == TextBox.TextProperty) Refresh();
        };
    }

    internal void Initialize(Action cancel) => cancelCurrent = cancel;
    internal IReadOnlyCollection<DesktopTask> History => tasks;

    internal void Add(DesktopTask task)
    {
        if (tasks.Count == 100)
        {
            tasks[^1].PropertyChanged -= TaskChanged;
            tasks.RemoveAt(tasks.Count - 1);
        }
        task.PropertyChanged += TaskChanged;
        tasks.Insert(0, task);
        Refresh(task.Id);
    }

    private void TaskChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DesktopTask.State)) Refresh();
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
             task.StateLabel.Contains(query, StringComparison.CurrentCultureIgnoreCase))).ToArray();
        filtering = true;
        try
        {
            TaskList.ItemsSource = visible;
            TaskList.SelectedItem = visible.FirstOrDefault(task => task.Id == selection) ?? visible.FirstOrDefault();
        }
        finally { filtering = false; }
        SelectCurrent();
        CountLabel.Text = $"{visible.Length} / {tasks.Count} 个任务 · 本次会话";
        EmptyNotice.IsVisible = visible.Length == 0;
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
        DetailTitle.Text = selected?.Title ?? "选择任务查看记录";
        LogText.Text = selected == null ? "任务记录仅保留在本次会话中。" : string.Join(Environment.NewLine, selected.Log);
        CancelButton.IsVisible = selected?.CanCancel == true;
    }

    private void CancelSelected(object? sender, RoutedEventArgs e)
    {
        if (selected?.CanCancel == true) cancelCurrent?.Invoke();
    }
}
