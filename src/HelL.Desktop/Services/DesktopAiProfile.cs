using HelL.Core;
using HelL.Core.Launcher;
using HelL.Core.Platform;

namespace HelL.Desktop.Services;

internal sealed record DesktopAiProfile(string Mode, string Endpoint, string Model, string ApiKey)
{
    public override string ToString() => Mode + ":" + Endpoint + ":" + Model;
    private const string KeyService = "HelL desktop AI provider";
    private static readonly string[] Fields = ["desktop_ai_mode", "desktop_ai_url", "desktop_ai_model", "desktop_ai_key_id"];

    internal static DesktopAiProfile Read(LauncherConfig config, ISecretStore store)
    {
        lock (config._lock) return ReadStored(config, store);
    }

    private static DesktopAiProfile ReadStored(LauncherConfig config, ISecretStore store)
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
        lock (config._lock)
        {
            string id = profile.Mode == "custom" ? Guid.NewGuid().ToString("N") : "";
            string previousId = Py.S(config.Get("desktop_ai_key_id"));
            bool committed = false;
            try
            {
                if (id.Length > 0)
                {
                    store.SetPassword(KeyService, id, profile.ApiKey);
                    if (store.GetPassword(KeyService, id) != profile.ApiKey)
                        throw new IOException("系统凭据库未能验证 API 密钥，原配置保留。");
                }
                config.Commit(PyDict.Of(Fields[0], profile.Mode, Fields[1], profile.Endpoint, Fields[2], profile.Model, Fields[3], id));
                committed = true;
            }
            finally
            {
                string discard = committed ? previousId : id;
                if (discard.Length > 0)
                {
                    try { store.DeletePassword(KeyService, discard); }
                    catch (Exception error) { System.Diagnostics.Trace.TraceWarning("DesktopAiProfile.Save key cleanup: " + error.GetType().Name); }
                }
            }
        }
    }
}
