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

internal sealed record DesktopAiProfile(string Mode, string Endpoint, string Model, string ApiKey)
{
    private const string KeyService = "HelL desktop AI provider";
    private static readonly string[] Fields = ["desktop_ai_mode", "desktop_ai_url", "desktop_ai_model", "desktop_ai_key_id"];

    internal static DesktopAiProfile Read(LauncherConfig config, ISecretStore store)
    {
        bool hasDesktop = !string.IsNullOrEmpty(Py.S(config.Get("desktop_ai_mode")));
        if (!hasDesktop) return new("ollama", "http://127.0.0.1:11434/v1", "qwen2.5:7b", "ollama");
        string mode = Py.S(config.Get("desktop_ai_mode", "ollama"));
        if (mode is not ("custom" or "ollama" or "public")) mode = "ollama";
        string fallbackUrl = mode == "ollama" ? "http://127.0.0.1:11434/v1" : "";
        string url = Py.S(config.Get("desktop_ai_url", fallbackUrl));
        if (string.IsNullOrWhiteSpace(url) && mode == "ollama") url = fallbackUrl;
        string model = Py.S(config.Get("desktop_ai_model", mode == "ollama" ? "qwen2.5:7b" : ""));
        string id = Py.S(config.Get("desktop_ai_key_id"));
        string key = mode is "public" or "ollama" ? "" : (id.Length > 0 ? store.GetPassword(KeyService, id) : "");
        return Validate(mode, url, model, key);
    }

    internal static DesktopAiProfile Validate(string mode, string endpoint, string model, string key)
    {
        if (mode is not ("custom" or "ollama" or "public"))
            throw new ArgumentException("请选择有效的模型服务类型。");
        endpoint = endpoint.Trim().TrimEnd('/');
        model = model.Trim();
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.UserInfo.Length != 0
            || uri.Query.Length != 0 || uri.Fragment.Length != 0 || string.IsNullOrEmpty(uri.Host))
            throw new ArgumentException("请填写完整的服务地址。");
        bool localHttp = mode == "ollama" && uri.Scheme == "http" && uri.IsLoopback;
        if (uri.Scheme != "https" && !localHttp)
            throw new ArgumentException("云端端点必须使用 HTTPS；仅本地 Ollama 允许本机回环地址。");
        if (model.Length is < 1 or > 256 || model.Any(char.IsControl))
            throw new ArgumentException("请填写有效的模型名称。");
        if (mode == "custom" && (string.IsNullOrWhiteSpace(key) || key.Length > 4096 || key.Any(char.IsControl)))
            throw new ArgumentException("请填写有效的 API 密钥。");
        return new(mode, endpoint, model, mode == "custom" ? key.Trim() : (mode == "ollama" ? "ollama" : ""));
    }

    internal PyDict RequestSettings() => PyDict.Of(
        "ai_mode", Mode == "public" ? "public" : "custom",
        "ai_base_url", Endpoint,
        "ai_gateway_url", Endpoint,
        "ai_model", Model,
        "ai_api_key", ApiKey,
        "ai_max_tokens", 4096L);

    internal static void Save(LauncherConfig config, ISecretStore store, DesktopAiProfile profile)
    {
        profile = Validate(profile.Mode, profile.Endpoint, profile.Model, profile.ApiKey);
        string id = profile.Mode == "custom" ? Guid.NewGuid().ToString("N") : "";
        string previousId = Py.S(config.Get("desktop_ai_key_id"));
        if (id.Length > 0)
        {
            store.SetPassword(KeyService, id, profile.ApiKey);
            if (store.GetPassword(KeyService, id) != profile.ApiKey)
                throw new IOException("系统凭据库未能验证 API 密钥。");
        }
        config.Update(PyDict.Of(Fields[0], profile.Mode, Fields[1], profile.Endpoint, Fields[2], profile.Model, Fields[3], id));
        config.Save();
        if (previousId.Length > 0)
        {
            try { store.DeletePassword(KeyService, previousId); } catch { }
        }
    }
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
