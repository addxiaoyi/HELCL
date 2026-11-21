using HelL.Core.Launcher;
using HelL.Portable;

namespace HelL.Desktop;

public sealed partial class MainWindow
{
    internal Func<string, string, string, CancellationToken, Task<InstallationPlan>>? InstallationPlanFixture { get; set; }
    internal Func<string, string, string, string?, CancellationToken, Task<string>>? InstallFixture { get; set; }

    internal async Task InstallWithConfirmationAsync(string instance, string version, string loader, string? build,
        Func<bool> selectionCurrent, Action<string>? installedAction = null)
    {
        if (loader != "none" && string.IsNullOrWhiteSpace(build))
        {
            SetStatus("请先获取并选择与当前 Minecraft 版本兼容的加载器构建。");
            return;
        }

        // Keep the task gate through preparation and approval so the target cannot be renamed and recreated.
        await RunTask(async token =>
        {
            RequireCurrentSelection();
            string folder = launcher.InstanceFolder(instance);
            SetStatus("正在准备安装计划…");
            InstallationPlan? plan = null;
            using (var planning = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                planning.CancelAfter(TimeSpan.FromSeconds(20));
                try
                {
                    plan = InstallationPlanFixture is { } prepare ? await prepare(instance, version, loader, planning.Token)
                        : await Task.Run(() => InstallationPlan.ForVersion(new Instance(instance), version, loader, planning.Token), planning.Token);
                }
                catch (Exception error) when (error is not OutOfMemoryException && !token.IsCancellationRequested)
                {
                    System.Diagnostics.Trace.TraceWarning("Desktop installation plan: " + error.GetType().Name);
                }
            }
            token.ThrowIfCancellationRequested();
            RequireCurrentSelection();

            string summary = $"目标实例：{instance}\n目标目录：{folder}\nMinecraft 版本：{version}\n"
                + $"加载器：{loader}\n加载器构建：{(loader == "none" ? "不适用" : build)}\n\n";
            summary += plan is null
                ? "未能读取安装清单，下载量与额外占用未知。继续后仍会校验实际下载文件。"
                : "以下是基础版本的目录与大小估算；加载器另建的启动配置和额外文件不计入该估算。\n" + plan.Summary;
            if (plan?.SpaceKnownInsufficient == true)
                throw new IOException(summary);

            if (!await ConfirmAction("确认安装计划", summary, "开始安装"))
            {
                task?.Cancel();
                token.ThrowIfCancellationRequested();
            }
            token.ThrowIfCancellationRequested();
            RequireCurrentSelection();

            var progress = CreateProgress();
            string installed = InstallFixture is { } install ? await install(instance, version, loader, build, token)
                : await Task.Run(() => launcher.Install(instance, version, loader, build, progress, token), token);
            token.ThrowIfCancellationRequested();
            ReloadInstances(instance);
            installedAction?.Invoke(installed);
            SetStatus($"已安装 {installed} 到「{instance}」。");
        }, "InstallVersion");

        void RequireCurrentSelection()
        {
            if (!selectionCurrent())
                throw new InvalidOperationException("安装选项已改变，请按当前目标重新确认安装计划。");
        }
    }
}
