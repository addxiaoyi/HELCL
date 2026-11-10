using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HelL.Core.Launcher.Ai;

public static class Hooks
{
	public static string HOOKS_DIR = null;

	private static readonly object _LOCK = new object();

	private static volatile bool _enabled = true;

	private static readonly List<Hook> _hooks = new List<Hook>
	{
		new Hook("write_audit", _audit_before)
	};

	public static void Register(Hook h)
	{
		lock (_LOCK)
		{
			_hooks.Add(h);
		}
	}

	public static void Unregister(string name)
	{
		lock (_LOCK)
		{
			_hooks.RemoveAll((Hook h) => h.name == name);
		}
	}

	public static void Clear()
	{
		lock (_LOCK)
		{
			_hooks.Clear();
		}
	}

	public static void SetEnabled(bool on)
	{
		_enabled = on;
	}

	public static (PyDict args, List<string> errors) RunBefore(string toolName, PyDict args)
	{
		PyDict pyDict = ((args != null) ? args.Copy() : new PyDict());
		List<string> list = new List<string>();
		if (!_enabled)
		{
			return (args: pyDict, errors: list);
		}
		List<Hook> list2;
		lock (_LOCK)
		{
			list2 = new List<Hook>(_hooks);
		}
		foreach (Hook item in list2)
		{
			if (item.before == null)
			{
				continue;
			}
			try
			{
				if (item.before(toolName, pyDict) is PyDict pyDict2)
				{
					pyDict = pyDict2;
				}
			}
			catch (Exception ex)
			{
				list.Add(item.name + ": " + Py.ExcStr(ex));
				Trace.Record("hook_error", null, "before", toolName, ex, null, null);
			}
		}
		return (args: pyDict, errors: list);
	}

	public static (string result, List<string> errors) RunAfter(string toolName, string result)
	{
		string text = result;
		List<string> list = new List<string>();
		if (!_enabled)
		{
			return (result: text, errors: list);
		}
		List<Hook> list2;
		lock (_LOCK)
		{
			list2 = new List<Hook>(_hooks);
		}
		foreach (Hook item in list2)
		{
			if (item.after == null)
			{
				continue;
			}
			try
			{
				if (item.after(toolName, text) is string text2)
				{
					text = text2;
				}
			}
			catch (Exception ex)
			{
				list.Add(item.name + ": " + Py.ExcStr(ex));
				Trace.Record("hook_error", null, "after", toolName, ex, null, null);
			}
		}
		return (result: text, errors: list);
	}

	private static string _audit_dir()
	{
		string obj = HOOKS_DIR ?? PPath.Join(Utils.ROOT, "cache", "ai_hooks");
		Utils.EnsureDir(obj);
		return obj;
	}

	private static bool _audit_worthy(string toolName)
	{
		ToolMetaInfo toolMetaInfo = ToolMeta.Get(toolName);
		if (toolMetaInfo != null)
		{
			if (!(toolMetaInfo.side_effect == "write_local") && !(toolMetaInfo.side_effect == "write_external") && !(toolMetaInfo.side_effect == "delete"))
			{
				return toolMetaInfo.side_effect == "launch";
			}
			return true;
		}
		return false;
	}

	private static object _audit_before(string toolName, PyDict args)
	{
		if (!_audit_worthy(toolName))
		{
			return null;
		}
		PyDict obj = PyDict.Of("ts", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"), "tool", toolName, "args_digest", Py.Slice(PyJson.Dumps(args ?? new PyDict(), null, ensureAscii: false), null, 500));
		lock (_LOCK)
		{
			try
			{
				File.AppendAllText(Utils.AbsPath(PPath.Join(_audit_dir(), "audit.jsonl")), PyJson.Dumps(obj, null, ensureAscii: false) + "\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			}
			catch (Exception)
			{
			}
		}
		return null;
	}
}
