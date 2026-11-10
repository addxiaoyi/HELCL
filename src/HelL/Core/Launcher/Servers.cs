using System;
using System.Collections.Generic;
using System.Linq;

namespace HelL.Core.Launcher;

public static class Servers
{
	public const string SERVER_FILE = "servers.json";

	public const string GAME_FILE = "servers.dat";

	private static string _server_path(Instance instance)
	{
		return PPath.Join(instance.path, "servers.json");
	}

	private static string _game_path(Instance instance)
	{
		return PPath.Join(instance.path, "servers.dat");
	}

	private static bool PyIsDigit(string s)
	{
		if (s.Length > 0)
		{
			return s.All(char.IsDigit);
		}
		return false;
	}

	public static (string host, long port) _split_addr(object ip, object port = null)
	{
		string text = Py.SS(ip);
		long num;
		if (port == null || port is string { Length: 0 })
		{
			num = 25565L;
		}
		else
		{
			try
			{
				num = Py.Int(port);
			}
			catch (Exception ex) when (ex is TypeError || ex is ValueError)
			{
				num = 25565L;
			}
		}
		if (1 > num || num > 65535)
		{
			num = 25565L;
		}
		if (text.Contains(":"))
		{
			int num2 = text.LastIndexOf(':');
			string text3 = text.Substring(0, num2);
			string text4 = text.Substring(num2 + 1);
			if (text3.Length > 0 && (!text3.Contains(":") || (text3.StartsWith("[") && text3.EndsWith("]"))) && PyIsDigit(text4))
			{
				long num3;
				try
				{
					num3 = Py.Int(text4);
				}
				catch (Exception)
				{
					num3 = -1L;
				}
				if (1 <= num3 && num3 <= 65535)
				{
					return (host: text3, port: (num == 25565) ? num3 : num);
				}
			}
		}
		return (host: text, port: num);
	}

	private static List<object> _read_json_rows(Instance instance)
	{
		return (Utils.ReadJson(_server_path(instance), new List<object>()) as List<object>) ?? new List<object>();
	}

	private static string AddrKey(string h, long p)
	{
		return h + "\0" + p;
	}

	private static List<object> _read_servers(Instance instance)
	{
		List<object> list = Terracotta.ReadGameServers(_game_path(instance));
		List<object> list2 = _read_json_rows(instance);
		if (list.Count > 0 || LocalFs.IsFile(_game_path(instance)))
		{
			Dictionary<string, PyDict> dictionary = new Dictionary<string, PyDict>();
			foreach (object item in list2)
			{
				if (item is PyDict pyDict)
				{
					var (h, p) = _split_addr(Py.Or(pyDict["ip"], ""), pyDict["port"]);
					dictionary[AddrKey(h, p)] = pyDict;
				}
			}
			List<object> list3 = new List<object>();
			{
				foreach (PyDict item2 in list)
				{
					var (text, num) = _split_addr(Py.Or(item2["ip"], ""));
					if (!dictionary.TryGetValue(AddrKey(text, num), out var value))
					{
						value = new PyDict();
					}
					list3.Add(PyDict.Of("name", Py.Or(item2["name"], value["name"], text), "ip", text, "port", num, "icon", Py.Or(value["icon"], ""), "description", Py.Or(value["description"], ""), "hidden", Py.Truthy(item2["hidden"])));
				}
				return list3;
			}
		}
		return list2.Where((object r) => r is PyDict).ToList();
	}

	private static void _write_servers(Instance instance, List<object> servers)
	{
		Utils.EnsureDir(instance.path);
		List<object> list = new List<object>();
		List<object> list2 = new List<object>();
		foreach (object server in servers)
		{
			if (server is PyDict pyDict)
			{
				var (text, num) = _split_addr(Py.Or(pyDict["ip"], ""), pyDict["port"]);
				if (text.Length != 0)
				{
					PyDict pyDict2 = new PyDict(pyDict);
					pyDict2["ip"] = text;
					pyDict2["port"] = num;
					list2.Add(pyDict2);
					list.Add(PyDict.Of("name", Py.Or(pyDict["name"], text), "ip", (num == 25565) ? text : (text + ":" + num), "hidden", (long)(Py.Truthy(pyDict["hidden"]) ? 1 : 0)));
				}
			}
		}
		Terracotta.WriteGameServers(_game_path(instance), list);
		Utils.WriteJson(_server_path(instance), list2);
	}

