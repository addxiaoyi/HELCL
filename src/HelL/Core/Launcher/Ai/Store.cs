using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace HelL.Core.Launcher.Ai;

public static class Store
{
	public const int MAX_CHATS = 40;

	public const int MAX_MESSAGES = 200;

	private static readonly string[] _KEEP_FIELDS = new string[6] { "tool_calls", "tool_call_id", "name", "id", "note", "images" };

	public const string STEER_ID_PREFIX = "steer_";

	public static string SESSIONS_DIR = null;

	private static readonly object _EVENT_LOCK = new object();

	public const int EVENT_LOG_KEEP_DAYS = 30;

	public const long EVENT_LOG_MAX_BYTES = 4194304L;

	private static bool _event_pruned;

	public static string STORE_FILE => PPath.Join(Utils.ROOT, "ai_chats.json");

	private static PyDict _Empty()
	{
		string text = _NewId();
		return PyDict.Of("active_id", text, "chats", Py.L(_BlankChat(text)));
	}

	public static string _NewId()
	{
		return Guid.NewGuid().ToString("N").Substring(0, 12);
	}

	private static PyDict _BlankChat(string cid = null)
	{
		return PyDict.Of("id", cid ?? _NewId(), "title", "新对话", "updated", (long)Py.Time(), "messages", new List<object>());
	}

	public static bool IsSteerMessage(object m)
	{
		if (m is PyDict pyDict && Py.Eq(pyDict.Get("role"), "user"))
		{
			return Py.S(pyDict.Get("id")).StartsWith("steer_", StringComparison.Ordinal);
		}
		return false;
	}

	private static PyDict _LoadMessage(PyDict m)
	{
		PyDict pyDict = PyDict.Of("role", m.Get("role"), "content", Py.Or(m.Get("content"), ""));
		string[] kEEP_FIELDS = _KEEP_FIELDS;
		foreach (string key in kEEP_FIELDS)
		{
			if (m.Get(key) != null)
			{
				pyDict[key] = m[key];
			}
		}
		return pyDict;
	}

	private static List<object> Tail(List<object> l, int n)
	{
		if (l.Count <= n)
		{
			return l;
		}
		return l.GetRange(l.Count - n, n);
	}

	public static PyDict Load()
	{
		if (!(Utils.ReadJson(STORE_FILE) is PyDict pyDict) || !(pyDict.Get("chats") is List<object> { Count: not 0 } list))
		{
			PyDict pyDict2 = _Empty();
			Save(pyDict2);
			return pyDict2;
		}
		List<object> list2 = new List<object>();
		foreach (object item in list)
		{
			if (!(item is PyDict pyDict3) || !Py.Truthy(pyDict3.Get("id")))
			{
				continue;
			}
			List<object> list3 = new List<object>();
			foreach (object item2 in Py.ListOr(Py.Or(pyDict3.Get("messages"), new List<object>())))
			{
				if (item2 is PyDict pyDict4 && Py.In(pyDict4.Get("role"), Py.L("user", "assistant", "error", "tool")))
				{
					list3.Add(_LoadMessage(pyDict4));
				}
			}
			PyDict pyDict5 = PyDict.Of("id", Py.Str(pyDict3["id"]), "title", Py.Slice(Py.Str(Py.Or(pyDict3.Get("title"), "对话")), null, 40), "updated", Py.Int(Py.Or(pyDict3.Get("updated"), 0L)), "messages", Tail(list3, 200));
			if (pyDict3.Get("plan") is PyDict)
			{
				pyDict5["plan"] = pyDict3["plan"];
			}
			list2.Add(pyDict5);
		}
		if (list2.Count == 0)
		{
			PyDict pyDict6 = _Empty();
			Save(pyDict6);
			return pyDict6;
		}
		string active = Py.S(pyDict.Get("active_id"));
		if (!list2.Any((object c) => Py.Eq(((PyDict)c)["id"], active)))
		{
			active = (string)((PyDict)list2[0])["id"];
		}
		return PyDict.Of("active_id", active, "chats", list2);
	}

