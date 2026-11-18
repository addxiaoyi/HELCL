using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HelL.Core.Launcher.Ai;

public static class Client
{
	private sealed class ToolSlot
	{
		public string id = "";

		public string name = "";

		public string arguments = "";
	}

	public static string _BUILTIN_BASE = Defaults.DEFAULT_API_BASE;

	public static string _BUILTIN_KEY = Defaults.DEFAULT_API_KEY;

	private static readonly string[] _OVERFLOW_KEYS = new string[11]
	{
		"maximum context length", "context length exceeded", "context_length_exceeded", "prompt too long", "too many input tokens", "input length exceeds", "上下文长度", "上下文过长", "输入过长", "请求过长",
		"超出长度"
	};

	internal static string Categorize(string message, int status)
	{
		string text = Py.Lower(message ?? "");
		if (status == 401 || status == 403 || text.Contains("令牌无效") || text.Contains("unauthorized"))
		{
			return "auth";
		}
		if (status == 429 || text.Contains("rate limit") || text.Contains("限制") || text.Contains("额度"))
		{
			return "rate_limited";
		}
		if (text.Contains("timed out") || text.Contains("timeout") || text.Contains("超时"))
		{
			return "provider_timeout";
		}
		string[] array = new string[8] { "econnreset", "epipe", "etimedout", "connection", "连不上", "network", "getaddrinfo", "网络" };
		foreach (string value in array)
		{
			if (text.Contains(value))
			{
				return "provider_network_error";
			}
		}
		if (text.Contains("stream") || text.Contains("stalled") || text.Contains("sse"))
		{
			return "provider_stream_error";
		}
		return "unknown";
	}

	public static bool IsContextOverflow(string message)
	{
		string msg = Py.Lower(message ?? "");
		return _OVERFLOW_KEYS.Any((string k) => msg.Contains(Py.Lower(k)));
	}

	public static long MaxOutputTokens(PyDict settings)
	{
		object a = (settings ?? new PyDict()).Get("ai_max_tokens");
		try
		{
			return Math.Max(512L, Py.Int(Py.Or(a, 8192L)));
		}
		catch (TypeError)
		{
			return 8192L;
		}
		catch (ValueError)
		{
			return 8192L;
		}
	}

	public static string NormalizeBase(string url)
	{
		string text = Py.RStrip(Py.Strip(url ?? ""), "/");
		if (text.Length == 0)
		{
			return "";
		}
		if (text.EndsWith("/v1", StringComparison.Ordinal))
		{
			return text;
		}
		return text + "/v1";
	}

	private static string Mode(PyDict s)
	{
		return Py.Lower(Py.Strip(Py.Str(Py.Or(s.Get("ai_mode"), "public"))));
	}

	private static bool IsCustom(string mode)
	{
		if (!(mode == "custom") && !(mode == "newapi"))
		{
			return mode == "自定义";
		}
		return true;
	}

	public static PyDict ModelCaps(PyDict settings)
	{
		string mode = Mode(settings);
		if (mode == "local")
			return PyDict.Of("contextWindow", Py.Int(settings.Get("ai_local_context", 4096L)), "supportsToolCall", Py.Truthy(settings.Get("ai_local_tools")),
				"inputFormat", PyDict.Of("supportsText", true, "supportsImage", false), "outputFormat", PyDict.Of("supportsText", true));
		string text;
		string baseUrl;
		if (IsCustom(mode))
		{
			text = Py.Strip(Py.Str(Py.Or(settings.Get("ai_model"), "deepseek-v4-flash")));
			if (text.Length == 0)
			{
				text = "deepseek-v4-flash";
			}
			baseUrl = Py.Str(Py.Or(settings.Get("ai_base_url"), ""));
		}
		else
		{
			text = Py.Str(Py.Or(settings.Get("ai_model"), Builtin.PublicModel(), "deepseek-v4-flash"));
			baseUrl = Py.Str(Py.Or(settings.Get("ai_gateway_url"), Py.Or(Defaults.DEFAULT_GATEWAY_URL, "")));
		}
		return HelL.Core.Launcher.Ai.ModelCaps.Resolve(text, HelL.Core.Launcher.Ai.ModelCaps.ApiTypeForMode(mode), baseUrl, settings);
	}

