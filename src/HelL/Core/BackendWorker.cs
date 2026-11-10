using System;
using System.Threading;
using HelL.Core.Launcher;

namespace HelL.Core;

public sealed class BackendWorker
{
	[ThreadStatic]
	private static BackendWorker _current;

	public readonly string task_id;

	private readonly Func<TaskContext, object> _target;

	private readonly Action<string, PyDict> _emit;

	private volatile bool _cancelled;

	private readonly CancellationTokenSource _cts = new CancellationTokenSource();

	private readonly Thread _thread;

	public static BackendWorker Current => _current;

	public TaskContext Context { get; }

	public bool IsCancelled => _cancelled;

	public CancellationToken Token => _cts.Token;

	public bool IsAlive => _thread.IsAlive;

	public BackendWorker(string task_id, Func<TaskContext, object> target, Action<string, PyDict> emit)
	{
		this.task_id = task_id;
		_target = target;
		_emit = emit ?? ((Action<string, PyDict>)delegate
		{
		});
		Context = new TaskContext(this);
		_thread = new Thread(Run)
		{
			IsBackground = true,
			Name = task_id
		};
	}

	public void Start()
	{
		_thread.Start();
	}

	public bool Join(double seconds)
	{
		return _thread.Join(TimeSpan.FromSeconds(Math.Max(0.0, seconds)));
	}

	public void cancel()
	{
		_cancelled = true;
		try
		{
			_cts.Cancel();
		}
		catch (Exception)
		{
		}
	}

	internal void _progress(object current, object total, object message)
	{
		if (_cancelled)
		{
			throw new TaskCancelled();
		}
		_emit("progress", PyDict.Of("task_id", task_id, "current", Py.Int(Py.Or(current, 0L)), "total", Py.Int(Py.Or(total, 0L)), "message", Py.S(message)));
	}

	internal void _log(object text)
	{
		_emit("log", PyDict.Of("task_id", task_id, "text", Py.Str(text)));
	}

	public void login_code(object code, object uri)
	{
		_emit("login_code", PyDict.Of("code", Py.Str(code), "uri", Py.Str(uri)));
	}

	public void login_status(object text)
	{
		_emit("login_status", PyDict.Of("text", Py.Str(text)));
	}

	private void Run()
	{
		_current = this;
		ICrashReportException ex3 = null;
		Exception ex6 = default(Exception);
		try
		{
			if (_target == null)
			{
				throw Py.NotCallable(null);
			}
			string text = ((_target(Context) is string { Length: >0 } text2) ? text2 : I18nCore.Tr("任务完成"));
			_emit("finished", PyDict.Of("task_id", task_id, "success", true, "message", text));
		}
		catch (TaskCancelled)
		{
			_emit("finished", PyDict.Of("task_id", task_id, "success", false, "message", "已取消"));
		}
		catch (OperationCanceledException) when (_cancelled)
		{
			_emit("finished", PyDict.Of("task_id", task_id, "success", false, "message", "已取消"));
		}
		catch (Exception ex5) when (((Func<bool>)delegate
		{
			// Could not convert BlockContainer to single expression
			ex6 = ex5;
			return ((Func<bool>)delegate
			{
				ex3 = ex6 as ICrashReportException;
				return ex3 != null;
			})();
		}).Invoke())
		{
			string text3 = Py.ExcStr(ex6);
			_log("[错误] " + text3);
			PyDict pyDict = ((ex3.Report != null) ? new PyDict(ex3.Report) : new PyDict());
			pyDict["task_id"] = task_id;
			_emit("crash", pyDict);
			_emit("finished", PyDict.Of("task_id", task_id, "success", false, "message", text3, "crash", true));
		}
		catch (Exception e)
		{
			string text4 = Py.ExcStr(e);
			try
			{
				_log("[错误] " + text4);
			}
			catch (Exception)
			{
			}
			_emit("finished", PyDict.Of("task_id", task_id, "success", false, "message", text4));
		}
		finally
		{
			_current = null;
		}
	}
}
