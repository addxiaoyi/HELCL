using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HelL.Core.Launcher.Ai.Local;

public static class LocalTools
{
    public static List<object> Schemas(List<object> schemas) => schemas.OfType<PyDict>()
        .Where(schema => schema.Get("function") is PyDict function && Py.S(function.Get("name")) != "dispatch_subagent")
        .Cast<object>().ToList();
    public static void Verify(PyDict settings, HttpCancel? cancel = null)
    {
        string endpoint = ModelRules.Endpoint(Py.S(settings.Get("ai_local_url", ModelRules.DefaultEndpoint)));
        using var session = new AiSession(localOnly: true);
        cancel?.Bind(session);
        using var response = session.Post(endpoint + "/api/show", PyDict.Of("Content-Type", "application/json"), PyDict.Of("model", Client.CurrentModel(settings)), 15, 15, false);
        if (response.StatusCode >= 400) throw new AIClientError("无法读取本地模型能力，请检查模型和运行环境。", response.StatusCode);
        var details = PyJson.Loads(response.Text) as PyDict ?? throw new AIClientError("本地服务未返回有效的模型信息。");
        if (Py.Truthy(details.Get("remote_model")) || Py.Truthy(details.Get("remote_host"))) throw new AIClientError("云端别名不能用于本地助手。");
        if (!details.ContainsKey("capabilities")) throw new AIClientError("本地服务没有返回模型工具能力，请先更新 Ollama，再选择支持工具调用的模型。");
        if (!Py.ListOr(details.Get("capabilities")).Any(capability => Py.S(capability) == "tools"))
            throw new AIClientError("这个模型未声明工具调用能力，请选择支持工具的本地模型，或关闭工具模式。");
    }

    public static PyDict Settings(PyDict settings)
    {
        var local = settings.Copy();
        local["ai_context_window"] = Math.Max(8192L, Math.Min(32768L, Py.Int(settings.Get("ai_local_context", 8192L))));
        local["ai_max_tokens"] = 1024L;
        local["ai_max_rounds"] = 8L;
        local["ai_permission_mode"] = Py.S(settings.Get("ai_permission_mode")) == "plan" ? "plan" : "default";
        local["ai_confirm_writes"] = true;
        local["ai_permission_dont_ask"] = false;
        local["ai_fallback_model"] = "";
        local["ai_hooks_enabled"] = false;
        return local;
    }

    public static IEnumerable<PyDict> Chat(PyDict settings, List<object> messages, List<object>? tools, double temperature, HttpCancel? cancel)
    {
        string endpoint = ModelRules.Endpoint(Py.S(settings.Get("ai_local_url", ModelRules.DefaultEndpoint)));
        var body = PyDict.Of("model", Client.CurrentModel(settings), "messages", Messages(messages), "tools", tools ?? new List<object>(),
            "think", false, "stream", true, "options", PyDict.Of("num_ctx", settings.Get("ai_context_window", 8192L), "num_predict", 1024L, "temperature", temperature));
        using var session = new AiSession(localOnly: true);
        cancel?.Bind(session);
        using var response = session.Post(endpoint + "/api/chat", PyDict.Of("Content-Type", "application/json"), body, 300, 300, true);
        cancel?.Bind(session, response);
        if (response.StatusCode >= 400) throw new AIClientError("本地工具请求失败：" + response.Text, response.StatusCode);
        foreach (var frame in Frames(response.IterLines())) yield return frame;
    }

    public static IEnumerable<PyDict> Frames(IEnumerable<byte[]> lines)
    {
        var calls = new List<object>();
        bool done = false;
        string finish = "stop";
        foreach (byte[] bytes in lines)
        {
            string line = Encoding.UTF8.GetString(bytes);
            if (string.IsNullOrWhiteSpace(line)) continue;
            var frame = PyJson.Loads(line) as PyDict ?? throw new AIClientError("本地工具响应无效。");
            if (Py.Truthy(frame.Get("error"))) throw new AIClientError(Py.S(frame.Get("error")));
            if (frame.Get("message") is PyDict message)
            {
                string content = Py.S(message.Get("content"));
                if (content.Length > 0) yield return PyDict.Of("type", "delta", "text", content);
                foreach (var call in Py.ListOr(message.Get("tool_calls")).OfType<PyDict>())
                {
                    if (!(call.Get("function") is PyDict function) || !(function.Get("arguments") is PyDict arguments) || Py.S(function.Get("name")).Length == 0)
                        throw new AIClientError("模型返回了不完整的工具参数，未执行操作。");
                    calls.Add(PyDict.Of("id", "local_" + Guid.NewGuid().ToString("N"), "type", "function",
                        "function", PyDict.Of("name", function["name"], "arguments", PyJson.Dumps(arguments))));
                    if (calls.Count > 16) throw new AIClientError("单轮工具请求过多，已停止执行。");
                }
            }
            if (!Py.Truthy(frame.Get("done"))) continue;
            done = true;
            finish = Py.S(frame.Get("done_reason"));
            yield return PyDict.Of("type", "usage", "usage", PyDict.Of("prompt_tokens", frame.Get("prompt_eval_count", 0L), "completion_tokens", frame.Get("eval_count", 0L)));
            break;
        }
        if (!done) throw new AIClientError("本地模型连接中断，未执行未完成的工具请求。");
        if (calls.Count > 0) yield return PyDict.Of("type", "tool_calls", "tool_calls", calls);
        else yield return PyDict.Of("type", "done", "finish_reason", finish == "length" ? "length" : "stop");
    }

    public static List<object> Messages(List<object> messages)
    {
        var mapped = new List<object>();
        var names = new Dictionary<string, string>();
        foreach (var row in messages.OfType<PyDict>())
        {
            var message = PyDict.Of("role", row.Get("role"), "content", row.Get("content", ""));
            var calls = new List<object>();
            foreach (var call in Py.ListOr(row.Get("tool_calls")).OfType<PyDict>())
            {
                if (!(call.Get("function") is PyDict function)) continue;
                object args = function.Get("arguments") is string json ? PyJson.Loads(json) : function.Get("arguments");
                if (!(args is PyDict)) throw new AIClientError("历史工具参数无效，请新建对话重试。");
                string name = Py.S(function.Get("name"));
                names[Py.S(call.Get("id"))] = name;
                calls.Add(PyDict.Of("function", PyDict.Of("name", name, "arguments", args)));
            }
            if (calls.Count > 0) message["tool_calls"] = calls;
            if (Py.S(row.Get("role")) == "tool" && names.TryGetValue(Py.S(row.Get("tool_call_id")), out string toolName)) message["tool_name"] = toolName;
            mapped.Add(message);
        }
        return mapped;
    }
}