	public static string CurrentModel(PyDict settings)
	{
		if (Mode(settings) == "local") return Py.S(settings.Get("ai_local_model"));
		if (IsCustom(Mode(settings)))
		{
			string text = Py.Strip(Py.Str(Py.Or(settings.Get("ai_model"), "deepseek-v4-flash")));
			if (text.Length != 0)
			{
				return text;
			}
			return "deepseek-v4-flash";
		}
		return Py.Str(Py.Or(settings.Get("ai_model"), Builtin.PublicModel(), "deepseek-v4-flash"));
	}

	public static bool SupportsImage(PyDict settings)
	{
		if (Mode(settings) == "local") return false;
		try
		{
			return VisionCaps.Supports(settings);
		}
		catch (Exception)
		{
			return false;
		}
	}

	public static PyDict ResolveEndpoint(PyDict settings)
	{
		string mode = Mode(settings);
		if (mode == "local")
		{
			string local = Local.ModelRules.Endpoint(Py.S(settings.Get("ai_local_url", Local.ModelRules.DefaultEndpoint)));
			string model = Local.ModelRules.ModelName(Py.S(settings.Get("ai_local_model")));
			return PyDict.Of("mode", "local", "url", local + "/v1/chat/completions", "models_url", local + "/v1/models",
				"headers", PyDict.Of("Content-Type", "application/json"), "model", model, "public", false, "builtin", false);
		}
		string text = Py.Strip(Py.Str(Py.Or(settings.Get("ai_model"), "deepseek-v4-flash")));
		if (text.Length == 0)
		{
			text = "deepseek-v4-flash";
		}
		if (IsCustom(mode))
		{
			string text2 = NormalizeBase(Py.Str(Py.Or(settings.Get("ai_base_url"), _BUILTIN_BASE)));
			string text3 = Py.Strip(Py.Str(Py.Or(settings.Get("ai_api_key"), "")));
			if (text3.Length == 0)
			{
				text3 = _BUILTIN_KEY;
			}
			if (string.IsNullOrEmpty(text2))
			{
				throw new AIClientError("请在设置里填写「自定义 OpenAI 兼容端点」的地址（到 /v1 为止）");
			}
			if (string.IsNullOrEmpty(text3))
			{
				throw new AIClientError("请在设置里填写「自定义 OpenAI 兼容端点」的令牌");
			}
			return PyDict.Of("mode", "custom", "url", text2 + "/chat/completions", "models_url", text2 + "/models", "headers", PyDict.Of("Authorization", "Bearer " + text3, "Content-Type", "application/json"), "model", text, "public", false, "builtin", text2 == _BUILTIN_BASE);
		}
		PyDict pyDict = Builtin.PublicEndpoint();
		string text4 = Py.RStrip(Py.Strip(Py.Str(Py.Or(settings.Get("ai_gateway_url"), Py.Or(Defaults.DEFAULT_GATEWAY_URL, "")))), "/");
		if (text4.Length > 0)
		{
			return PyDict.Of("mode", "public", "url", text4 + "/tipcl/chat", "models_url", text4 + "/tipcl/models", "headers", PyDict.Of("Content-Type", "application/json", "X-HelL-Client", AppInfo.CLIENT_HEADER), "model", text, "public", true);
		}
		throw new AIClientError("还没有配置 AI 网关：请到「设置 → AI 助手」填入自建公益网关地址，或切到「自定义 OpenAI 兼容端点」模式填地址与令牌。搭建自建网关的方法请查看启动器安装目录下的说明文档，或访问项目主页。");
	}

	public static AiSession _Session(bool localOnly = false)
	{
		return new AiSession(localOnly);
	}

	private static object JsonOf(AiResponse r)
	{
		return PyJson.Loads(r.Text);
	}

