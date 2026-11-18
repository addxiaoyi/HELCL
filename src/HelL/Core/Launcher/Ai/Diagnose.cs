using System;
using System.Collections.Generic;
using System.Linq;

namespace HelL.Core.Launcher.Ai;

public static class Diagnose
{
	public static PyDict Run(Instance instance, string extraLog = "")
	{
		PyDict pyDict = Crash.AnalyzeInstance(instance, extraLog);
		List<object> list = new List<object>();
		foreach (PyDict item in Py.ListOr(Py.Or(pyDict.Get("reasons"), new List<object>())))
		{
			List<object> list2 = Py.ListOr(Py.Or(item.Get("extra"), new List<object>()));
			list.Add(PyDict.Of("code", item.Get("code"), "title", Py.Or(item.Get("title"), item.Get("code")), "snippet", (list2.Count > 0) ? Py.Slice(string.Join("\n", list2.Select(Py.Str)), null, 400) : Py.Or(pyDict.Get("summary"), "")));
		}
		object obj = "";
		foreach (object item2 in Py.ListOr(Py.Or(pyDict.Get("files"), new List<object>())))
		{
			if (Py.Lower(Py.Str(item2)).EndsWith("latest.log", StringComparison.Ordinal))
			{
				obj = item2;
				break;
			}
		}
		return PyDict.Of("instance", Py.Or(pyDict.Get("instance"), instance.name), "has_latest", Py.Truthy(pyDict.Get("has_latest")), "has_crash", Py.Truthy(pyDict.Get("has_crash")), "has_hs_err", Py.Truthy(pyDict.Get("has_hs_err")), "latest_path", obj, "crash_path", Py.Or(pyDict.Get("direct_file"), ""), "findings", list, "hint", Py.Or(pyDict.Get("summary"), pyDict.Get("detail"), ""), "detail", Py.Or(pyDict.Get("detail"), ""), "latest_tail", Py.Or(pyDict.Get("log_mc"), ""), "crash_tail", Py.Or(pyDict.Get("log_crash"), ""));
	}

	public static PyDict CollectLogs(Instance instance)
	{
		string path = instance.path;
		string text = "";
		string text2 = "";
		string text3 = "";
		string text4 = "";
		string text5 = "";
		string text6 = "";
		string s = "";
		foreach (string item in Crash.CollectFiles(path, null, loose: true))
		{
			string text7 = Crash.Classify(item);
			string text8 = Crash.ReadText(item, 120000L);
			string text9 = Py.Lower(new PPath(item).Name);
			if (text7 == "mc" && text9.StartsWith("latest", StringComparison.Ordinal) && text.Length == 0)
			{
				text = text8;
				text4 = item;
			}
			else if (text7 == "crash" && text2.Length == 0)
			{
				text2 = text8;
				text5 = item;
			}
			else if (text7 == "hs" && text3.Length == 0)
			{
				text3 = text8;
				text6 = item;
			}
			else if (text9 == "debug.log")
			{
				s = text8;
			}
		}
		if (text.Length == 0)
		{
			string text10 = PPath.Join(path, "logs", "latest.log");
			if (LocalFs.IsFile(text10))
			{
				text = Crash.ReadText(text10, 120000L);
				text4 = text10;
			}
		}
		return PyDict.Of("latest_path", text4, "crash_path", text5, "hs_path", text6, "latest", text, "crash", text2, "hs_err", text3, "debug_tail", Py.Slice(s, -20000));
	}

	public static string LogExcerpt(Instance instance, string kind = "latest", int maxChars = 6000)
	{
		PyDict pyDict = CollectLogs(instance);
		string key = ((kind == "crash") ? "crash" : ((kind == "hs") ? "hs_err" : "latest"));
		string key2 = ((kind == "crash") ? "crash_path" : ((kind == "hs") ? "hs_path" : "latest_path"));
		string text = Py.S(pyDict.Get(key));
		string text2 = Py.S(pyDict.Get(key2));
		if (text.Length == 0)
		{
			return "没有 " + kind + " 日志";
		}
		return "文件: " + text2 + "\n\n" + Py.Slice(text, -maxChars);
	}
}
