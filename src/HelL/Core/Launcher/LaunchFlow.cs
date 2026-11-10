using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace HelL.Core.Launcher;

public static class LaunchFlow
{
	private static readonly HashSet<string> _CMD_BUILTINS = new HashSet<string>
	{
		"assoc", "break", "call", "cd", "chcp", "chdir", "cls", "color", "copy", "date",
		"del", "dir", "echo", "endlocal", "erase", "exit", "for", "ftype", "goto", "if",
		"md", "mkdir", "mklink", "move", "path", "pause", "popd", "prompt", "pushd", "rd",
		"rem", "ren", "rename", "rmdir", "set", "setlocal", "shift", "start", "time", "title",
		"type", "ver", "verify", "vol"
	};

	private const string _CMD_UNSAFE = "&|<>^()%!\r\n";

	public static PyDict Prepare(Instance instance, string version_id, IEnumerable<object> extra_game_args = null, object memory_mb = null)
	{
		PyDict pyDict = VersionSettings.Load(instance, version_id);
		string text = VersionSettings.ApplyIsolation(instance, version_id, pyDict);
		long num = GlobalMods.Apply(PPath.Join(text, "mods"));
		object obj = Py.Or(pyDict.Get("memory_mb"), memory_mb);
		List<object> list = new List<object>();
		foreach (object item in extra_game_args ?? new List<object>())
		{
			if (item != null && !(item is string { Length: 0 }))
			{
				list.Add(Py.Str(item));
			}
		}
		list.AddRange(ArgSplit.SplitArgs(pyDict.Get("game_args")));
		if (Py.Truthy(pyDict.Get("server")) && !list.Any((object x) => Py.Eq(x, "--server")))
		{
			list.Add("--server");
			list.Add(Py.Str(pyDict["server"]));
			list.Add("--port");
			list.Add(Py.Str(Py.Or(pyDict.Get("port"), 25565L)));
		}
		object obj2 = Py.Or(pyDict.Get("window_mode"), Config.CONFIG.Get("window_mode"), "window");
		if (obj2 is string value && VersionSettings.FULLSCREEN_MODES.Contains<string>(value) && !list.Any((object x) => Py.Eq(x, "--fullscreen")))
		{
			list.Add("--fullscreen");
		}
		string text3 = Gc.Apply(Py.Or(pyDict.Get("gc"), Config.CONFIG.Get("gc_preset"), "auto"), Py.Or(pyDict.Get("jvm_args"), ""));
		return PyDict.Of("settings", pyDict, "game_dir", text, "memory_mb", obj, "extra_game_args", list, "jvm_args", text3, "priority", Py.Or(pyDict.Get("process_priority"), Config.CONFIG.Get("default_priority"), "normal"), "global_mods", num, "pre_launch_wait", pyDict.Get("pre_launch_wait", true), "login_account", Py.Or(pyDict.Get("login_account"), ""), "nide8_id", Py.Or(pyDict.Get("nide8_id"), ""), "auth_server", Py.Or(pyDict.Get("auth_server"), ""), "window_mode", obj2, "window_width", _positive(pyDict.Get("window_width")), "window_height", _positive(pyDict.Get("window_height")), "window_title", Py.Strip(Py.AsStr(Py.Or(pyDict.Get("window_title"), ""), "strip")));
	}

	private static object _positive(object value)
	{
		long num;
		try
		{
			num = Py.Int(value);
		}
		catch (TypeError)
		{
			return null;
		}
		catch (ValueError)
		{
			return null;
		}
		if (num <= 0)
		{
			return null;
		}
		return num;
	}

	public static object[] ResolveResolution(PyDict prep, object width = null, object height = null)
	{
		object obj = Py.Or(prep.Get("window_width"), width);
		object obj2 = Py.Or(prep.Get("window_height"), height);
		if (Py.Or(prep.Get("window_mode"), "window") is string value && VersionSettings.FULLSCREEN_MODES.Contains<string>(value))
		{
			obj = Math.Max(Py.Int(Py.Or(obj, 0L)), 1280L);
			obj2 = Math.Max(Py.Int(Py.Or(obj2, 0L)), 720L);
		}
		return new object[2] { obj, obj2 };
	}

	private static List<string> ShlexNonPosix(string s)
	{
		List<string> list = new List<string>();
		StringBuilder stringBuilder = new StringBuilder();
		char c = ' ';
		int num = 0;
		while (true)
		{
			char? c2 = ((num < s.Length) ? new char?(s[num]) : ((char?)null));
			num++;
			switch (c)
			{
			case ' ':
			{
				if (!c2.HasValue)
				{
					break;
				}
				char value2 = c2.Value;
				if (" \t\r\n".IndexOf(value2) >= 0)
				{
					if (stringBuilder.Length > 0)
					{
						list.Add(stringBuilder.ToString());
						stringBuilder.Clear();
					}
				}
				else if (value2 == '\'' || value2 == '"')
				{
					stringBuilder.Append(value2);
					c = value2;
				}
				else
				{
					stringBuilder.Append(value2);
					c = 'a';
				}
				continue;
			}
			case '"':
			case '\'':
			{
				if (!c2.HasValue)
				{
					throw new ValueError("No closing quotation");
				}
				char value3 = c2.Value;
				stringBuilder.Append(value3);
				if (value3 == c)
				{
					list.Add(stringBuilder.ToString());
					stringBuilder.Clear();
					c = ' ';
				}
				continue;
			}
			default:
			{
				if (!c2.HasValue)
				{
					break;
				}
				char value = c2.Value;
				if (" \t\r\n".IndexOf(value) >= 0)
				{
					c = ' ';
					if (stringBuilder.Length > 0)
					{
						list.Add(stringBuilder.ToString());
						stringBuilder.Clear();
					}
				}
				else
				{
					stringBuilder.Append(value);
				}
				continue;
			}
			}
			break;
		}
		if (stringBuilder.Length > 0)
		{
			list.Add(stringBuilder.ToString());
		}
		return list;
	}

