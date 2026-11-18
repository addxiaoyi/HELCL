using System.Text;
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
    internal IReadOnlyList<ChatEntry> Entries { get { lock (gate) return history.ToArray(); } }

    private bool busy;
    internal void Clear()
    {
        lock (gate)
        {
            if (busy) throw new InvalidOperationException("请先停止当前回复。");
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
