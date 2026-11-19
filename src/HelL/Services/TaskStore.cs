using System;
using System.Collections.Generic;
using System.Linq;

namespace HelL.Services;

public static class TaskStore
{
	private static readonly List<TaskRow> _rows = new List<TaskRow>();

	public static string PlaceholderTitle => I18n.L("任务");

	public static IReadOnlyList<TaskRow> Rows => _rows;

	public static int RunningCount => _rows.Count((TaskRow r) => !r.Finished);

	public static TaskRow? Newest => _rows.LastOrDefault((TaskRow r) => !r.Finished) ?? _rows.LastOrDefault();

	public static event Action<TaskRow>? Added;

	public static event Action<TaskRow>? Updated;

	public static event Action? Cleared;

	private const int MaxRows = 60;

	/// <summary>超过上限时优先淘汰已完成的旧任务，进行中的一条都不丢。</summary>
	private static void Trim()
	{
		int num = _rows.Count - MaxRows;
		int num2 = 0;
		while (num > 0 && num2 < _rows.Count)
		{
			if (_rows[num2].Finished)
			{
				_rows.RemoveAt(num2);
				num--;
				continue;
			}
			num2++;
		}
		if (num > 0)
		{
			_rows.RemoveRange(0, num);
		}
	}
	public static TaskRow? Get(string id)
	{
		return _rows.FirstOrDefault((TaskRow r) => r.Id == id);
	}
	public static void Notify(string id) { var row = Get(id); if (row != null) Updated?.Invoke(row); }

	public static void Handle(BridgeEvent ev)
	{
		switch (ev.Event)
		{
		case "crash":
			var crashed = Get(ev.TaskId);
			if (crashed != null)
			{
				crashed.HasCrash = true;
				Updated?.Invoke(crashed);
			}
			break;
		case "task_added":
			if (!string.IsNullOrEmpty(ev.TaskId) && Get(ev.TaskId) == null)
			{
				TaskRow taskRow2 = new TaskRow
				{
					Id = ev.TaskId,
					Title = (string.IsNullOrEmpty(ev.Title) ? PlaceholderTitle : ev.Title)
				};
				_rows.Add(taskRow2);
				Trim();
				TaskStore.Added?.Invoke(taskRow2);
			}
			break;
		case "progress":
		{
			TaskRow taskRow4 = Get(ev.TaskId);
			if (taskRow4 != null)
			{
				Fmt.SplitMsg(ev.Message, out string status, out string speed);
				taskRow4.Status = (string.IsNullOrEmpty(status) ? taskRow4.Status : status);
				taskRow4.Speed = speed;
				taskRow4.Indeterminate = ev.Total <= 0;
				if (ev.Total > 0)
				{
					taskRow4.Progress = Clamp.Of((double)ev.Current * 100.0 / (double)ev.Total, 0.0, 100.0);
				}
				TaskStore.Updated?.Invoke(taskRow4);
			}
			break;
		}
		case "log":
		{
			TaskRow taskRow3 = Get(ev.TaskId);
			if (taskRow3 != null)
			{
				taskRow3.Append(ev.Text);
				TaskStore.Updated?.Invoke(taskRow3);
			}
			break;
		}
		case "finished":
		{
			TaskRow taskRow = Get(ev.TaskId);
			if (taskRow != null)
			{
				taskRow.Finished = true;
				taskRow.Success = ev.Success;
				taskRow.Indeterminate = false;
				taskRow.Progress = (ev.Success ? 100.0 : taskRow.Progress);
				taskRow.Status = ((!string.IsNullOrEmpty(ev.Message)) ? ev.Message : (ev.Success ? I18n.L("已完成") : I18n.L("失败")));
				taskRow.Speed = "";
				TaskStore.Updated?.Invoke(taskRow);
			}
			break;
		}
		}
	}

	public static void ClearFinished()
	{
		foreach (var row in _rows.Where(row => row.Finished)) DownloadRetries.Remove(row.Id);
		_rows.RemoveAll((TaskRow r) => r.Finished);
		TaskStore.Cleared?.Invoke();
	}

	public static bool Remove(string id)
	{
		TaskRow taskRow = Get(id);
		if (taskRow == null || !taskRow.Finished)
		{
			return false;
		}
		_rows.Remove(taskRow);
		DownloadRetries.Remove(id);
		TaskStore.Cleared?.Invoke();
		return true;
	}

	public static bool Reconcile(IReadOnlyList<(string Id, string Title)> running, IReadOnlyList<(string Id, bool Success, string Message)> finished)
	{
		bool flag = false;
		foreach (var (text, text2) in running)
		{
			if (!string.IsNullOrEmpty(text) && Get(text) == null)
			{
				TaskRow taskRow = new TaskRow
				{
					Id = text,
					Title = (string.IsNullOrEmpty(text2) ? PlaceholderTitle : text2)
				};
				_rows.Add(taskRow);
				TaskStore.Added?.Invoke(taskRow);
				flag = true;
			}
		}
		foreach (var (text3, flag2, text4) in finished)
		{
			if (!string.IsNullOrEmpty(text3))
			{
				TaskRow taskRow2 = Get(text3);
				if (taskRow2 == null)
				{
					taskRow2 = new TaskRow
					{
						Id = text3,
						Title = PlaceholderTitle
					};
					_rows.Add(taskRow2);
					TaskStore.Added?.Invoke(taskRow2);
				}
				else if (taskRow2.Finished && taskRow2.Success == flag2)
				{
					continue;
				}
				taskRow2.Finished = true;
				taskRow2.Success = flag2;
				taskRow2.Indeterminate = false;
				taskRow2.Progress = (flag2 ? 100.0 : taskRow2.Progress);
				taskRow2.Status = ((!string.IsNullOrEmpty(text4)) ? text4 : (flag2 ? I18n.L("已完成") : I18n.L("失败")));
				taskRow2.Speed = "";
				TaskStore.Updated?.Invoke(taskRow2);
				flag = true;
			}
		}
		Trim();
		if (flag)
		{
			TaskStore.Cleared?.Invoke();
		}
		return flag;
	}
}
