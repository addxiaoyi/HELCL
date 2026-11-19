using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HelL;
using HelL.Core.Launcher;
using HelL.Models;
using HelL.Pages;
using HelL.Services;

internal static class InstallInteractionAudit
{
    internal static async Task Run(MainWindow window, string folder)
    {
        window.Navigate("version", instant: true); await Program.WaitForPage(window, "version");
        var page = RichUiAudit.Find<VersionPage>(window).Single();
        await MaterialControlsPreview.Until(() => RichUiAudit.Field<ListBox>(page, "_list").Items.Count > 0, "real version manifest loaded", 30);
        const string taskId = "install-layout-fixture";
        TaskStore.Handle(new BridgeEvent { Event = "task_added", TaskId = taskId, Title = "安装 Minecraft · 中文长名称实例与加载器兼容组件" });
        RichUiAudit.Invoke(page, "TrackInstall", taskId, "默认", "1.21.1");
        foreach (bool dark in new[] { false, true })
        {
            App.ApplyTheme(dark); window.Width = 800; window.Height = 620;
            TaskStore.Handle(new BridgeEvent { Event = "progress", TaskId = taskId, Current = 74, Total = 100,
                Message = "正在下载并校验中文路径下的游戏依赖与资源文件，这是较长的状态说明" });
            await MaterialControlsPreview.Layout(window);
            RichUiAudit.Field<SPanel>(page, "_installActivity").BringIntoView();
            await MaterialControlsPreview.Layout(window);
            RichUiAudit.Shot(window, folder, "install-progress-" + dark);
            InteractionAudit.Check(InteractionAudit.Inside(RichUiAudit.Field<Button>(page, "_cancelInstall"), page), "cancel installation remains reachable at 800 DIP");
            InteractionAudit.Check(RichUiAudit.Field<ProgressBar>(page, "_installProgress").Value == 74, "installation progress uses task percentage");
            window.Navigate("tasks", instant: true); await Program.WaitForPage(window, "tasks");
            await MaterialControlsPreview.Layout(window);
            RichUiAudit.Shot(window, folder, "tasks-progress-" + dark);
            var tasks = RichUiAudit.Find<TasksPage>(window).Single();
            foreach (var text in RichUiAudit.Find<TextBlock>(tasks).Where(text => text.Text.Contains("正在下载并校验")))
                InteractionAudit.Check(text.TextWrapping == TextWrapping.Wrap || text.TextTrimming != TextTrimming.None, "long task status wraps or provides ellipsis");
            window.Navigate("version", instant: true); await Program.WaitForPage(window, "version");
        }
        TaskStore.Handle(new BridgeEvent { Event = "finished", TaskId = taskId, Success = false, Message = "已取消" });
        await MaterialControlsPreview.Layout(window);
        RichUiAudit.Shot(window, folder, "install-cancelled");
        var game = new Instance("layout-retry-fixture", true); game.Create();
        string failed = await AppServices.Client.StartTaskAsync("repair_version", new { instance = game.name, version = "not-installed" });
        await AppServices.Client.CallAsync("wait_task", new { task_id = failed, timeout = 15 });
        await MaterialControlsPreview.Until(() => TaskStore.Get(failed)?.Finished == true, "real failed task reaches UI");
        RichUiAudit.Invoke(page, "TrackInstall", failed, game.name, "not-installed");
        await MaterialControlsPreview.Layout(window);
        RichUiAudit.Field<SPanel>(page, "_installActivity").BringIntoView();
        await MaterialControlsPreview.Layout(window);
        RichUiAudit.Shot(window, folder, "install-failed");
        var retry = RichUiAudit.Field<Button>(page, "_retryInstall");
        InteractionAudit.Check(InteractionAudit.Inside(retry, page), "failed installation retry is visible at 800 DIP");
        retry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await MaterialControlsPreview.Until(() => page.VisibleInstallTask != failed, "retry creates a new backend task");
        string next = page.VisibleInstallTask;
        InteractionAudit.Check(DownloadRetries.Find(failed) == null && DownloadRetries.Find(next)?.ArgumentsJson.Contains("not-installed") == true,
            "retry preserves target and retires old retry request");
        await AppServices.Client.CallAsync("wait_task", new { task_id = next, timeout = 15 });
        window.Navigate("tasks", instant: true); await Program.WaitForPage(window, "tasks");
        await MaterialControlsPreview.Layout(window);
        RichUiAudit.Shot(window, folder, "tasks-retry");
        var tasksPage = RichUiAudit.Find<TasksPage>(window).Single();
        InteractionAudit.Check(!RichUiAudit.Find<Button>(tasksPage).Any(button => button.IsVisible && System.Windows.Automation.AutomationProperties.GetName(button) == "崩溃分析"),
            "cancelled downloads and ordinary failures do not advertise nonexistent crash reports");
        const string crashId = "crash-report-layout-fixture";
        TaskStore.Handle(new BridgeEvent { Event = "task_added", TaskId = crashId, Title = "启动游戏 · 崩溃报告验收" });
        TaskStore.Handle(new BridgeEvent { Event = "crash", TaskId = crashId, Crash = new CrashReport { Title = "模组不兼容" } });
        TaskStore.Handle(new BridgeEvent { Event = "finished", TaskId = crashId, Success = false, Message = "模组不兼容" });
        await MaterialControlsPreview.Layout(window);
        InteractionAudit.Check(RichUiAudit.Find<Button>(tasksPage).Count(button => button.IsVisible && System.Windows.Automation.AutomationProperties.GetName(button) == "崩溃分析") == 1,
            "a task with a crash report retains its analysis action");
        Console.WriteLine("[INSTALL LIMIT] Progress/cancellation use UI event fixtures. Missing-version failure and retry use real isolated backend tasks. No pause API exists.");
    }
}
