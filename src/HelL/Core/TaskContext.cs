using System;
using System.Threading;

namespace HelL.Core;

public sealed class TaskContext
{
	private readonly BackendWorker _w;

	public string TaskId => _w.task_id;

	public bool Cancelled => _w.IsCancelled;

	public CancellationToken Token => _w.Token;

	public Action<object, object, object> ProgressFn => delegate(object a, object b, object c)
	{
		_w._progress(a, b, c);
	};

	public Action<object> LogFn => delegate(object t)
	{
		_w._log(t);
	};

	public static TaskContext Current => BackendWorker.Current?.Context;

	internal TaskContext(BackendWorker w)
	{
		_w = w;
	}

	public void Progress(object current, object total, object message = null)
	{
		_w._progress(current, total, message);
	}

	public void Progress(long current, long total, string message = "")
	{
		_w._progress(current, total, message);
	}

	public void Log(object text)
	{
		_w._log(text);
	}

	public void LoginCode(object code, object uri)
	{
		_w.login_code(code, uri);
	}

	public void LoginStatus(object text)
	{
		_w.login_status(text);
	}
}