	private static object DGet(object d, string k)
	{
		if (d is PyDict pyDict)
		{
			return pyDict.Get(k);
		}
		throw new AttributeError("'" + Py.TypeName(d) + "' object has no attribute 'get'");
	}

	public static string TestConnection(PyDict settings)
	{
		PyDict pyDict = ResolveEndpoint(settings);
		using AiSession aiSession = _Session(Mode(settings) == "local");
		AiResponse aiResponse;
		try
		{
			aiResponse = aiSession.Get((string)pyDict["models_url"], (PyDict)pyDict["headers"], 15);
		}
		catch (AiRequestException ex)
		{
			throw new PyException("ConnectionError", ex.Message, ex);
		}
		if (aiResponse.StatusCode >= 400)
		{
			throw new AIClientError(_ErrText(aiResponse), aiResponse.StatusCode);
		}
		object d;
		try
		{
			d = JsonOf(aiResponse);
		}
		catch (Exception)
		{
			return "已连通";
		}
		if ((bool)pyDict["public"])
		{
			if (Py.Truthy(DGet(d, "service")))
			{
				return "公益接口正常（" + Py.Str(DGet(d, "service")) + "）";
			}
			List<string> list = ModelNames(Py.Or(DGet(d, "data"), new List<object>()));
			string text = Py.Str(Py.Or(pyDict.Get("model"), "deepseek-v4-flash"));
			if (list.Contains(text) || list.Count == 0)
			{
				return "公益接口正常，模型 " + text;
			}
			return "公益接口已连通，但列表里没有 " + text + "（当前有 " + list[0] + "）";
		}
		List<string> list2 = ModelNames(Py.Or(DGet(d, "data"), new List<object>()));
		if (list2.Count > 0)
		{
			return "自定义端点正常，可用模型 " + list2.Count + " 个，例如 " + list2[0];
		}
		return "自定义端点已连通";
	}

	private static List<string> ModelNames(object models)
	{
		List<string> list = new List<string>();
		if (models is List<object> list2)
		{
			foreach (object item in list2)
			{
				if (item is PyDict pyDict && Py.Truthy(pyDict.Get("id")))
				{
					list.Add(Py.Str(pyDict.Get("id")));
				}
			}
		}
		return list;
	}

	public static List<object> ListModels(PyDict settings)
	{
		if (Mode(settings) == "local")
		{
			using var local = new Local.OllamaClient(Py.S(settings.Get("ai_local_url", Local.ModelRules.DefaultEndpoint)));
			return local.ModelsAsync(System.Threading.CancellationToken.None).GetAwaiter().GetResult().Select(model => (object)model.Name).ToList();
		}
		PyDict pyDict = ResolveEndpoint(settings);
		using AiSession aiSession = _Session(Mode(settings) == "local");
		AiResponse aiResponse;
		try
		{
			aiResponse = aiSession.Get((string)pyDict["models_url"], (PyDict)pyDict["headers"], 15);
		}
		catch (AiRequestException ex)
		{
			throw new PyException("ConnectionError", ex.Message, ex);
		}
		if (aiResponse.StatusCode >= 400)
		{
			throw new AIClientError(_ErrText(aiResponse), aiResponse.StatusCode);
		}
		object obj;
		try
		{
			obj = JsonOf(aiResponse);
		}
		catch (Exception inner)
		{
			throw new AIClientError("模型列表接口返回的不是 JSON（可能网关未启用该接口）", 0, inner);
		}
		if (!(obj is PyDict pyDict2))
		{
			throw new AIClientError("模型列表接口返回格式不对");
		}
		object obj2 = pyDict2.Get("data");
		if (obj2 == null)
		{
			obj2 = pyDict2.Get("models");
		}
		List<string> list = new List<string>();
		HashSet<string> hashSet = new HashSet<string>();
		if (obj2 is List<object> list2)
		{
			foreach (object item in list2)
			{
				if (((item is PyDict pyDict3) ? pyDict3.Get("id") : ((item is string) ? item : null)) is string text && Py.Strip(text).Length > 0 && hashSet.Add(text))
				{
					list.Add(text);
				}
			}
		}
		else if (obj2 is PyDict pyDict4)
		{
			foreach (string key in pyDict4.Keys)
			{
				if (Py.Strip(key).Length > 0 && hashSet.Add(key))
				{
					list.Add(key);
				}
			}
		}
		list.Sort(string.CompareOrdinal);
		return list.Cast<object>().ToList();
	}

