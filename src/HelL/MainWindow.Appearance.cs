using System;
using System.Diagnostics;
using System.Threading.Tasks;
using HelL.Core.Launcher;
using HelL.Services;

namespace HelL;

public partial class MainWindow
{
    public async Task ReloadAppearanceAsync()
    {
        await SyncAppearanceFromBridgeAsync();
        await Wallpaper.ReloadAsync();
    }

    public void RestartForAppearance()
    {
        if (TaskStore.RunningCount > 0)
        {
            Toast(I18n.L("任务仍在运行"), I18n.L("请等待当前任务完成后，再重启应用外观。"), ToastKind.Warning);
            return;
        }
        _restartForAppearance = true;
        Close();
    }

    private static void RestartAppearanceProcess()
    {
        try
        {
            string executable = Process.GetCurrentProcess().MainModule?.FileName
                ?? throw new InvalidOperationException("Cannot locate the running launcher executable.");
            // Direct launch retains the same profile environment and elevation as this process.
            using var replacement = Process.Start(new ProcessStartInfo
            {
                FileName = executable, WorkingDirectory = Environment.CurrentDirectory,
                UseShellExecute = false, CreateNoWindow = true
            }) ?? throw new InvalidOperationException("Windows did not start the replacement launcher.");
            Guard.WriteLog("appearance-restart", "Started replacement process " + replacement.Id);
        }
        catch (Exception error)
        {
            Guard.WriteLog("appearance-restart", error.ToString());
            Smoke.Note("appearance-restart", error.ToString());
        }
    }
}