	public static void Save(PyDict data)
	{
		List<object> list = Py.ListOr(Py.Or(data.Get("chats"), new List<object>()));
		List<object> list2 = ((list.Count > 40) ? list.GetRange(0, 40) : list);
		string active = Py.S(data.Get("active_id"));
		if (list2.Count > 0 && !list2.Any((object c) => Py.Eq(((PyDict)c).Get("id"), active)))
		{
			active = Py.Str(((PyDict)list2[0])["id"]);
			data["active_id"] = active;
		}
		Utils.WriteJson(STORE_FILE, PyDict.Of("active_id", (list2.Count > 0) ? active : "", "chats", list2));
	}

	public static PyDict NewChat(PyDict data)
	{
		PyDict pyDict = _BlankChat();
		List<object> list = new List<object> { pyDict };
		list.AddRange(Py.ListOr(Py.Or(data.Get("chats"), new List<object>())));
		data["chats"] = ((list.Count > 40) ? list.GetRange(0, 40) : list);
		data["active_id"] = pyDict["id"];
		Save(data);
		return pyDict;
	}

	public static PyDict GetChat(PyDict data, object cid)
	{
		foreach (object item in Py.ListOr(Py.Or(data.Get("chats"), new List<object>())))
		{
			if (Py.Eq(((PyDict)item).Get("id"), cid))
			{
				return (PyDict)item;
			}
		}
		return null;
	}

	public static PyDict SetActive(PyDict data, object cid)
	{
		PyDict chat = GetChat(data, cid);
		if (chat == null)
		{
			return null;
		}
		data["active_id"] = cid;
		Save(data);
		return chat;
	}

	public static List<object> ApiMessages(List<object> messages)
	{
		List<object> list = new List<object>();
		foreach (object item in messages ?? new List<object>())
		{
			if (!(item is PyDict pyDict))
			{
				continue;
			}
			object a = pyDict.Get("role");
			if (Py.Eq(a, "error"))
			{
				a = "assistant";
			}
			if (Py.Eq(a, "tool"))
			{
				PyDict pyDict2 = PyDict.Of("role", "tool", "content", Py.Or(pyDict.Get("content"), ""));
				if (Py.Truthy(pyDict.Get("tool_call_id")))
				{
					pyDict2["tool_call_id"] = pyDict["tool_call_id"];
				}
				if (Py.Truthy(pyDict.Get("name")))
				{
					pyDict2["name"] = pyDict["name"];
				}
				list.Add(pyDict2);
			}
			else if (Py.Eq(a, "assistant"))
			{
				PyDict pyDict3 = PyDict.Of("role", "assistant", "content", Py.Or(pyDict.Get("content"), ""));
				if (Py.Truthy(pyDict.Get("tool_calls")))
				{
					pyDict3["tool_calls"] = pyDict["tool_calls"];
				}
				list.Add(pyDict3);
			}
			else if (Py.Eq(a, "user"))
			{
				PyDict pyDict4 = PyDict.Of("role", "user", "content", Py.Or(pyDict.Get("content"), ""));
				if (Py.Truthy(pyDict.Get("images")))
				{
					pyDict4["images"] = Py.ListCtor(pyDict["images"]);
				}
				list.Add(pyDict4);
			}
		}
		while (list.Count > 0 && Py.Eq(((PyDict)list[0]).Get("role"), "tool"))
		{
			list.RemoveAt(0);
		}
		return list;
	}

	public static void UpsertMessages(PyDict data, object cid, List<object> messages, string title = null)
	{
		PyDict chat = GetChat(data, cid);
		if (chat == null)
		{
			return;
		}
		List<object> l = (messages ?? new List<object>()).Select((object m) => (m is PyDict m2) ? _LoadMessage(m2) : m).ToList();
		chat["messages"] = Tail(l, 200);
		chat["updated"] = (long)Py.Time();
		if (!string.IsNullOrEmpty(title))
		{
			chat["title"] = Py.Slice(title, null, 40);
		}
		else if (Py.In(chat.Get("title"), Py.L("", "新对话")))
		{
			foreach (PyDict item in (List<object>)chat["messages"])
			{
				if (Py.Eq(item.Get("role"), "user") && Py.Strip(Py.S(item.Get("content"))).Length > 0)
				{
					chat["title"] = Py.Slice(Py.Strip((string)item["content"]).Replace("\n", " "), null, 24);
					break;
				}
			}
		}
		data["chats"] = Py.Sorted((List<object>)data["chats"], (object c) => Py.Or(((PyDict)c).Get("updated"), 0L), reverse: true);
		Save(data);
	}