	public static string _ErrText(AiResponse resp)
	{
		int num = resp?.StatusCode ?? 0;
		string prefix = ((num != 0) ? ("HTTP " + num + " ") : "");
		string text = resp.Text;
		object obj;
		try
		{
			obj = PyJson.Loads(text);
		}
		catch (Exception)
		{
			return Out(Py.Truthy(text) ? text : "接口错误");
		}
		object obj2 = ((obj is PyDict pyDict) ? pyDict.Get("error") : null);
		if (obj2 is PyDict pyDict2)
		{
			object o = Py.Or(pyDict2.Get("message"), pyDict2.Get("msg"), Py.Or(pyDict2.Get("code"), ""));
			object o2 = Py.Or(pyDict2.Get("type"), pyDict2.Get("code"), "");
			string text2 = "";
			string[] array = new string[3] { "request_id", "requestId", "id" };
			foreach (string key in array)
			{
				PyDict pyDict3 = (PyDict)obj;
				if (Py.Truthy(pyDict3.Get(key)) || Py.Truthy(pyDict2.Get(key)))
				{
					text2 = Py.Str(Py.Or(pyDict3.Get(key), pyDict2.Get(key)));
					break;
				}
			}
			List<string> list = new List<string> { Py.Strip(Py.Str(o)) };
			if (Py.Truthy(o2) && !list[0].Contains(Py.Str(o2)))
			{
				list.Add(Py.Str(o2));
			}
			if (text2.Length > 0 && !list[0].Contains(text2))
			{
				list.Add("request id: " + text2);
			}
			string text3 = string.Join(" ", list.Where((string p) => p.Length > 0));
			return Out((text3.Length > 0) ? text3 : PyJson.Dumps(obj2, null, ensureAscii: false));
		}
		if (obj2 is string text4 && Py.Strip(text4).Length > 0)
		{
			return Out(text4);
		}
		if (obj is PyDict pyDict4)
		{
			object o3 = Py.Or(pyDict4.Get("message"), pyDict4.Get("msg"));
			if (Py.Truthy(o3))
			{
				return Out(Py.Str(o3));
			}
		}
		return Out(Py.Truthy(text) ? text : "接口错误");
		string Out(string text5)
		{
			return Py.Slice(prefix + DecodeSseLine(Py.Truthy(text5) ? text5 : "接口错误"), null, 800);
		}
	}

	public static string DecodeSseLine(string raw)
	{
		if (raw == null)
		{
			return "";
		}
		if (raw.Length == 0 || raw.Any((char ch) => ch > 'ÿ') || raw.All((char ch) => ch < '\u0080'))
		{
			return raw;
		}
		string text;
		try
		{
			text = Utils.DecodeUtf8Strict(raw.Select((char ch) => (byte)ch).ToArray());
		}
		catch (Exception)
		{
			return raw;
		}
		if (!raw.Any((char ch) => ch >= '\u0080' && ch <= '\u009f') && !text.Any((char ch) => ch >= '一' && ch <= '鿿'))
		{
			return raw;
		}
		return text;
	}

	public static string DecodeSseLine(byte[] raw)
	{
		if (raw != null)
		{
			return Encoding.UTF8.GetString(raw);
		}
		return "";
	}

	private static bool ArgsComplete(string raw)
	{
		string text = Py.Strip(raw ?? "");
		if (text.Length == 0)
		{
			return true;
		}
		try
		{
			PyJson.Loads(text);
			return true;
		}
		catch (Exception)
		{
			return false;
		}
	}

