using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HelL;
using HelL.Models;
using HelL.Pages;
using HelL.Services;

internal static class TaskWorkspaceCheck
{
    private static int _checks;
    private static void Check(bool pass, string label)
    {
        if (!pass) throw new InvalidOperationException(label);
        _checks++;
        Console.WriteLine("[PASS] " + label);
    }

    internal static async Task Run(MainWindow window, string folder)
    {
        Directory.CreateDirectory(folder);
        Motion.Enabled = false;
        window.Navigate("tasks", instant: true);
        await Program.WaitForPage(window, "tasks");
        var page = RichUiAudit.Find<TasksPage>(window).Single();
        var search = RichUiAudit.Field<TextBox>(page, "_search");
        var state = RichUiAudit.Field<ComboBox>(page, "_state");
        var list = RichUiAudit.Field<SPanel>(page, "_list");
        int Visible() => list.Children.OfType<Border>().Count(card => card.Visibility == Visibility.Visible);
        object Card(string id) => RichUiAudit.Field<IDictionary>(page, "_cards")[id]!;
        T Part<T>(object card, string property) => (T)card.GetType().GetProperty(property)!.GetValue(card)!;
        void Add(string id, string title) => TaskStore.Handle(new BridgeEvent { Event = "task_added", TaskId = id, Title = title });
        void Finish(string id, bool success, string message) => TaskStore.Handle(new BridgeEvent { Event = "finished", TaskId = id, Success = success, Message = message });
        Add("running-id", "Fabric 中文生存世界");
        Add("success-id", "安装原版"); Finish("success-id", true, "已完成");
        Add("failed-id", "模组下载"); Finish("failed-id", false, "网络中断");
        Add("cancelled-id", "资源包下载"); Finish("cancelled-id", false, "已取消");
        await MaterialControlsPreview.Layout(window);
        Check(Visible() == 4, "all four task states are visible");
        foreach (string query in new[] { "fabric", "RUNNING-ID", "网络中断" })
        {
            search.Text = query;
            Check(Visible() == 1, "search matches name, ID or status: " + query);
        }
        search.Clear();
        foreach (int index in new[] { 1, 2, 3, 4 })
        {
            state.SelectedIndex = index;
            Check(Visible() == 1, "status filter separates completed, failed and cancelled: " + index);
        }
        search.Text = "no-match";
        Check(Visible() == 0 && RichUiAudit.Field<SPanel>(page, "_empty").Visibility == Visibility.Visible, "unmatched query exposes recovery state");
        RichUiAudit.Field<Button>(page, "_reset").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(search.Text.Length == 0 && state.SelectedIndex == 0 && Visible() == 4, "reset restores all tasks");
        object running = Card("running-id");
        for (int index = 0; index < 80; index++) TaskStore.Get("running-id")!.Append("日志验收 " + index + " · 保留正在阅读的记录");
        Part<Button>(running, "Toggle").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var log = Part<TextBox>(running, "Log");
        await MaterialControlsPreview.Layout(window);
        log.ScrollToVerticalOffset(120);
        log.Select(10, 8);
        await MaterialControlsPreview.Layout(window);
        double offset = log.VerticalOffset;
        string selected = log.SelectedText;
        Add("new-id", "新的任务");
        TaskStore.Get("running-id")!.Append("新增任务期间的日志");
        await Task.Delay(140);
        TaskStore.Notify("running-id");
        await MaterialControlsPreview.Layout(window);
        Check(log.Text.Contains("新增任务期间的日志"), "updated log content reaches the open reader");
        Check(ReferenceEquals(running, Card("running-id")), "new task preserves existing card identity");
        Check(Part<Border>(running, "LogWrap").Visibility == Visibility.Visible, "new task preserves expanded log");
        Check(log.SelectedText == selected, "log append preserves selected text");
        Check(Math.Abs(log.VerticalOffset - offset) < 2, "log append preserves reading offset");
        foreach (bool dark in new[] { false, true })
        foreach (int width in new[] { 800, 1180 })
        {
            window.Width = width; window.Height = 740; App.ApplyTheme(dark);
            await MaterialControlsPreview.Layout(window);
            search.Text = "running-id";
            await MaterialControlsPreview.Layout(window);
            Check(search.ActualWidth >= 200 && state.ActualWidth >= 140, "filters remain usable at " + width);
            foreach (Button button in new[] { Part<Button>(running, "Cancel"), Part<Button>(running, "Toggle") })
            {
                var point = button.TransformToAncestor(page).Transform(new Point());
                Check(point.X >= 0 && point.X + button.ActualWidth <= page.ActualWidth + 1, "task action fits viewport at " + width);
            }
            Program.SaveSnapshot(window, Path.Combine(folder, $"tasks-{width}-{dark}.png"));
        }
        search.Clear(); state.SelectedIndex = 1;
        Check(Visible() == 2, "running filter includes newly added task");
        Finish("running-id", true, "已完成");
        await MaterialControlsPreview.Layout(window);
        Check(Visible() == 1, "completion immediately leaves running filter");
        TaskStore.ClearFinished();
        await MaterialControlsPreview.Layout(window);
        Check(TaskStore.Rows.Count == 1 && Visible() == 1, "clear finished preserves running task");
        Console.WriteLine("[TASK WORKSPACE] " + _checks + " checks passed");
    }
}