	public static void PruneEmpty(PyDict data)
	{
		List<object> source = Py.ListOr(Py.Or(data.Get("chats"), new List<object>()));
		List<PyDict> list = source.Where((object c) => !Py.Truthy(((PyDict)c).Get("messages"))).Cast<PyDict>().ToList();
		if (list.Count > 1)
		{
			object active = data.Get("active_id");
			PyDict keep = list.FirstOrDefault((PyDict c) => Py.Eq(c.Get("id"), active)) ?? list[0];
			HashSet<string> drop = new HashSet<string>(from c in list
				where !Py.Eq(c["id"], keep["id"])
				select Py.Str(c["id"]));
			data["chats"] = source.Where((object c) => !drop.Contains(Py.Str(((PyDict)c).Get("id")))).ToList();
			if (active is string item && drop.Contains(item))
			{
				List<object> list2 = (List<object>)data["chats"];
				data["active_id"] = ((list2.Count > 0) ? ((PyDict)list2[0])["id"] : "");
			}
			Save(data);
		}
	}

	public static PyDict DeleteChat(PyDict data, object cid)
	{
		data["chats"] = (from c in Py.ListOr(Py.Or(data.Get("chats"), new List<object>()))
			where !Py.Eq(((PyDict)c).Get("id"), cid)
			select c).ToList();
		List<object> list = (List<object>)data["chats"];
		if (list.Count == 0)
		{
			PyDict pyDict = _BlankChat();
			data["chats"] = Py.L(pyDict);
			data["active_id"] = pyDict["id"];
		}
		else if (Py.Eq(data.Get("active_id"), cid))
		{
			data["active_id"] = ((PyDict)list[0])["id"];
		}
		Save(data);
		return GetChat(data, data["active_id"]);
	}

	public static void SetPlan(PyDict data, object cid, PyDict plan)
	{
		PyDict chat = GetChat(data, cid);
		if (chat != null)
		{
			if (plan == null)
			{
				chat.Pop("plan");
			}
			else
			{
				chat["plan"] = plan;
			}
			Save(data);
		}
	}

	private static void _PruneEventLogs(string d)
	{
		if (_event_pruned)
		{
			return;
		}
		_event_pruned = true;
		try
		{
			double num = Py.Time() - 2592000.0;
			string[] files = Directory.GetFiles(d, "*.jsonl");
			foreach (string fileName in files)
			{
				try
				{
					FileInfo fileInfo = new FileInfo(fileName);
					if ((fileInfo.LastWriteTimeUtc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds < num || fileInfo.Length > 4194304)
					{
						fileInfo.Delete();
					}
				}
				catch (Exception)
				{
				}
			}
		}
		catch (Exception)
		{
		}
	}

	public static void LogEvent(string sessionId, string ev, PyDict fields = null)
	{
		try
		{
			string text = SESSIONS_DIR ?? PPath.Join(Utils.ROOT, "cache", "ai_sessions");
			Utils.EnsureDir(text);
			_PruneEventLogs(Utils.AbsPath(text));
			PyDict pyDict = PyDict.Of("ts", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fff"), "event", ev ?? "");
			if (fields != null)
			{
				pyDict.Update(fields);
			}
			string path = Utils.AbsPath(PPath.Join(text, (string.IsNullOrEmpty(sessionId) ? "active" : sessionId) + ".jsonl"));
			lock (_EVENT_LOCK)
			{
				File.AppendAllText(path, PyJson.Dumps(pyDict, null, ensureAscii: false) + "\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			}
		}
		catch (Exception)
		{
		}
	}
}
