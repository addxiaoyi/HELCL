using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace HelL.Core.Launcher.Ai;

public static class Trace
{
	public static string TRACE_DIR = null;

	public const int KEEP_DAYS = 14;

	private static readonly object _LOCK = new object();

	private static bool _pruned;

	private static string _dir()
	{
		string obj = TRACE_DIR ?? PPath.Join(Utils.ROOT, "cache", "ai_trace");
		Utils.EnsureDir(obj);
		return obj;
	}

	private static void _prune_once()
	{
		if (_pruned)
		{
			return;
		}
		_pruned = true;
		try
		{
			DateTime dateTime = DateTime.UtcNow.AddSeconds(-1209600.0);
			foreach (string item in Directory.EnumerateFiles(Utils.AbsPath(_dir()), "*.jsonl"))
			{
				try
				{
					if (File.GetLastWriteTimeUtc(item) < dateTime)
					{
						File.Delete(item);
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

	public static string ExcType(Exception e)
	{
		if (!(e is PyException ex))
		{
			if (!(e is TargetInvocationException ex2))
			{
				if (e is FileNotFoundException || e is DirectoryNotFoundException)
				{
					return "FileNotFoundError";
				}
				if (e is UnauthorizedAccessException)
				{
					return "PermissionError";
				}
				if (e is IOException)
				{
					return "OSError";
				}
				if (e is NotImplementedException)
				{
					return "NotImplementedError";
				}
				if (e is OperationCanceledException)
				{
					return "CancelledError";
				}
				if (e is ArgumentException || e is FormatException)
				{
					return "ValueError";
				}
				if (e is InvalidCastException || e is NullReferenceException)
				{
					return "TypeError";
				}
			}
			else if (ex2.InnerException != null)
			{
				return ExcType(ex2.InnerException);
			}
			return e.GetType().Name;
		}
		return ex.PyType;
	}

	public static void Record(string ev, object round_ = null, string phase = "", string tool_name = "", Exception exc = null, bool? stream_failed = null, long? text_len = null, params object[] extra)
	{
		try
		{
			PyDict pyDict = PyDict.Of("ts", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fff"), "event", ev ?? "", "round", (round_ is int num) ? ((object)(long)num) : round_, "phase", phase ?? "", "tool_name", tool_name ?? "");
			if (exc != null)
			{
				pyDict["exc_type"] = ExcType(exc);
				pyDict["exc_msg"] = Py.Slice(Py.ExcStr(exc), null, 800);
			}
			if (stream_failed.HasValue)
			{
				pyDict["stream_failed"] = stream_failed.Value;
			}
			if (text_len.HasValue)
			{
				pyDict["text_len"] = text_len.Value;
			}
			if (extra != null)
			{
				for (int i = 0; i + 1 < extra.Length; i += 2)
				{
					pyDict[(string)extra[i]] = Py.Box(extra[i + 1]);
				}
			}
			string path = Utils.AbsPath(PPath.Join(_dir(), DateTime.Now.ToString("yyyyMMdd") + ".jsonl"));
			string text = PyJson.Dumps(pyDict, null, ensureAscii: false);
			lock (_LOCK)
			{
				_prune_once();
				File.AppendAllText(path, text + "\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			}
		}
		catch (Exception)
		{
		}
	}
}
