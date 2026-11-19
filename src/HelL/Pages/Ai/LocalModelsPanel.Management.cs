using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using HelL.Core.Launcher.Ai.Local;
using HelL.Services;

namespace HelL.Pages.Ai;

public sealed partial class LocalModelsPanel
{
    private async Task SaveToolMode()
    {
        await Api.CallAsync("ai_local_tool_mode", new { enabled = _toolMode.IsChecked == true });
        Feedback(_toolMode.IsChecked == true ? "本地工具助手已启用，操作仍需确认。" : "已切回本地文字聊天。");
    }

    private async Task DeleteModel(LocalModelInfo model)
    {
        if (!await Dlg.Confirm("删除本地模型", "删除 " + model.Name + "（" + ModelRules.FormatBytes(model.Size) + "）？\n模型权重由 Ollama 管理，共用的层可能仍被其他模型使用。聊天记录会保留。", "删除模型", danger: true)) return;
        await Api.CallAsync("ai_local_delete", new { model = model.Name });
        await RefreshRuntime(false);
        Feedback("模型已删除：" + model.Name);
    }

    private sealed class QueueState
    {
        public List<ModelJob> Jobs { get; set; } = new List<ModelJob>();
        public string Error { get; set; } = "";
    }

    private async Task RenderQueue()
    {
        var queue = await Api.CallAsync<QueueState>("ai_local_queue") ?? throw new InvalidOperationException("无法读取下载队列。");
        if (queue.Error.Length > 0) throw new InvalidOperationException(queue.Error);
        _rows.Children.Clear();
        _rows.Children.Add(Ui.Muted("下载按顺序执行。重启后自动恢复；本地服务未启动时等待连接。取消的任务需手动重试。").M(0, 4, 0, 12));
        if (queue.Jobs.Count == 0) _rows.Children.Add(Ui.Muted("队列为空，前往推荐下载或上游仓库添加模型。"));
        foreach (var job in queue.Jobs.AsEnumerable().Reverse())
        {
            var action = Ui.H(4);
            if (ModelQueue.IsPending(job.State)) action.Children.Add(Ui.Btn("取消", BtnKind.Ghost, (_, _) => RunUi(async () => { await Api.CallAsync("ai_local_cancel", new { id = job.Id }); await RenderQueue(); })));
            if (job.State is "failed" or "cancelled") action.Children.Add(Ui.Btn("重试", BtnKind.Chip, (_, _) => RunUi(async () => { await Api.CallAsync("ai_local_retry", new { id = job.Id }); await RenderQueue(); })));
            string state = job.State switch { "queued" => "排队中", "running" => "进行中", "waiting" => "等待服务", "completed" => "已完成", "failed" => "失败", "cancelled" => "已取消", _ => "正在取消" };
            _rows.Children.Add(Row(job.Title, state + " · " + job.Message + (job.Total > 0 ? "\n" + ModelRules.FormatBytes(job.Completed) + " / " + ModelRules.FormatBytes(job.Total) : ""), action));
        }
    }

    private async Task RenderStorage()
    {
        var storage = await Api.CallAsync<ModelStorageInfo>("ai_local_storage") ?? throw new InvalidOperationException("无法统计模型存储。");
        _rows.Children.Clear();
        _rows.Children.Add(Ui.Muted("模型目录 " + ModelRules.FormatBytes(storage.ModelBytes) + " · 运行环境 " + ModelRules.FormatBytes(storage.RuntimeBytes)
            + " · 下载缓存 " + ModelRules.FormatBytes(storage.CacheBytes) + " · 磁盘剩余 " + ModelRules.FormatBytes(storage.FreeBytes)).M(0, 4, 0, 12));
        _rows.Children.Add(Ui.Muted("服务中模型标称体积 " + ModelRules.FormatBytes(storage.InstalledModelBytes) + "（共用权重可能重复计算）\n" + storage.ModelsPath + "\n磁盘占用按本机已知目录统计；独立服务自定义的目录可能不同。").M(0, 0, 0, 12));
        var checks = new List<(CheckBox Check, ModelCacheFile File)>();
        foreach (var file in storage.Cache)
        {
            var check = Ui.Check((file.Partial ? "未完成下载" : "完整缓存") + " · " + ModelRules.FormatBytes(file.Size));
            checks.Add((check, file));
            _rows.Children.Add(Ui.V(4, check, Ui.Txt(file.Id, 11, false, "B.InkMuted").Wrap()).M(0, 4, 0, 10));
        }
        if (checks.Count == 0) { _rows.Children.Add(Ui.Muted("没有可清理的下载缓存。")); return; }
        _rows.Children.Add(Ui.Btn("清理所选缓存", BtnKind.Chip, (_, _) => RunUi(async () =>
        {
            var selected = checks.Where(entry => entry.Check.IsChecked == true).Select(entry => entry.File).ToList();
            if (selected.Count == 0) throw new InvalidOperationException("请先选择缓存文件。");
            if (!await Dlg.Confirm("清理缓存", "清理 " + selected.Count + " 个文件，共 " + ModelRules.FormatBytes(selected.Sum(file => file.Size)) + "？\n已导入的模型可以继续使用。未完成下载将从头开始。", "清理", danger: true)) return;
            await Api.CallAsync("ai_local_clean_cache", new { files = selected });
            await RenderStorage();
            Feedback("所选缓存已清理。");
        })).Left());
    }

}
