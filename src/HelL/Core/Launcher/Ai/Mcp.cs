using System;
using System.Collections.Generic;
using System.Linq;

namespace HelL.Core.Launcher.Ai;

public static class Mcp
{
	private static List<PyDict> _server_configs(PyDict settings)
	{
		List<PyDict> list = new List<PyDict>();
		if (!((settings ?? new PyDict()).Get("ai_mcp_servers") is List<object> list2))
		{
			return list;
		}
		foreach (object item in list2)
		{
			if (!(item is PyDict pyDict))
			{
				continue;
			}
			string text = Py.Strip(Py.S(pyDict.Get("name")));
			string text2 = Py.Strip(Py.S(pyDict.Get("command")));
            if (text == "minecraft") { Trace.Record("mcp_config_skipped", null, "", text, null, null, null, "reason", "minecraft 是内置 AI 玩家保留名称"); continue; }
			if (text.Length == 0 || text2.Length == 0)
			{
				continue;
			}
			if (!PyDifflib.CodePoints(text).All((int cp) => (cp < 65536 && char.IsLetterOrDigit((char)cp)) || cp == 45 || cp == 95))
			{
				object[] extra = new object[2] { "reason", "server 名只允许字母数字-_" };
				Trace.Record("mcp_config_skipped", null, "", text, null, null, null, extra);
				continue;
			}
			PyDict pyDict2 = null;
			if (pyDict.Get("env") is PyDict pyDict3)
			{
				pyDict2 = new PyDict();
				foreach (KeyValuePair<string, object> item2 in pyDict3)
				{
					pyDict2[item2.Key] = Py.Str(item2.Value);
				}
			}
			list.Add(PyDict.Of("name", text, "command", text2, "args", (pyDict.Get("args") as List<object>) ?? new List<object>(), "env", pyDict2));
		}
		return list;
	}

	public static List<McpClient> ConnectServers(PyDict settings)
	{
		List<McpClient> list = new List<McpClient>();
		foreach (PyDict item in _server_configs(settings))
		{
			McpClient mcpClient = new McpClient((string)item["name"], (string)item["command"], (List<object>)item["args"], item["env"] as PyDict);
			try
			{
				mcpClient.Connect();
				list.Add(mcpClient);
			}
			catch (Exception exc)
			{
				mcpClient.Close();
				Trace.Record("mcp_connect_failed", null, "", (string)item["name"], exc, null, null);
			}
		}
		return list;
	}

	public static void CloseAll(List<McpClient> clients)
	{
		foreach (McpClient item in clients ?? new List<McpClient>())
		{
			item.Close();
		}
	}

	public static List<object> McpToolSchemas(List<McpClient> clients)
	{
		List<object> list = new List<object>();
		foreach (McpClient item in clients ?? new List<McpClient>())
		{
			List<object> list2;
			try
			{
				list2 = item.ListTools();
			}
			catch (Exception exc)
			{
				Trace.Record("mcp_list_tools_failed", null, "", item.name, exc, null, null);
				continue;
			}
			foreach (object item2 in list2)
			{
				if (item2 is PyDict pyDict && Py.Truthy(pyDict.Get("name")))
				{
					string text = "mcp_" + item.name + "_" + Py.Str(pyDict["name"]);
					list.Add(PyDict.Of("type", "function", "function", PyDict.Of("name", text, "description", Py.Slice(Py.Str(Py.Or(pyDict.Get("description"), text)), null, 800), "parameters", (pyDict.Get("inputSchema") is PyDict pyDict2) ? pyDict2 : PyDict.Of("type", "object", "properties", new PyDict()))));
				}
			}
		}
		return list;
	}

	public static string RouteCall(List<McpClient> clients, string name, PyDict args)
	{
		if (!name.StartsWith("mcp_", StringComparison.Ordinal))
		{
			return null;
		}
		string text = name.Substring(4);
		foreach (McpClient item in (clients ?? new List<McpClient>()).OrderByDescending((McpClient c) => c.name.Length))
		{
			string text2 = item.name + "_";
			if (text.StartsWith(text2, StringComparison.Ordinal))
			{
				return item.CallTool(text.Substring(text2.Length), args);
			}
		}
		throw new McpError("没有叫 " + name + " 的 MCP 工具（server 可能已崩）");
	}
}
