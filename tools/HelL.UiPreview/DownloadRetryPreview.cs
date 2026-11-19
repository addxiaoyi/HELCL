using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using HelL;
using HelL.Core.Launcher;
using HelL.Models;
using HelL.Services;

internal static class DownloadRetryPreview
{
    internal static async Task Run(MainWindow window, string folder)
    {
        Directory.CreateDirectory(folder);
        var instance = new Instance("retry-ui-fixture", true); instance.Create();
        var api = AppServices.Client;
        string task = await api.StartTaskAsync("repair_version", new { instance = instance.name, version = "not-installed" });
        var failed = await api.CallAsync<OpResult>("wait_task", new { task_id = task, timeout = 10 });
        if (failed?.Ok != false) throw new InvalidOperationException("Fixture should fail before downloading anything.");
        window.Navigate("tasks", instant: true); await Program.WaitForPage(window, "tasks");
        await Task.Delay(300); window.UpdateLayout();
        var retry = Walk(window).OfType<Button>().Single(button => button.IsVisible && AutomationProperties.GetName(button) == "重试下载");
        int before = TaskStore.Rows.Count;
        retry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        for (int i = 0; i < 100 && (TaskStore.Rows.Count == before || DownloadRetries.Find(task) != null); i++) await Task.Delay(50);
        if (TaskStore.Rows.Count != before + 1 || DownloadRetries.Find(task) != null) throw new InvalidOperationException("Retry did not replace its launchable request exactly once.");
        string next = TaskStore.Rows.Last().Id;
        if (DownloadRetries.Find(next)?.ArgumentsJson.Contains("not-installed") != true) throw new InvalidOperationException("Retry lost the original target.");
        foreach (bool dark in new[] { false, true })
        {
            App.ApplyTheme(dark); window.Width = 900; window.Height = 620; await Task.Delay(250); window.UpdateLayout();
            Program.SaveSnapshot(window, Path.Combine(folder, "download-retry-" + (dark ? "dark" : "light") + ".png"));
        }
        Console.WriteLine("[DOWNLOAD RETRY UI] Failed task retry creates one new task with the original target; original retry is retired.");
    }
    private static System.Collections.Generic.IEnumerable<DependencyObject> Walk(DependencyObject root)
    { for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++) { var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Walk(child)) yield return nested; } }
}
