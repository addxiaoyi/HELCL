using System.Text.Json;
using HelL.Desktop.Models;

namespace HelL.Desktop.Services;

internal static class DesktopTaskHistory
{
    private sealed record Saved(string Title, DesktopTaskState State, string Message, string[] Lines, DateTimeOffset? StartedAt = null, DateTimeOffset? FinishedAt = null);
    internal static void Save(string root, IEnumerable<DesktopTask> tasks)
    {
        string path = Path.Combine(root, "desktop-task-history.json"), temporary = path + ".tmp";
        var saved = tasks.Where(task => !task.IsActive).Take(100).Select(task => new Saved(
            task.Title.Length > 160 ? task.Title[..160] : task.Title, task.State,
            LogPreview.Redact(task.Message), task.Log.TakeLast(80).Select(LogPreview.Redact).ToArray(), task.StartedAt, task.FinishedAt)).ToArray();
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(saved));
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    internal static DesktopTask[] Read(string root)
    {
        string path = Path.Combine(root, "desktop-task-history.json");
        if (!File.Exists(path)) return [];
        if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new InvalidDataException("任务历史过大，请备份后移除损坏的记录。");
        Saved[] saved = JsonSerializer.Deserialize<Saved[]>(File.ReadAllBytes(path)) ?? throw new InvalidDataException("任务历史格式无效。");
        if (saved.Length > 100 || saved.Any(item => item is null || item.Title is null || item.Message is null || item.Lines is null
            || item.Lines.Length > 80 || item.State is not (DesktopTaskState.Completed or DesktopTaskState.Cancelled or DesktopTaskState.Failed)))
            throw new InvalidDataException("任务历史包含无效状态。");
        return saved.Select(item =>
        {
            var task = new DesktopTask(item.Title, item.StartedAt);
            foreach (string line in item.Lines) task.Log.Add(line);
            task.Finish(item.State, item.Message);
            task.RestoreFinishedAt(item.FinishedAt);
            return task;
        }).ToArray();
    }
}