	public static List<object> ListServers(Instance instance)
	{
		List<object> list = _read_servers(instance);
		List<object> list2 = new List<object>();
		for (int i = 0; i < list.Count; i++)
		{
			if (list[i] is PyDict s)
			{
				list2.Add(_normalize(s, i));
			}
		}
		return list2;
	}

	private static PyDict _normalize(PyDict s, long index = 0L)
	{
		var (text, num) = _split_addr(Py.Or(s["ip"], ""), s["port"]);
		return PyDict.Of("name", Py.Str(Py.Or(s["name"], text, "服务器 #" + (index + 1))), "ip", text, "port", num, "icon", Py.Str(s.Get("icon", "")), "description", Py.Str(s.Get("description", "")), "hidden", Py.Truthy(s.Get("hidden", false)), "index", index);
	}

	public static PyDict GetServer(Instance instance, long index)
	{
		foreach (PyDict item in ListServers(instance))
		{
			if (Py.Eq(item["index"], index))
			{
				return item;
			}
		}
		return null;
	}

	private static string StripStr(object v, string attr = "strip")
	{
		return Py.Strip(Py.AsStr(v, attr));
	}

	public static PyDict AddServer(Instance instance, object name, object ip, object port = null, object description = null, object icon = null)
	{
		description = description ?? "";
		icon = icon ?? "";
		if (!Py.Truthy(ip) || StripStr(ip).Length == 0)
		{
			throw new ServerError("服务器地址不能为空");
		}
		long num = (Py.Truthy(port) ? Py.Int(port) : 25565);
		if (num < 1 || num > 65535)
		{
			throw new ServerError("端口号必须在 1-65535 之间");
		}
		List<object> list = _read_servers(instance);
		PyDict pyDict = PyDict.Of("name", StripStr(Py.Or(name, ip)), "ip", StripStr(ip), "port", num, "icon", icon, "description", StripStr(description));
		list.Add(pyDict);
		_write_servers(instance, list);
		return _normalize(pyDict, list.Count - 1);
	}

	private static long IndexArg(object index)
	{
		if (index is bool flag)
		{
			return flag ? 1 : 0;
		}
		if (index is long)
		{
			return (long)index;
		}
		if (index is double a)
		{
			return (long)Math.Ceiling(a);
		}
		throw new TypeError("'<' not supported between instances of '" + Py.TypeName(index) + "' and 'int'");
	}

	public static PyDict UpdateServer(Instance instance, object index, PyDict kwargs)
	{
		List<object> list = _read_servers(instance);
		long num = IndexArg(index);
		bool flag;
		if (!(index is double num2))
		{
			if (num < 0)
			{
				goto IL_0050;
			}
			flag = num >= list.Count;
		}
		else
		{
			if (num2 < 0.0)
			{
				goto IL_0050;
			}
			flag = num2 >= (double)list.Count;
		}
		if (!flag)
		{
			if (index is double)
			{
				throw new TypeError("list indices must be integers or slices, not float");
			}
			if (!(list[(int)num] is PyDict pyDict))
			{
				throw new ServerError(Py.Fmt("服务器数据损坏: {0}", index));
			}
			kwargs = kwargs ?? new PyDict();
			if (kwargs.ContainsKey("name"))
			{
				pyDict["name"] = Py.Strip(Py.Str(kwargs["name"]));
			}
			if (kwargs.ContainsKey("ip"))
			{
				string text = Py.Strip(Py.Str(kwargs["ip"]));
				if (text.Length == 0)
				{
					throw new ServerError("服务器地址不能为空");
				}
				pyDict["ip"] = text;
			}
			if (kwargs.ContainsKey("port"))
			{
				long num3 = Py.Int(kwargs["port"]);
				if (num3 < 1 || num3 > 65535)
				{
					throw new ServerError("端口号必须在 1-65535 之间");
				}
				pyDict["port"] = num3;
			}
			if (kwargs.ContainsKey("description"))
			{
				pyDict["description"] = Py.Strip(Py.Str(kwargs["description"]));
			}
			if (kwargs.ContainsKey("icon"))
			{
				pyDict["icon"] = Py.Str(kwargs["icon"]);
			}
			if (kwargs.ContainsKey("hidden"))
			{
				pyDict["hidden"] = Py.Truthy(kwargs["hidden"]);
			}
			_write_servers(instance, list);
			return _normalize(pyDict, num);
		}
		goto IL_0050;
		IL_0050:
		throw new ServerError(Py.Fmt("服务器索引 {0} 不存在", index));
	}

