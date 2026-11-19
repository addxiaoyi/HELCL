using System;
using System.Linq;
using System.Threading.Tasks;
using HelL.Models;
using HelL.Services;

namespace HelL.Pages;

public sealed partial class LaunchPage
{
    private async Task<PreflightResult?> CheckAndRepairLaunch(string instance, string version, int memory, string java)
    {
        while (true)
        {
            var check = await Api.CallAsync<PreflightResult>("preflight_launch", new { instance, version, memory_mb = memory, java });
            if (check == null) throw new InvalidOperationException(I18n.L("启动检测没有返回结果，请重试。"));
            var errors = check.Items.Where(item => item.Level == "error").ToList();
            if (errors.Count == 0) return check;
            bool javaIssue = errors.Any(item => item.Code.StartsWith("java_", StringComparison.Ordinal));
            bool fileIssue = errors.Any(item => item.Code is "libs_missing" or "libs_hash" or "client_corrupt" or "assets_missing" or "assets_index_missing" or "no_client_jar");
            if (!javaIssue && !fileIssue) return check;
            string label = I18n.L(javaIssue ? "自动匹配 Java 并重新检测" : "修复文件并重新检测");
            string details = string.Join("\n\n", errors.Select(item => "· " + I18n.L(item.Title) + "\n" + item.Detail));
            if (!await Dlg.Confirm(I18n.L("启动环境需要修复"), details, label, I18n.L("返回修改"))) return null;
            _status.Text = I18n.L("正在修复启动环境…");
            string task = await Api.StartTaskAsync("repair_launch", new { instance, version, repair = javaIssue ? "java" : "files" });
            var outcome = await Api.CallAsync<OpResult>("wait_task", new { task_id = task, timeout = 1800 });
            if (outcome?.Ok != true)
            {
                if (!await Dlg.Confirm(I18n.L("修复未完成"), outcome?.Message ?? I18n.L("请查看下载任务中的失败原因。"), I18n.L("重新检测"), I18n.L("返回修改"))) return null;
            }
        }
    }
}
