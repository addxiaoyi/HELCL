using System;
using System.IO;
using System.Text;
using System.Threading;

namespace HelL.Core.Launcher;

public static class Guard
{
	private static bool _installed;

	private static string _log_path;

	private static Action<string, string, string> _ui_hook;

	private static readonly object _writeLock = new object();

	public static string LogPath()
	{
		return _log_path ?? PPath.Join(Utils.ROOT, "tipcl-error.log");
	}

	public static string WriteLog(string kind, string text)
	{
		string text2 = LogPath();
		string contents = new string('=', 64) + "\n" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + kind + "]\n" + Py.RStrip(text ?? "") + "\n";
		try
		{
			string path = Utils.AbsPath(text2);
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			lock (_writeLock)
			{
				File.AppendAllText(path, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			}
		}
		catch (Exception)
		{
		}
		return text2;
	}

	private static void Notify(string kind, string text)
	{
		string arg = WriteLog(kind, text);
		try
		{
			Console.Error.Write(text.EndsWith("\n") ? text : (text + "\n"));
		}
		catch (Exception)
		{
		}
		Action<string, string, string> ui_hook = _ui_hook;
		if (ui_hook == null)
		{
			return;
		}
		try
		{
			ui_hook(kind, text, arg);
		}
		catch (Exception)
		{
		}
	}

	public static string Install(string path = null, Action<string, string, string> ui_hook = null)
	{
		_log_path = ((path != null) ? new PPath(path).ToString() : PPath.Join(Utils.ROOT, "tipcl-error.log"));
		if (ui_hook != null)
		{
			_ui_hook = ui_hook;
		}
		if (_installed)
		{
			return _log_path;
		}
		AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
		{
			Exception obj = e.ExceptionObject as Exception;
			string name = Thread.CurrentThread.Name;
			string text = obj?.ToString() ?? Convert.ToString(e.ExceptionObject);
			if (!string.IsNullOrEmpty(name))
			{
				Notify("thread", "thread=" + name + "\n" + text);
			}
			else
			{
				Notify("unhandled", text);
			}
		};
		_installed = true;
		WriteLog("guard", "global hooks installed");
		return _log_path;
	}
}