	public static void DeleteServer(Instance instance, object index)
	{
		List<object> list = _read_servers(instance);
		long num = IndexArg(index);
		bool flag;
		if (!(index is double num2))
		{
			if (num < 0)
			{
				goto IL_0050;
			}
			flag = num >= list.Count;
		}
		else
		{
			if (num2 < 0.0)
			{
				goto IL_0050;
			}
			flag = num2 >= (double)list.Count;
		}
		if (!flag)
		{
			if (index is double)
			{
				throw new TypeError("list indices must be integers or slices, not float");
			}
			list.RemoveAt((int)num);
			_write_servers(instance, list);
			return;
		}
		goto IL_0050;
		IL_0050:
		throw new ServerError(Py.Fmt("服务器索引 {0} 不存在", index));
	}

	public static long ImportServersTxt(Instance instance, object text0)
	{
		string s = Py.AsStr(text0, "splitlines");
		long num = 0L;
		List<object> list = _read_servers(instance);
		HashSet<string> hashSet = new HashSet<string>();
		foreach (object item2 in list)
		{
			if (item2 is PyDict pyDict)
			{
				hashSet.Add(Py.Str(pyDict.Get("ip", "")) + ":" + Py.Str(pyDict.Get("port", 25565L)));
			}
		}
		foreach (string item3 in Py.SplitLines(s))
		{
			string text1 = Py.Strip(item3);
			if (text1.Length == 0 || text1.StartsWith("#"))
			{
				continue;
			}
			string text2;
			string text3;
			if (text1.Contains("\t"))
			{
				int num2 = text1.IndexOf('\t');
				text2 = Py.Strip(text1.Substring(0, num2));
				text3 = Py.Strip(text1.Substring(num2 + 1));
			}
			else
			{
				text2 = "";
				text3 = text1;
			}
			long num4;
			string s2;
			if (text3.Contains(":"))
			{
				int num3 = text3.LastIndexOf(':');
				s2 = text3.Substring(0, num3);
				string o = text3.Substring(num3 + 1);
				try
				{
					num4 = Py.Int(o);
				}
				catch (ValueError)
				{
					num4 = 25565L;
				}
				s2 = Py.Strip(s2);
			}
			else
			{
				s2 = text3;
				num4 = 25565L;
			}
			if (s2.Length != 0)
			{
				string item = s2 + ":" + num4;
				if (!hashSet.Contains(item))
				{
					list.Add(PyDict.Of("name", (text2.Length > 0) ? text2 : s2, "ip", s2, "port", num4));
					hashSet.Add(item);
					num++;
				}
			}
		}
		if (num > 0)
		{
			_write_servers(instance, list);
		}
		return num;
	}

	public static string ExportServersTxt(Instance instance)
	{
		List<object> list = ListServers(instance);
		List<string> list2 = new List<string>
		{
			"# HelL 服务器列表导出",
			"# 共 " + list.Count + " 个服务器",
			""
		};
		foreach (PyDict item in list)
		{
			string text = Py.Str(item["name"]);
			string text2 = Py.Str(item["ip"]);
			string text3 = Py.Str(item["port"]);
			if (text.Length > 0 && text != text2)
			{
				list2.Add(text + "\t" + text2 + ":" + text3);
			}
			else
			{
				list2.Add(text2 + ":" + text3);
			}
		}
		return string.Join("\n", list2);
	}
}
