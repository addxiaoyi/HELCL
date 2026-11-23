using HelL.Core;
using HelL.Core.Launcher;
using HelL.Core.Launcher.Ai;
using HelL.Core.Platform;

namespace HelL.Mobile.Ai;

internal sealed record MobileAiProfile(string Mode, string Endpoint, string Model, string ApiKey)
{
    public override string ToString() => Mode + ":" + Endpoint + ":" + Model;
    internal const string KeyService = "HelL mobile AI provider";
    private static readonly string[] Fields = ["mobile_ai_mode", "mobile_ai_url", "mobile_ai_model", "mobile_ai_key_id"];

    internal static MobileAiProfile Read(LauncherConfig config, ISecretStore store)
    {
        lock (config._lock) return ReadStored(config, store);
    }

    private static MobileAiProfile ReadStored(LauncherConfig config, ISecretStore store)
    {
        bool mobile = !string.IsNullOrEmpty(Py.S(config.Get("mobile_ai_mode")));
        string mode = Py.S(config.Get(mobile ? "mobile_ai_mode" : "ai_mode", "custom"));
        mode = mode is "newapi" or "自定义" ? "custom" : mode;
        if (mode is not ("custom" or "public")) throw new InvalidOperationException("此页面支持云端文字对话，请先配置自定义端点或网关。");
        string url = Py.S(config.Get(mobile ? "mobile_ai_url" : mode == "public" ? "ai_gateway_url" : "ai_base_url"));
        string model = Py.S(config.Get(mobile ? "mobile_ai_model" : "ai_model"));
        string id = mobile ? Py.S(config.Get("mobile_ai_key_id")) : "";
        string key = mode == "public" ? "" : mobile ? id.Length > 0 ? store.GetPassword(KeyService, id) : "" : Py.S(config.Get("ai_api_key"));
        return Validate(mode, url, model, key);
    }

    internal static MobileAiProfile Validate(string mode, string endpoint, string model, string key)
    {
        if (mode is not ("custom" or "public")) throw new ArgumentException("请选择自定义端点或网关。");
        endpoint = endpoint.Trim().TrimEnd('/');
        model = model.Trim();
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0
            || uri.Query.Length != 0 || uri.Fragment.Length != 0 || string.IsNullOrEmpty(uri.Host))
            throw new ArgumentException("请填写 HTTPS 服务地址，不要包含账号、查询参数或片段。");
        if (model.Length is < 1 or > 256 || model.Any(char.IsControl)) throw new ArgumentException("请填写有效的模型名称。");
        if (mode == "custom" && (string.IsNullOrWhiteSpace(key) || key.Length > 4096 || key.Any(char.IsControl)))
            throw new ArgumentException("请填写你自己的 API 密钥。密钥不会使用内置值补全。");
        return new(mode, endpoint, model, mode == "custom" ? key.Trim() : "");
    }

    internal PyDict RequestSettings() => PyDict.Of("ai_mode", Mode, "ai_base_url", Endpoint, "ai_gateway_url", Endpoint,
        "ai_model", Model, "ai_api_key", ApiKey, "ai_max_tokens", 4096L);

    internal static void Save(LauncherConfig config, ISecretStore store, MobileAiProfile profile)
    {
        profile = Validate(profile.Mode, profile.Endpoint, profile.Model, profile.ApiKey);
        lock (config._lock)
        {
            string id = profile.Mode == "custom" ? Guid.NewGuid().ToString("N") : "";
            string previousId = Py.S(config.Get("mobile_ai_key_id"));
            bool committed = false;
            try
            {
                if (id.Length > 0)
                {
                    store.SetPassword(KeyService, id, profile.ApiKey);
                    if (store.GetPassword(KeyService, id) != profile.ApiKey) throw new IOException("系统凭据库未能验证 API 密钥，原配置保留。");
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
                    catch (Exception error) { System.Diagnostics.Trace.TraceWarning("MobileAiProfile.Save key cleanup: " + error.GetType().Name); }
                }
            }
        }
    }
}
