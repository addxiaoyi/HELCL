using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace HelL.Core.Launcher.Ai;

/// <summary>
/// 本机 MCP Server（Streamable HTTP，仅 127.0.0.1）。
/// 把 AI 助手已有的工具原样开放给 Claude / Cursor 等外部 agent。
/// 地址：http://127.0.0.1:25585/mcp
/// </summary>
public static class McpServer
{
	public const int DefaultPort = 25585;

	public const string ProtocolVersion = "2025-06-18";

	// 依赖启动器界面的工具不对外开放
	private static readonly HashSet<string> _hidden = new HashSet<string> { "ask_user", "dispatch_subagent", "update_plan", "remember", "forget" };

	private static readonly object _lock = new object();

	private static TcpListener _listener;

	private static volatile bool _running;

	public static BackendApi Backend { get; set; }

	public static int Port { get; private set; } = DefaultPort;

	public static bool Running => _running;

	public static string Url => $"http://127.0.0.1:{Port}/mcp";

	public static string LastError { get; private set; } = "";

	/// <summary>外部客户端最近一次调用的工具名和时间（本机时间）；没调用过为空。</summary>
	public static string LastTool { get; private set; } = "";

	public static DateTime LastToolAt { get; private set; }

	/// <summary>开关变化时触发（可能在后台线程）。</summary>
	public static event Action StateChanged;

	/// <summary>外部客户端调用工具时触发：工具名、时间（可能在后台线程）。</summary>
	public static event Action<string, DateTime> ToolCalled;

	private static void RaiseState()
	{
		try { StateChanged?.Invoke(); } catch (Exception) { }
	}

	private static string PrefPath => Path.Combine(Utils.ResolveRoot(), "mcp_server.json");