	private static List<object> FlushTools(SortedDictionary<long, ToolSlot> acc)
	{
		List<object> list = new List<object>();
		foreach (KeyValuePair<long, ToolSlot> item in acc)
		{
			ToolSlot value = item.Value;
			list.Add(PyDict.Of("id", (value.id.Length > 0) ? value.id : ("call_" + item.Key), "type", "function", "function", PyDict.Of("name", value.name, "arguments", (value.arguments.Length > 0) ? value.arguments : "{}")));
		}
		return list;
	}

	private static List<object> FlushCompleteTools(SortedDictionary<long, ToolSlot> acc)
	{
		List<object> list = FlushTools(acc);
		if (list.Count == 0)
		{
			return null;
		}
		if (list.All((object t) => ArgsComplete(Py.S(Py.DictOr(((PyDict)t).Get("function")).Get("arguments")))))
		{
			return list;
		}
		return null;
	}

	private static PyDict Ev(string type, string k = null, object v = null)
	{
		PyDict pyDict = PyDict.Of("type", type);
		if (k != null)
		{
			pyDict[k] = v;
		}
		return pyDict;
	}

	public static IEnumerable<PyDict> AssembleStream(IEnumerable<byte[]> lineSource, bool expectUsage = false)
	{
		SortedDictionary<long, ToolSlot> toolAcc = new SortedDictionary<long, ToolSlot>();
		bool gotDelta = false;
		bool inReasoning = false;
		PyDict pendingDone = null;
		List<object> pendingTools = null;
		object lastUsage = null;
		new List<PyDict>();
		IEnumerator<byte[]> it = lineSource.GetEnumerator();
		while (true)
		{
			bool flag = false;
			bool flag2;
			try
			{
				flag2 = it.MoveNext();
			}
			catch (AiStreamBroken)
			{
				flag2 = false;
				flag = true;
			}
			if (flag)
			{
				List<object> tools = ((toolAcc.Count > 0) ? FlushCompleteTools(toolAcc) : null);
				if (tools != null || pendingTools != null)
				{
					if (Py.Truthy(lastUsage))
					{
						yield return Ev("usage", "usage", lastUsage);
					}
					yield return Ev("tool_calls", "tool_calls", tools ?? pendingTools);
				}
				else
				{
					yield return Ev("error", "message", "接口超时，正在换一次非流式");
				}
				yield break;
			}
			if (!flag2)
			{
				break;
			}
			byte[] current = it.Current;
			if (current == null || current.Length == 0)
			{
				continue;
			}
			string text = Py.Strip(DecodeSseLine(current));
			if (text.StartsWith("data:", StringComparison.Ordinal))
			{
				text = Py.Strip(text.Substring(5));
			}
			if (text.Length == 0)
			{
				continue;
			}
			if (text == "[DONE]")
			{
				string text2 = CloseReasoning();
				if (text2.Length > 0)
				{
					yield return Ev("delta", "text", text2);
				}
				List<object> list = ((toolAcc.Count > 0) ? FlushCompleteTools(toolAcc) : null);
				if (list != null)
				{
					pendingTools = list;
				}
				else if (toolAcc.Count > 0)
				{
					yield return Ev("error", "message", "工具参数不完整，正在换一次非流式");
					yield break;
				}
				if (Py.Truthy(lastUsage))
				{
					yield return Ev("usage", "usage", lastUsage);
				}
				if (pendingTools != null)
				{
					yield return Ev("tool_calls", "tool_calls", pendingTools);
				}
				else
				{
					yield return pendingDone ?? Ev("done");
				}
				yield break;
			}
			object obj;
			try
			{
				obj = PyJson.Loads(text);
			}
			catch (Exception)
			{
				continue;
			}
			if (!(obj is PyDict pyDict))
			{
				throw new AttributeError("'" + Py.TypeName(obj) + "' object has no attribute 'get'");
			}
			if (Py.Truthy(pyDict.Get("error")))
			{
				object obj2 = pyDict["error"];
				yield return Ev("error", "message", (obj2 is PyDict pyDict2) ? pyDict2.Get("message") : Py.Str(obj2));
				yield break;
			}
			object obj3 = pyDict.Get("usage");
			if (obj3 is PyDict { Count: >0 })
			{
				lastUsage = obj3;
			}
			if (!(Py.Or(pyDict.Get("choices"), new List<object>()) is List<object> { Count: not 0 } list2))
			{
				continue;
			}
			PyDict choice = (PyDict)list2[0];
			PyDict delta = (Py.Or(choice.Get("delta"), new PyDict()) as PyDict) ?? new PyDict();
			object obj4 = delta.Get("reasoning_content");
			if (obj4 is string { Length: >0 } rc)
			{
				if (!inReasoning)
				{
					inReasoning = true;
					gotDelta = true;
					yield return Ev("delta", "text", "<think>");
				}
				yield return Ev("delta", "text", DecodeSseLine(rc));
			}
			object text3 = delta.Get("content");
			if (Py.Truthy(text3))
			{
				string text4 = CloseReasoning();
				if (text4.Length > 0)
				{
					yield return Ev("delta", "text", text4);
				}
				gotDelta = true;
				yield return Ev("delta", "text", (text3 is string raw) ? DecodeSseLine(raw) : text3);
			}
			foreach (PyDict item in Py.ListOr(delta.Get("tool_calls")))
			{
				long key = (item.ContainsKey("index") ? Py.Int(item["index"]) : 0);
				if (!toolAcc.TryGetValue(key, out ToolSlot value))
				{
					ToolSlot toolSlot = (toolAcc[key] = new ToolSlot());
					value = toolSlot;
				}
				if (Py.Truthy(item.Get("id")))
				{
					value.id = Py.Str(item["id"]);
				}
				PyDict pyDict5 = (Py.Or(item.Get("function"), new PyDict()) as PyDict) ?? new PyDict();
				if (Py.Truthy(pyDict5.Get("name")))
				{
					value.name = Py.Str(pyDict5["name"]);
				}
				if (Py.Truthy(pyDict5.Get("arguments")))
				{
					value.arguments += Py.Str(pyDict5["arguments"]);
				}
			}
			object reason = choice.Get("finish_reason");
			if (Py.Eq(reason, "tool_calls"))
			{
				string text5 = CloseReasoning();
				if (text5.Length > 0)
				{
					yield return Ev("delta", "text", text5);
				}
				List<object> list3 = FlushCompleteTools(toolAcc);
				if (list3 == null)
				{
					yield return Ev("error", "message", "工具参数不完整，正在换一次非流式");
					yield break;
				}
				pendingTools = list3;
			}
			else if (Py.Eq(reason, "stop") || Py.Eq(reason, "length"))
			{
				string text6 = CloseReasoning();
				if (text6.Length > 0)
				{
					yield return Ev("delta", "text", text6);
				}
				if (!expectUsage)
				{
					yield return PyDict.Of("type", "done", "finish_reason", reason);
					yield break;
				}
				pendingDone = PyDict.Of("type", "done", "finish_reason", reason);
			}
		}
		string text7 = CloseReasoning();
		if (text7.Length > 0)
		{
			yield return Ev("delta", "text", text7);
		}
		List<object> list4 = ((toolAcc.Count > 0) ? FlushCompleteTools(toolAcc) : null);
		if (list4 != null)
		{
			pendingTools = list4;
		}
		else if (toolAcc.Count > 0)
		{
			yield return Ev("error", "message", "工具参数不完整，正在换一次非流式");
			yield break;
		}
		if (pendingTools != null)
		{
			if (Py.Truthy(lastUsage))
			{
				yield return Ev("usage", "usage", lastUsage);
			}
			yield return Ev("tool_calls", "tool_calls", pendingTools);
		}
		else if (pendingDone != null)
		{
			if (Py.Truthy(lastUsage))
			{
				yield return Ev("usage", "usage", lastUsage);
			}
			yield return pendingDone;
		}
		else if (gotDelta)
		{
			yield return Ev("done");
		}
		else
		{
			yield return Ev("error", "message", "接口没有返回内容");
		}
		string CloseReasoning()
		{
			if (inReasoning)
			{
				inReasoning = false;
				return "</think>";
			}
			return "";
		}
	}

