using System.Text.Json;
using HelL.Desktop.Models;

namespace HelL.Desktop.Services;

internal static class DesktopTaskHistory
{
    private const int MaxHistoryBytes = 2 * 1024 * 1024;
    private sealed record Saved(string Title, DesktopTaskState State, string Message, string[] Lines, DateTimeOffset? StartedAt = null, DateTimeOffset? FinishedAt = null);
    internal static string? Save(string root, IEnumerable<DesktopTask> tasks)
    {
        string path = Path.Combine(root, "desktop-task-history.json");
        string temporary = Path.Combine(root, ".desktop-task-history-" + Guid.NewGuid().ToString("N") + ".tmp");
        var candidates = tasks.Where(task => !task.IsActive).Take(100).Select(task => new Saved(
            task.Title.Length > 160 ? task.Title[..160] : task.Title, task.State,
            Bounded(task.Message, 1024), task.Log.TakeLast(80).Select(line => Bounded(line, 512)).ToArray(), task.StartedAt, task.FinishedAt));
        var saved = new List<Saved>();
        int encodedBytes = 2;
        foreach (Saved entry in candidates)
        {
            int size = JsonSerializer.SerializeToUtf8Bytes(entry).Length + (saved.Count == 0 ? 0 : 1);
            if (encodedBytes + size > MaxHistoryBytes) break;
            saved.Add(entry);
            encodedBytes += size;
        }
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(saved);
        Directory.CreateDirectory(root);
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(temporary, options)) { stream.Write(payload); stream.Flush(true); }
            string? archived = ArchiveInvalidHistory(root, path);
            File.Move(temporary, path, overwrite: true);
            return archived;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static string Bounded(string text, int limit)
    {
        string redacted = LogPreview.Redact(text);
        return redacted.Length <= limit ? redacted : redacted[..limit];
    }

    private static string? ArchiveInvalidHistory(string root, string path)
    {
        if (!File.Exists(path)) return null;
        try { Read(root); return null; }
        catch (Exception error) when (error is JsonException or InvalidDataException or ArgumentException)
        {
            // Move the original bytes in the same directory, retaining its permissions and evidence.
            string backup = Path.Combine(root, "desktop-task-history." + Guid.NewGuid().ToString("N") + ".corrupt");
            File.Move(path, backup);
            return Path.GetFileName(backup);
        }
    }
    internal static DesktopTask[] Read(string root)
    {
        string path = Path.Combine(root, "desktop-task-history.json");
        if (!File.Exists(path)) return [];
        if (new FileInfo(path).Length > MaxHistoryBytes) throw new InvalidDataException("任务历史过大，请备份后移除损坏的记录。");
        Saved[] saved = JsonSerializer.Deserialize<Saved[]>(File.ReadAllBytes(path)) ?? throw new InvalidDataException("任务历史格式无效。");
        if (saved.Length > 100 || saved.Any(item => item is null || string.IsNullOrWhiteSpace(item.Title) || item.Title.Length > 160
            || item.Message is null || item.Message.Length > 1024 || item.Lines is null
            || item.Lines.Length > 80 || item.Lines.Any(line => line is null || line.Length > 512)
            || item.State is not (DesktopTaskState.Completed or DesktopTaskState.Cancelled or DesktopTaskState.Failed)))
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
