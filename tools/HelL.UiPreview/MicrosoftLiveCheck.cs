using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using HelL;
using HelL.Core;
using HelL.Core.Launcher;
using HelL.Pages;
using HelL.Services;

internal static class MicrosoftLiveCheck
{
    internal static async Task Run(MainWindow window, string readyHome)
    {
        string evidence = Path.Combine(readyHome, "microsoft-verification.json");
        void Record(string stage, string message = "") => File.WriteAllText(evidence,
            JsonSerializer.Serialize(new { stage, message, recordedAtUtc = DateTime.UtcNow }, new JsonSerializerOptions { WriteIndented = true }));
        Record("starting");
        var api = AppServices.Client;
        var completed = new TaskCompletionSource<bool>();
        string failure = "";
        string? loginTask = null;
        void LoginEvent(object? sender, BridgeEvent ev)
        {
            if (ev.Event == "login_code") Record("waiting-for-user", "请在测试启动器的微软登录弹窗中查看代码并完成授权。");
            if (ev.Event == "finished" && ev.TaskId == loginTask)
            {
                if (!ev.Success) failure = ev.Message;
                completed.TrySetResult(ev.Success);
            }
        }
        api.EventReceived += LoginEvent;
        GameProcess? game = null;
        AccountManager? accounts = null;
        try
        {
            window.Title = "HelL · 微软登录实测（隔离配置）";
            window.Navigate("account", instant: true);
            await Program.WaitForPage(window, "account");
            var page = Walk(window).OfType<AccountPage>().Single();
            await (Task)typeof(AccountPage).GetMethod("MicrosoftLoginAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(page, null)!;
            loginTask = (string?)typeof(AccountPage).GetField("_loginTask", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page);
            DateTime deadline = DateTime.UtcNow.AddMinutes(15);
            while (!completed.Task.IsCompleted && DateTime.UtcNow < deadline)
            {
                accounts = new AccountManager();
                if (accounts.GetActive()?.Get("type") as string == "microsoft") break;
                await Task.Delay(1000);
            }
            accounts = new AccountManager();
            var account = accounts.GetActive();
            if (account?.Get("type") as string != "microsoft")
                throw new InvalidOperationException(failure.Length > 0 ? failure : "尚未完成微软账号授权，正版启动和令牌刷新未验证。");
            Record("refreshing", "微软登录及正版资格检查已通过，正在验证真实刷新。");
            account["expires_at"] = 0L;
            account = await Task.Run(() => accounts.EnsureValid(account));
            if (Py.Int(account.Get("expires_at")) <= Py.Time()) throw new IOException("刷新后的令牌有效期无效。");
            Record("launching", "真实令牌刷新已通过，正在启动正版游戏。");
            using var prepared = JsonDocument.Parse(File.ReadAllText(Path.Combine(readyHome, "verification.json")));
            string version = prepared.RootElement.GetProperty("version").GetString()!;
            string java = prepared.RootElement.GetProperty("java").GetString()!;
            var instance = new Instance("verify-demo");
            var launch = McLauncher.BuildLaunchCommand(instance, version, accounts.LaunchProps(account), java,
                memory_mb: 2048L, width: 854L, height: 480L);
            if (launch.cmd.Contains("--demo")) throw new InvalidOperationException("正版验收不能带演示模式参数。");
            bool textures = false;
            game = new GameProcess(launch.cmd, launch.game_dir, line => { if (line.Contains("textures/atlas")) textures = true; });
            deadline = DateTime.UtcNow.AddMinutes(3);
            while (DateTime.UtcNow < deadline)
            {
                if (game.Poll().HasValue) throw new IOException("正版游戏在加载完成前退出。");
                game.proc.Refresh();
                if (game.proc.MainWindowHandle != IntPtr.Zero && textures) break;
                await Task.Delay(500);
            }
            if (game.proc.MainWindowHandle == IntPtr.Zero || !textures) throw new TimeoutException("未检测到加载完成的正版游戏窗口。");
            await Task.Delay(10000);
            if (game.Poll().HasValue) throw new IOException("正版游戏在首帧后退出。");
            Record("passed", "微软登录、正版资格、真实令牌刷新及非演示模式游戏窗口均已验证。");
            Console.WriteLine("[MICROSOFT LIVE] Login, refresh and non-demo game rendering passed.");
        }
        catch (Exception ex) { Record("blocked", ex.Message); throw; }
        finally
        {
            api.EventReceived -= LoginEvent;
            if (game != null)
            {
                if (!game.proc.HasExited && (!game.proc.CloseMainWindow() || !game.proc.WaitForExit(15000))) game.Kill();
                game.Wait(); game.proc.Dispose();
            }
            if (loginTask != null) await api.CallAsync("cancel_task", new { task_id = loginTask });
            accounts ??= new AccountManager();
            foreach (var account in accounts.accounts.OfType<PyDict>().ToArray()) accounts.RemoveAccount(account.Get("name"));
        }
    }

    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Walk(child)) yield return nested; }
    }
}