	private static AIClientError HttpError(Exception exc, HttpCancel httpCancel)
	{
		if (httpCancel != null && httpCancel.Cancelled())
		{
			return new AIClientError("已停止");
		}
		return new AIClientError("连不上接口: " + exc.Message);
	}

	public static List<object> PrepareMessages(PyDict settings, List<object> messages)
	{
		if (messages == null || messages.Count == 0)
		{
			return messages;
		}
		if (!messages.Any((object m) => m is PyDict pyDict3 && Py.Truthy(pyDict3.Get("images"))))
		{
			return messages;
		}
		bool flag = SupportsImage(settings);
		List<object> list = new List<object>();
		foreach (object message in messages)
		{
			if (!(message is PyDict pyDict) || !Py.Truthy(pyDict.Get("images")))
			{
				list.Add(message);
				continue;
			}
			List<object> list2 = Py.ListOr(Py.Or(pyDict.Get("images"), new List<object>())).Where(Py.Truthy).ToList();
			PyDict pyDict2 = pyDict.Copy();
			pyDict2.Remove("images");
			if (list2.Count == 0)
			{
				list.Add(pyDict2);
				continue;
			}
			object o = pyDict2.Get("content");
			if (flag)
			{
				List<object> list3 = new List<object>();
				List<string> list4 = new List<string>();
				foreach (object item in list2.Take(4))
				{
					try
					{
						list3.Add(Images.EncodeFile(Py.Str(item)));
					}
					catch (ImageError imageError)
					{
						list4.Add(new PPath(Py.Str(item)).Name + "：" + imageError.Message);
					}
					catch (Exception e)
					{
						list4.Add(new PPath(Py.Str(item)).Name + "：" + Py.Slice(Py.ExcStr(e), null, 80));
					}
				}
				if (list3.Count > 0)
				{
					pyDict2["content"] = Images.BuildUserContent(Py.S(o), list3);
				}
				if (list4.Count > 0)
				{
					string text = "（这些图片没能附上：" + string.Join("；", list4) + "）";
					if (pyDict2["content"] is List<object> list5)
					{
						list5.Add(PyDict.Of("type", "text", "text", text));
					}
					else
					{
						pyDict2["content"] = Py.Strip(Py.Str(pyDict2.Req("content")) + "\n" + text);
					}
				}
			}
			else
			{
				string text2 = "（用户附了 " + list2.Count + " 张图片，但当前模型不支持看图，已省略。如需识别图片，请在设置里换成支持图片的模型。）";
				pyDict2["content"] = Py.Strip(Py.Str(o) + "\n" + text2);
			}
			list.Add(pyDict2);
		}
		return list;
	}

