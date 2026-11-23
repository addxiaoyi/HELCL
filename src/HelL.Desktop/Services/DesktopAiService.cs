using System.Text;
using Avalonia;
using Avalonia.Layout;
using Avalonia.Media;
using HelL.Core;
using HelL.Core.Launcher;
using HelL.Core.Launcher.Ai;
using HelL.Core.Platform;
using HelL.Portable;

namespace HelL.Desktop.Services;

internal sealed record DesktopAiMessage(string Role, string Content, string State)
{
    private static readonly IBrush UserBg = new SolidColorBrush(Color.Parse("#228E8E93"));
    private static readonly IBrush BotBg = Brushes.Transparent;

    public string Speaker => Role == "user" ? "你" : "助手";
    public bool IsUser => Role == "user";
    public bool IsAssistant => Role != "user";
    public HorizontalAlignment BubbleAlign => IsUser ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
    public Thickness BubbleMargin => IsUser ? new Thickness(80, 0, 0, 0) : new Thickness(0, 0, 24, 0);
    public IBrush BubbleBg => IsUser ? UserBg : BotBg;
    public string StateHint => State switch
    {
        "streaming" => "生成中…",
        "cancelled" => "已取消",
        "failed" => "未完成",
        _ => ""
    };
}

internal sealed class DesktopAiSession
{
    private const int MaxPrompt = 12000;
    private const int MaxReply = 120000;
    private const int HistoryLimit = 24;
    private readonly object gate = new();
    private readonly List<DesktopAiMessage> history = [];
    public IReadOnlyList<DesktopAiMessage> Entries { get { lock (gate) return history.ToArray(); } }
    public ISecretStore Secrets { get; } = SystemSecretStore.Create();
    private bool busy;

    public void Clear()
    {
        lock (gate)
        {
            if (busy) throw new InvalidOperationException("请先停止当前回复。");
            history.Clear();
        }
    }

    public void Send(DesktopAiProfile profile, string prompt, Action changed, CancellationToken token)
    {
        prompt = prompt.Trim();
        if (prompt.Length is < 1 or > MaxPrompt) throw new ArgumentException("请输入 1–12000 个字符。");
        profile = DesktopAiProfile.Validate(profile.Mode, profile.Endpoint, profile.Model, profile.ApiKey);
        token.ThrowIfCancellationRequested();
        List<object> messages;
        int replyIndex;
        lock (gate)
        {
            if (busy) throw new InvalidOperationException("请等待当前回复完成。");
            busy = true;
            messages = history.Where(entry => entry.State == "complete").TakeLast(HistoryLimit)
                .Select(entry => (object)PyDict.Of("role", entry.Role, "content", entry.Content)).ToList();
            messages.Insert(0, PyDict.Of("role", "system", "content",
                "你是 HelL 启动器的 Minecraft 顾问。请用简洁清晰的中文协助用户排查日志、配置模组与优化启动参数。"));
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
                    if (text.Length > MaxReply) throw new InvalidDataException("回复内容过长。");
                    Update("streaming");
                }
                else if (kind == "done") done = true;
                else if (kind == "error") throw new InvalidOperationException("模型服务未完成回复。");
            }
            token.ThrowIfCancellationRequested();
            if (!done || text.Length == 0) throw new InvalidDataException("模型未返回有效回复。");
            Update("complete");
        }
        catch (Exception) when (token.IsCancellationRequested) { Update("cancelled"); throw new OperationCanceledException(token); }
        catch { Update("failed"); throw; }
        finally
        {
            http.Abort();
            lock (gate) busy = false;
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
