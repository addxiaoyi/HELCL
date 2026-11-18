using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HelL.Core.Launcher.Ai.Local;

public static class LocalChat
{
    public static AgentResult Run(PyDict settings, List<object> history, string userText, Action<string>? onDelta,
        Action<string, PyDict>? onStatus, Func<bool>? cancelled, HttpCancel? httpCancel, List<object>? images)
    {
        if (images != null && images.Count > 0) throw new AIClientError("本地聊天目前仅支持文字，请移除图片或切换云端模型。");
        string endpoint = ModelRules.Endpoint(Py.S(settings.Get("ai_local_url", ModelRules.DefaultEndpoint)));
        string model = ModelRules.ModelName(Py.S(settings.Get("ai_local_model")));
        int context = (int)Math.Max(2048, Math.Min(32768, Py.Int(settings.Get("ai_local_context", 4096L))));
        var messages = BuildMessages(history, userText, context - 1200);
        using var session = new AiSession(localOnly: true);
        httpCancel?.Bind(session);
        if (cancelled?.Invoke() == true) throw new AIClientError("已停止");
        using (var details = session.Post(endpoint + "/api/show", PyDict.Of("Content-Type", "application/json"), PyDict.Of("model", model), 15, 15, stream: false))
        {
            if (details.StatusCode >= 400) throw new AIClientError("本地模型尚未就绪，请打开模型库检查服务和已安装模型。", details.StatusCode);
            var metadata = PyJson.Loads(details.Text) as PyDict ?? throw new AIClientError("本地服务没有返回模型信息。");
            if (Py.Truthy(metadata.Get("remote_model")) || Py.Truthy(metadata.Get("remote_host")))
                throw new AIClientError("该模型会转发到云端，已停止发送对话。请在模型库选择本地模型。");
        }
        onStatus?.Invoke("think", PyDict.Of("after_tools", false));
        var body = PyDict.Of("model", model, "messages", messages, "stream", true, "think", false, "keep_alive", "5m",
            "options", PyDict.Of("num_ctx", context, "num_predict", 1024, "temperature", 0.3));
        using var response = session.Post(endpoint + "/api/chat", PyDict.Of("Content-Type", "application/json"), body, 300, 300, stream: true);
        httpCancel?.Bind(session, response);
        if (response.StatusCode >= 400) throw new AIClientError("本地模型请求失败（" + response.StatusCode + "）：" + response.Text, response.StatusCode);
        var reply = new StringBuilder();
        var usage = new PyDict();
        bool finished = false;
        string reason = "completed";
        foreach (byte[] bytes in response.IterLines())
        {
            string line = Encoding.UTF8.GetString(bytes);
            if (cancelled?.Invoke() == true) throw new AIClientError("已停止");
            if (string.IsNullOrWhiteSpace(line)) continue;
            var frame = PyJson.Loads(line) as PyDict ?? throw new AIClientError("本地服务返回了无效响应。");
            if (Py.Truthy(frame.Get("error"))) throw new AIClientError("本地模型：" + Py.S(frame.Get("error")));
            if (frame.Get("message") is PyDict message)
            {
                string text = Py.S(message.Get("content"));
                if (text.Length > 0) { reply.Append(text); onDelta?.Invoke(text); }
            }
            if (Py.Truthy(frame.Get("done")))
            {
                finished = true;
                reason = Py.S(frame.Get("done_reason")) == "length" ? "truncated" : "completed";
                usage = PyDict.Of("prompt_tokens", frame.Get("prompt_eval_count", 0L), "completion_tokens", frame.Get("eval_count", 0L));
            }
        }
        if (!finished) throw new AIClientError("本地模型连接中断，回复尚未完成。请检查运行状态后重试。");
        if (reply.Length == 0) throw new AIClientError("本地模型没有返回文字。请重试或换用其他聊天模型。");
        var turn = new List<object> { PyDict.Of("role", "user", "content", userText), PyDict.Of("role", "assistant", "content", reply.ToString()) };
        return new AgentResult(reply.ToString(), reason, "", new List<object>(), 1)
        { usage = usage, messages = messages.Skip(1).Concat(new[] { turn[1] }).ToList(), turn_messages = turn };
    }

    public static List<object> BuildMessages(List<object> history, string text, int budget)
    {
        if (text.Length > budget) throw new AIClientError("这条消息超过当前本地上下文容量，请缩短内容或拆分发送。");
        var selected = new List<object>();
        int remaining = budget - text.Length;
        foreach (var row in (history ?? new List<object>()).OfType<PyDict>().Reverse())
        {
            string role = Py.S(row.Get("role"));
            if (role != "user" && role != "assistant") continue;
            string content = row.Get("content") as string ?? "";
            if (content.Length == 0) continue;
            if (content.Length > remaining) break;
            selected.Insert(0, PyDict.Of("role", role, "content", content));
            remaining -= content.Length;
        }
        while (selected.Count > 0 && Py.S(((PyDict)selected[0]).Get("role")) == "assistant") selected.RemoveAt(0);
        selected.Insert(0, PyDict.Of("role", "system", "你是 HelL 的本地聊天助手，使用中文回答。当前仅能对话，不能访问文件或执行操作；不要声称已经安装、修改或检查过用户的电脑。"));
        selected.Add(PyDict.Of("role", "user", "content", text));
        return selected;
    }
}