	private static PyDict Body(PyDict ep, PyDict settings, List<object> messages, List<object> tools, double temperature, bool stream)
	{
		PyDict pyDict = PyDict.Of("model", ep["model"], "messages", PrepareMessages(settings, messages), "temperature", temperature, "stream", stream, "max_tokens", MaxOutputTokens(settings));
		if (tools != null && tools.Count > 0 && (Mode(settings) != "local" || Py.Truthy(settings.Get("ai_local_tools"))))
		{
			pyDict["tools"] = tools;
			pyDict["tool_choice"] = "auto";
		}
		return pyDict;
	}

	public static IEnumerable<PyDict> ChatStream(PyDict settings, List<object> messages, List<object> tools = null, double temperature = 0.3, HttpCancel httpCancel = null)
	{
		if (Mode(settings) == "local") return Local.LocalTools.Chat(settings, messages, tools, temperature, httpCancel);
		PyDict pyDict = ResolveEndpoint(settings);
		PyDict pyDict2 = Body(pyDict, settings, messages, tools, temperature, stream: true);
		bool expectUsage = false;
		if (!(bool)pyDict["public"])
		{
			pyDict2["stream_options"] = PyDict.Of("include_usage", true);
			expectUsage = true;
		}
		AiSession aiSession = _Session(Mode(settings) == "local");
		if (httpCancel != null)
		{
			httpCancel.Bind(aiSession);
			if (httpCancel.Cancelled())
			{
				throw new AIClientError("已停止");
			}
		}
		AiResponse aiResponse;
		try
		{
			aiResponse = aiSession.Post((string)pyDict["url"], (PyDict)pyDict["headers"], pyDict2, 15, 90, stream: true);
		}
		catch (AiRequestException exc)
		{
			throw HttpError(exc, httpCancel);
		}
		if (httpCancel != null)
		{
			httpCancel.Bind(aiSession, aiResponse);
			if (httpCancel.Cancelled())
			{
				throw new AIClientError("已停止");
			}
		}
		if (aiResponse.StatusCode >= 400)
		{
			throw new AIClientError(_ErrText(aiResponse), aiResponse.StatusCode);
		}
		return AssembleStream(aiResponse.IterLines(), expectUsage);
	}

