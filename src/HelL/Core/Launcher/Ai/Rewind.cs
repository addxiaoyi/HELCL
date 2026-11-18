using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace HelL.Core.Launcher.Ai;

public static class Rewind
{
	private static List<string> _turn_candidates(string chatId)
	{
		try
		{
			string text = Store.SESSIONS_DIR ?? PPath.Join(Utils.ROOT, "cache", "ai_sessions");
			string path = Utils.AbsPath(PPath.Join(text, (Py.Truthy(chatId) ? chatId : "active") + ".jsonl"));
			if (!File.Exists(path))
			{
				return new List<string>();
			}
			List<string> list = new List<string>();
			HashSet<string> rewound = new HashSet<string>();
			string[] array = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false).GetString(File.ReadAllBytes(path)).Split('\n');
			foreach (string text2 in array)
			{
				if (text2.Length == 0)
				{
					continue;
				}
				object obj;
				try
				{
					obj = PyJson.Loads(text2);
				}
				catch (Exception)
				{
					continue;
				}
				PyDict pyDict = (obj as PyDict) ?? throw new AttributeError("get");
				string text3 = Py.Str(Py.Or(pyDict["turn_id"], ""));
				if (text3.Length != 0)
				{
					object a = pyDict["event"];
					if (Py.Eq(a, "TurnStarted"))
					{
						list.Add(text3);
					}
					else if (Py.Eq(a, "TurnRewound"))
					{
						rewound.Add(text3);
					}
				}
			}
			return list.Where((string t) => !rewound.Contains(t)).ToList();
		}
		catch (Exception)
		{
			return new List<string>();
		}
	}

	private static void _mark_rewound(string chatId, string turnId)
	{
		try
		{
			Store.LogEvent(chatId, "TurnRewound", PyDict.Of("turn_id", turnId ?? ""));
		}
		catch (Exception)
		{
		}
	}

	private static void _clear_plan_of(string chatId, string turnId)
	{
		try
		{
			PyDict data = Store.Load();
			if ((Store.GetChat(data, chatId) ?? new PyDict())["plan"] is PyDict pyDict && Py.Str(Py.Or(pyDict["turn_id"], "")) == turnId)
			{
				Store.SetPlan(data, chatId, null);
			}
		}
		catch (Exception)
		{
		}
	}

	private static PyDict R(bool ok, bool truncated, bool rollbackable)
	{
		return PyDict.Of("ok", ok, "truncated", truncated, "restored_files", 0L, "restored_bytes", 0L, "rollbackable", rollbackable, "disk_changed", false);
	}

	public static PyDict RewindLastRound(object chatIdObj)
	{
		string text = Py.Str(Py.Truthy(chatIdObj) ? chatIdObj : "active");
		PyDict data = Store.Load();
		PyDict chat = Store.GetChat(data, text);
		if (chat == null || chat.Count == 0)
		{
			return R(ok: false, truncated: false, rollbackable: false);
		}
		List<object> list = Py.ListOr(Py.Or(chat["messages"], new List<object>()));
		int num = -1;
		for (int num2 = list.Count - 1; num2 >= 0; num2--)
		{
			PyDict pyDict = (list[num2] as PyDict) ?? throw new AttributeError("'" + Py.TypeName(list[num2]) + "' object has no attribute 'get'");
			if (Py.Eq(pyDict["role"], "user") && !Store.IsSteerMessage(pyDict))
			{
				num = num2;
				break;
			}
		}
		if (num < 0)
		{
			return R(ok: true, truncated: false, rollbackable: false);
		}
		chat["messages"] = list.GetRange(0, num);
		chat["updated"] = (long)Math.Floor(Py.Time());
		Store.Save(data);
		List<string> list2 = _turn_candidates(text);
		string text2 = Checkpoint.LastTurnId(text);
		string text3 = "";
		if (list2.Count > 0)
		{
			text3 = list2[list2.Count - 1];
			PyDict pyDict2 = R(ok: true, truncated: true, text2.Length > 0);
			if (text3.Length == 0)
			{
				return pyDict2;
			}
			bool flag = Checkpoint.TurnHasOps(text, text3);
			if (!flag && list2.Count > 0)
			{
				_mark_rewound(text, text3);
				_clear_plan_of(text, text3);
				return pyDict2;
			}
			if (flag)
			{
				PyDict pyDict3 = Checkpoint.Rollback(text, text3);
				pyDict2["restored_files"] = Py.Or(pyDict3["restored_files"], 0L);
				pyDict2["restored_bytes"] = Py.Or(pyDict3["restored_bytes"], 0L);
				pyDict2["ok"] = Py.Truthy(pyDict3["ok"]);
				pyDict2["disk_changed"] = true;
			}
			_clear_plan_of(text, text3);
			_mark_rewound(text, text3);
			return pyDict2;
		}
		PyDict pyDict4 = R(ok: true, truncated: true, rollbackable: false);
		pyDict4["reason"] = "会话日志缺失，无法定位该轮的磁盘改动；已只回退对话";
		return pyDict4;
	}
}