	private static List<string> _split_hook(string command)
	{
		if (!Utils.IS_WINDOWS)
		{
			return ArgSplit.SplitArgs(command);
		}
		List<string> list;
		try
		{
			list = ShlexNonPosix(command);
		}
		catch (ValueError)
		{
			return Py.SplitWs(command);
		}
		List<string> list2 = new List<string>();
		foreach (string item in list)
		{
			string text = item;
			if (text.Length >= 2 && text[0] == text[text.Length - 1] && (text[0] == '"' || text[0] == '\''))
			{
				text = text.Substring(1, text.Length - 2);
			}
			if (text.Length > 0)
			{
				list2.Add(text);
			}
		}
		return list2;
	}

	private static List<string> _cmd_builtin_argv(string command)
	{
		if (!Utils.IS_WINDOWS)
		{
			return null;
		}
		List<string> list = _split_hook(command);
		if (list.Count == 0 || !_CMD_BUILTINS.Contains(Py.Lower(list[0])))
		{
			return null;
		}
		if (command.IndexOfAny("&|<>^()%!\r\n".ToCharArray()) >= 0)
		{
			return null;
		}
		return new List<string> { "cmd", "/c", command };
	}

	private static string OsErrorText(Exception exc, string exe)
	{
		if (exc is Win32Exception ex)
		{
			if (ex.NativeErrorCode == 2)
			{
				return "[Errno 2] No such file or directory: " + Py.ReprStr(exe);
			}
			if (ex.NativeErrorCode == 13 || ex.NativeErrorCode == 5)
			{
				return "[Errno 13] Permission denied: " + Py.ReprStr(exe);
			}
		}
		return exc.Message;
	}

	public static long RunHook(string command, string cwd, Action<string> log = null, bool wait = true)
	{
		string text = Py.Strip(command ?? "");
		if (text.Length == 0)
		{
			return 0L;
		}
		List<string> list = _split_hook(text);
		if (list.Count == 0)
		{
			return 0L;
		}
		log?.Invoke("运行启动脚本: " + text);
		List<string> list2 = _cmd_builtin_argv(text);
		if (list2 == null && _CMD_BUILTINS.Contains(Py.Lower(list[0])))
		{
			log?.Invoke("启动脚本包含 shell 元字符，已拒绝执行（改用 .bat / .cmd 脚本）");
			return -1L;
		}
		if (list2 != null)
		{
			list = list2;
		}
		Process proc;
		try
		{
			ProcessStartInfo startInfo = Utils.MakeStartInfo(list, cwd);
			proc = Process.Start(startInfo);
		}
		catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException || ex is IOException)
		{
			log?.Invoke("启动脚本无法执行: " + OsErrorText(ex, list[0]));
			return -1L;
		}
		using (proc)
		{
			try
			{
				proc.StandardInput.Close();
			}
			catch (Exception)
			{
			}
			if (!wait)
			{
				Thread thread = new Thread((ThreadStart)delegate
				{
					try
					{
						proc.StandardOutput.ReadToEnd();
					}
					catch (Exception)
					{
					}
				});
				thread.IsBackground = true;
				thread.Start();
				Thread thread2 = new Thread((ThreadStart)delegate
				{
					try
					{
						proc.StandardError.ReadToEnd();
					}
					catch (Exception)
					{
					}
				});
				thread2.IsBackground = true;
				thread2.Start();
				return 0L;
			}
			string so = null;
			string se = null;
			Thread thread3 = new Thread((ThreadStart)delegate
			{
				try
				{
					so = proc.StandardOutput.ReadToEnd();
				}
				catch (Exception)
				{
					so = "";
				}
			})
			{
				IsBackground = true
			};
			Thread obj = new Thread((ThreadStart)delegate
			{
				try
				{
					se = proc.StandardError.ReadToEnd();
				}
				catch (Exception)
				{
					se = "";
				}
			})
			{
				IsBackground = true
			};
			thread3.Start();
			obj.Start();
			proc.WaitForExit();
			thread3.Join();
			obj.Join();
			long num = proc.ExitCode;
			if (log != null && !string.IsNullOrEmpty(so))
			{
				foreach (string item in Py.SplitLines(so).Take(40))
				{
					log(item);
				}
			}
			if (num != 0L)
			{
				log?.Invoke("脚本退出码 " + num + ": " + Py.Slice(se ?? "", null, 300));
			}
			return num;
		}
	}
}
