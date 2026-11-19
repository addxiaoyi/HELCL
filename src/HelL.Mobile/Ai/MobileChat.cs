using System.Text;
using System.Text.Json;
using HelL.Core;
using HelL.Core.Launcher.Ai;

namespace HelL.Mobile.Ai;

internal sealed record ChatEntry(string Role, string Text, string State)
{
    public string Speaker => Role == "user" ? "你" : "AI";
    public string Content => Text;
    public string Notice => State switch { "streaming" => "正在回复…", "cancelled" => "已停止", "failed" => "回复未完成", _ => "" };
}

internal sealed class MobileChat
{
    private const int MaxPrompt = 8000;
    private const int MaxReply = 120000;
    private const int HistoryLimit = 24;
    private readonly object gate = new();
    private readonly List<ChatEntry> history = [];
    private ChatEntry[]? cleared;
    internal IReadOnlyList<ChatEntry> Entries { get { lock (gate) return history.ToArray(); } }

    private bool busy;
    internal void Restore(string path)
    {
        lock (gate)
        {
            if (history.Count != 0 || !File.Exists(path)) return;
            if (new FileInfo(path).Length > 512 * 1024) throw new InvalidDataException("聊天记录超过允许大小，原文件已保留。");
            ChatEntry[] saved = JsonSerializer.Deserialize<ChatEntry[]>(File.ReadAllText(path)) ?? throw new InvalidDataException("聊天记录格式无效，原文件已保留。");
            if (saved.Length > HistoryLimit * 2 || saved.Any(entry => entry is null || entry.Role is not ("user" or "assistant")
                || entry.Text is null || entry.Text.Length > MaxReply || entry.State is not ("complete" or "failed" or "cancelled" or "streaming")))
                throw new InvalidDataException("聊天记录内容无效，原文件已保留。");
            history.AddRange(saved.Select(entry => entry.State == "streaming" ? entry with { State = "cancelled" } : entry));
        }
    }

    internal void Save(string path)
    {
        string json;
        lock (gate) json = JsonSerializer.Serialize(history.TakeLast(HistoryLimit * 2));
        if (Encoding.UTF8.GetByteCount(json) > 512 * 1024) throw new IOException("聊天记录过大，请清理部分内容后重试。");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, json);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal bool UndoClear()
    {
        lock (gate)
        {
            if (busy || cleared is null || history.Count != 0) return false;
            history.AddRange(cleared); cleared = null; return true;
        }
    }
    internal void Clear()
    {
        lock (gate)
        {
            if (busy) throw new InvalidOperationException("请先停止当前回复。");
            cleared = history.ToArray();
            history.Clear();
        }
    }

    internal void Send(MobileAiProfile profile, string prompt, Action changed, CancellationToken token)
    {
        prompt = prompt.Trim();
        if (prompt.Length is < 1 or > MaxPrompt) throw new ArgumentException("请输入 1–8000 个字符的消息。");
        profile = MobileAiProfile.Validate(profile.Mode, profile.Endpoint, profile.Model, profile.ApiKey);
        token.ThrowIfCancellationRequested();
        List<object> messages;
        int replyIndex;
        lock (gate)
        {
            if (busy) throw new InvalidOperationException("请等待当前回复完成，或先停止。");
            busy = true;
            messages = history.Where(entry => entry.State == "complete").TakeLast(HistoryLimit)
                .Select(entry => (object)PyDict.Of("role", entry.Role, "content", entry.Text)).ToList();
            messages.Insert(0, PyDict.Of("role", "system", "content", "你是 HelL 的 Minecraft 文字助手。提供建议和解释。当前会话没有文件、命令、MCP、主机或游戏操作工具，请不要声称已经执行操作。"));
            messages.Add(PyDict.Of("role", "user", "content", prompt));
            history.Add(new("user", prompt, "complete"));
            history.Add(new("assistant", "", "streaming"));
            replyIndex = history.Count - 1;
        }
        var text = new StringBuilder();
        using var http = new HttpCancel();
        using var registration = token.Register(() => ThreadPool.QueueUserWorkItem(_ => http.Abort()));
        bool done = false;
        try
        {
            changed();
            foreach (var frame in Client.ChatStream(profile.RequestSettings(), messages, tools: [], httpCancel: http))
            {
                token.ThrowIfCancellationRequested();
                string kind = Py.S(frame.Get("type"));
                if (kind == "delta")
                {
                    text.Append(Py.S(frame.Get("text")));
                    if (text.Length > MaxReply) throw new InvalidDataException("回复超过显示上限，请缩短问题后重试。");
                    Update("streaming");
                }
                else if (kind == "done") done = true;
                else if (kind == "tool_calls") throw new InvalidOperationException("模型请求了工具操作。此页面仅支持文字对话，没有执行任何操作。");
                else if (kind == "error") throw new InvalidOperationException("模型服务未完成流式回复，请稍后重试。");
            }
            token.ThrowIfCancellationRequested();
            if (!done || text.Length == 0) throw new InvalidDataException("模型服务没有返回完整文字回复，请检查模型设置后重试。");
            Update("complete");
        }
        catch (Exception) when (token.IsCancellationRequested) { Update("cancelled"); throw new OperationCanceledException(token); }
        catch { Update("failed"); throw; }
        finally
        {
            http.Abort();
            lock (gate)
            {
                busy = false;
                if (history.Count > HistoryLimit * 2) history.RemoveRange(0, history.Count - HistoryLimit * 2);
            }
        }

        void Update(string state)
        {
            lock (gate)
            {
                history[replyIndex] = new("assistant", text.ToString(), state);
                if (state is "failed" or "cancelled") history[replyIndex - 1] = history[replyIndex - 1] with { State = state };
            }
            changed();
        }
    }
}