	public static PyDict ChatOnce(PyDict settings, List<object> messages, List<object> tools = null, double temperature = 0.3, HttpCancel httpCancel = null)
	{
		if (Mode(settings) == "local")
		{
			var text = new StringBuilder();
			var calls = new List<object>();
			var usage = new PyDict();
			foreach (var frame in Local.LocalTools.Chat(settings, messages, tools, temperature, httpCancel))
			{
				if (Py.S(frame.Get("type")) == "delta") text.Append(Py.S(frame.Get("text")));
				if (Py.S(frame.Get("type")) == "tool_calls") calls = Py.ListOr(frame.Get("tool_calls"));
				if (frame.Get("usage") is PyDict counters) usage = counters;
			}
			return PyDict.Of("content", text.ToString(), "tool_calls", calls, "usage", usage, "finish_reason", "stop");
		}
		PyDict pyDict = ResolveEndpoint(settings);
		PyDict jsonBody = Body(pyDict, settings, messages, tools, temperature, stream: false);
		AiSession aiSession = _Session(Mode(settings) == "local");
		if (httpCancel != null)
		{
			httpCancel.Bind(aiSession);
			if (httpCancel.Cancelled())
			{
				throw new AIClientError("已停止");
			}
		}
		AiResponse aiResponse;
		try
		{
			aiResponse = aiSession.Post((string)pyDict["url"], (PyDict)pyDict["headers"], jsonBody, 90, 90, stream: false);
		}
		catch (AiRequestException exc)
		{
			throw HttpError(exc, httpCancel);
		}
		if (aiResponse.StatusCode >= 400)
		{
			throw new AIClientError(_ErrText(aiResponse), aiResponse.StatusCode);
		}
		object obj;
		try
		{
			obj = PyJson.Loads(aiResponse.Text);
		}
		catch (Exception inner)
		{
			string text = Py.Slice(DecodeSseLine(Py.Strip(aiResponse.Text ?? "")), null, 200);
			throw new AIClientError("接口返回的不是 JSON（HTTP " + aiResponse.StatusCode + "）：" + ((text.Length > 0) ? text : "空响应"), aiResponse.StatusCode, inner);
		}
		if (!(obj is PyDict pyDict2))
		{
			throw new AIClientError("接口返回格式异常（HTTP " + aiResponse.StatusCode + "）", aiResponse.StatusCode);
		}
		PyDict pyDict3 = (((Py.Or(pyDict2.Get("choices"), Py.L(new PyDict())) is List<object> { Count: >0 } list) ? list[0] : new PyDict()) as PyDict) ?? new PyDict();
		PyDict pyDict4 = (Py.Or(pyDict3.Get("message"), new PyDict()) as PyDict) ?? new PyDict();
		object obj2 = Py.Or(pyDict4.Get("content"), "");
		if (pyDict4.Get("reasoning_content") is string { Length: >0 } text2)
		{
			obj2 = "<think>" + text2 + "</think>" + Py.Str(obj2);
		}
		return PyDict.Of("content", obj2, "tool_calls", Py.Or(pyDict4.Get("tool_calls"), new List<object>()), "finish_reason", Py.Or(pyDict3.Get("finish_reason"), "stop"), "usage", Py.Or(pyDict2.Get("usage"), new PyDict()));
	}
}