	public static bool Enabled
	{
		get
		{
			try
			{
				if (!File.Exists(PrefPath)) return false;
				using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(PrefPath));
				if (doc.RootElement.TryGetProperty("port", out JsonElement p) && p.TryGetInt32(out int port) && port > 0 && port < 65536) Port = port;
				return doc.RootElement.TryGetProperty("enabled", out JsonElement e) && e.ValueKind == JsonValueKind.True;
			}
			catch (Exception)
			{
				return false;
			}
		}
	}

	/// <summary>启动时调用：上次开着就自动开。</summary>
	public static void StartIfEnabled(BackendApi backend)
	{
		Backend = backend;
		if (Enabled) Start();
	}

	/// <summary>设置页开关调用；返回是否已在运行。</summary>
	public static bool SetEnabled(bool on)
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(PrefPath));
			File.WriteAllText(PrefPath, $"{{\"enabled\": {(on ? "true" : "false")}, \"port\": {Port}}}");
		}
		catch (Exception)
		{
		}
		if (on) Start(); else Stop();
		return _running;
	}

	public static void Start()
	{
		lock (_lock)
		{
			if (_running) return;
			try
			{
				_listener = new TcpListener(IPAddress.Loopback, Port);
				_listener.Start(32);
				_running = true;
				LastError = "";
				new Thread(AcceptLoop) { IsBackground = true, Name = "mcp-accept" }.Start();
			}
			catch (Exception ex)
			{
				_running = false;
				LastError = ex.Message;
			}
		}
		RaiseState();
	}

	public static void Stop()
	{
		lock (_lock)
		{
			_running = false;
			try { _listener?.Stop(); } catch (Exception) { }
			_listener = null;
		}
		RaiseState();
	}

	private static void AcceptLoop()
	{
		TcpListener l = _listener;
		while (_running && l != null)
		{
			TcpClient c;
			try { c = l.AcceptTcpClient(); }
			catch (Exception) { if (!_running) break; continue; }
			ThreadPool.QueueUserWorkItem(_ => Handle(c));
		}
	}

	private static void Handle(TcpClient client)
	{
		using (client)
		{
			try
			{
				if (!(client.Client.RemoteEndPoint is IPEndPoint ep) || !IPAddress.IsLoopback(ep.Address)) return;
				client.ReceiveTimeout = 30000;
				NetworkStream s = client.GetStream();
				string requestLine = ReadLine(s);
				if (string.IsNullOrEmpty(requestLine)) return;
				string[] parts = requestLine.Split(' ');
				string method = parts.Length > 0 ? parts[0] : "";
				string path = parts.Length > 1 ? parts[1] : "";
				var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				string line;
				while (!string.IsNullOrEmpty(line = ReadLine(s)))
				{
					int i = line.IndexOf(':');
					if (i > 0) headers[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
				}
				// 防止网页通过浏览器偷偷调用本机接口（DNS rebinding / CSRF）
				if (headers.TryGetValue("Origin", out string origin) && !IsLocalOrigin(origin))
				{
					Write(s, 403, "text/plain", "forbidden origin");
					return;
				}
				int q = path.IndexOf('?');
				if (q >= 0) path = path.Substring(0, q);
				if (path != "/mcp" && path != "/mcp/")
				{
					Write(s, 404, "text/plain", "HelL MCP endpoint: /mcp");
					return;
				}
				if (method == "GET")
				{
					Write(s, 405, "text/plain", "SSE stream not supported; use POST");
					return;
				}
				if (method == "DELETE")
				{
					Write(s, 200, "text/plain", "");
					return;
				}
				if (method != "POST")
				{
					Write(s, 405, "text/plain", "method not allowed");
					return;
				}
				int len = headers.TryGetValue("Content-Length", out string cl) && int.TryParse(cl, out int n) ? n : 0;
				if (len <= 0 || len > 4194304)
				{
					Write(s, 400, "text/plain", "bad content-length");
					return;
				}
				byte[] body = new byte[len];
				int read = 0;
				while (read < len)
				{
					int r = s.Read(body, read, len - read);
					if (r <= 0) break;
					read += r;
				}
				string reply = Dispatch(Encoding.UTF8.GetString(body, 0, read));
				if (reply == null) Write(s, 202, "text/plain", "");
				else Write(s, 200, "application/json", reply);
			}
			catch (Exception)
			{
			}
		}
	}

	private static bool IsLocalOrigin(string origin)
	{
		if (string.IsNullOrEmpty(origin) || origin == "null") return true;
		try
		{
			string h = new Uri(origin).Host;
			return h == "127.0.0.1" || h == "localhost" || h == "[::1]" || h == "::1";
		}
		catch (Exception)
		{
			return false;
		}
	}

	/// <summary>处理一条 JSON-RPC 消息（或批量），返回响应 JSON；通知返回 null。</summary>
	public static string Dispatch(string json)
	{
		JsonDocument doc;
		try { doc = JsonDocument.Parse(json); }
		catch (Exception) { return Error(null, -32700, "Parse error"); }
		using (doc)
		{
			if (doc.RootElement.ValueKind == JsonValueKind.Array)
			{
				var outs = new List<string>();
				foreach (JsonElement m in doc.RootElement.EnumerateArray())
				{
					string r = One(m);
					if (r != null) outs.Add(r);
				}
				return outs.Count == 0 ? null : "[" + string.Join(",", outs) + "]";
			}
			return One(doc.RootElement);
		}
	}

	private static string One(JsonElement m)
	{
		if (m.ValueKind != JsonValueKind.Object) return Error(null, -32600, "Invalid Request");
		string id = m.TryGetProperty("id", out JsonElement idEl) ? idEl.GetRawText() : null;
		string method = m.TryGetProperty("method", out JsonElement me) && me.ValueKind == JsonValueKind.String ? me.GetString() : null;
		if (method == null) return null; // 客户端发来的响应，忽略
		bool isNotification = id == null;
		JsonElement prm = m.TryGetProperty("params", out JsonElement pe) ? pe : default;
		try
		{
			switch (method)
			{
			case "initialize":
			{
				string ver = prm.ValueKind == JsonValueKind.Object && prm.TryGetProperty("protocolVersion", out JsonElement pv) && pv.ValueKind == JsonValueKind.String ? pv.GetString() : ProtocolVersion;
				return Result(id, "{\"protocolVersion\":" + Str(ver) + ",\"capabilities\":{\"tools\":{\"listChanged\":false}},\"serverInfo\":{\"name\":\"tipcl\",\"title\":\"HelL Minecraft 启动器\",\"version\":" + Str(Defaults.CLIENT_HEADER.Replace("HelL/", "")) + "},\"instructions\":" + Str("HelL 启动器的本机工具：查实例、装版本/模组/整合包/光影、启动游戏、诊断崩溃。写操作会直接改玩家的游戏目录，执行前请先和用户确认。") + "}");
			}
			case "ping":
				return isNotification ? null : Result(id, "{}");
			case "tools/list":
				return Result(id, "{\"tools\":" + ToolList() + "}");
			case "tools/call":
				return Result(id, CallTool(prm));
			default:
				if (method.StartsWith("notifications/") || isNotification) return null;
				return Error(id, -32601, "Method not found: " + method);
			}
		}
		catch (Exception ex)
		{
			return isNotification ? null : Error(id, -32603, ex.Message);
		}
	}

	private static string ToolList()
	{
		var items = new List<string>();
		foreach (object schema in Tools.TOOL_SCHEMAS)
		{
			string name = Tools.SchemaName(schema);
			if (string.IsNullOrEmpty(name) || _hidden.Contains(name)) continue;
			PyDict fn = (PyDict)((PyDict)schema)["function"];
			string desc = Py.S(fn.Get("description"));
			object parameters = fn.Get("parameters") ?? new PyDict { ["type"] = "object", ["properties"] = new PyDict() };
			bool write = Tools.IsWriteTool(name);
			items.Add("{\"name\":" + Str(name) + ",\"description\":" + Str(desc) + ",\"inputSchema\":" + PyJson.DumpsU(parameters) + ",\"annotations\":{\"readOnlyHint\":" + (write ? "false" : "true") + ",\"destructiveHint\":" + (name.StartsWith("delete_") ? "true" : "false") + "}}");
		}
		return "[" + string.Join(",", items) + "]";
	}

	private static string CallTool(JsonElement prm)
	{
		if (prm.ValueKind != JsonValueKind.Object || !prm.TryGetProperty("name", out JsonElement ne) || ne.ValueKind != JsonValueKind.String)
			return ToolText("缺少工具名 name", true);
		string name = ne.GetString();
		if (_hidden.Contains(name)) return ToolText("该工具只能在启动器界面里用: " + name, true);
		BackendApi backend = Backend;
		if (backend == null) return ToolText("启动器内核还没就绪，请稍后再试", true);
		NoteToolCall(name);
		string args = prm.TryGetProperty("arguments", out JsonElement ae) && ae.ValueKind == JsonValueKind.Object ? ae.GetRawText() : "{}";
		string output = Tools.RunTool(backend, name, args, wait: true);
		bool isError = LooksLikeError(output);
		return ToolText(output, isError);
	}

	/// <summary>记下最近一次外部调用并通知界面。</summary>
	public static void NoteToolCall(string name)
	{
		LastTool = name ?? "";
		LastToolAt = DateTime.Now;
		try { ToolCalled?.Invoke(LastTool, LastToolAt); } catch (Exception) { }
	}

	private static bool LooksLikeError(string output)
	{
		try
		{
			using JsonDocument d = JsonDocument.Parse(output);
			return d.RootElement.ValueKind == JsonValueKind.Object && (d.RootElement.TryGetProperty("error", out _) || (d.RootElement.TryGetProperty("ok", out JsonElement ok) && ok.ValueKind == JsonValueKind.False));
		}
		catch (Exception)
		{
			return false;
		}
	}

	private static string ToolText(string text, bool isError) =>
		"{\"content\":[{\"type\":\"text\",\"text\":" + Str(text ?? "") + "}],\"isError\":" + (isError ? "true" : "false") + "}";

	private static string Result(string id, string result) => id == null ? null : "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":" + result + "}";

	private static string Error(string id, int code, string msg) => "{\"jsonrpc\":\"2.0\",\"id\":" + (id ?? "null") + ",\"error\":{\"code\":" + code + ",\"message\":" + Str(msg) + "}}";

	private static string Str(string s) => JsonSerializer.Serialize(s ?? "");

	private static string ReadLine(Stream s)
	{
		var sb = new StringBuilder();
		int b;
		while ((b = s.ReadByte()) >= 0)
		{
			if (b == '\n') break;
			if (b != '\r') sb.Append((char)b);
			if (sb.Length > 65536) return null;
		}
		return b < 0 && sb.Length == 0 ? null : sb.ToString();
	}

	private static void Write(Stream s, int code, string type, string body)
	{
		byte[] data = Encoding.UTF8.GetBytes(body ?? "");
		string reason = code switch { 200 => "OK", 202 => "Accepted", 400 => "Bad Request", 403 => "Forbidden", 404 => "Not Found", 405 => "Method Not Allowed", _ => "OK" };
		string head = $"HTTP/1.1 {code} {reason}\r\nContent-Type: {type}; charset=utf-8\r\nContent-Length: {data.Length}\r\nConnection: close\r\n\r\n";
		byte[] h = Encoding.ASCII.GetBytes(head);
		s.Write(h, 0, h.Length);
		s.Write(data, 0, data.Length);
		s.Flush();
	}
}
