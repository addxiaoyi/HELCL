using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HelL.Core.Launcher.Ai;

public static class Scheduler
{
	private static readonly string[] EXCLUSIVE_SIDE_EFFECTS = new string[2] { "delete", "launch" };

	private static readonly string[] PARALLEL_SIDE_EFFECTS = new string[3] { "none", "read", "network" };

	public static bool IsExclusive(ToolCall tc)
	{
		if (tc.name == "ask_user")
		{
			return true;
		}
		ToolMetaInfo toolMetaInfo = ToolMeta.Get(tc.name);
		if (toolMetaInfo != null)
		{
			return Array.IndexOf<string>(EXCLUSIVE_SIDE_EFFECTS, toolMetaInfo.side_effect) >= 0;
		}
		return false;
	}

	public static bool IsParallelizable(ToolCall tc)
	{
		ToolMetaInfo toolMetaInfo = ToolMeta.Get(tc.name);
		if (toolMetaInfo == null)
		{
			return false;
		}
		if (toolMetaInfo.@readonly)
		{
			return Array.IndexOf<string>(PARALLEL_SIDE_EFFECTS, toolMetaInfo.side_effect) >= 0;
		}
		return false;
	}

	public static List<List<ToolCall>> Group(IEnumerable<ToolCall> toolCalls, int maxConcurrency = 4)
	{
		List<List<ToolCall>> groups = new List<List<ToolCall>>();
		List<ToolCall> current = new List<ToolCall>();
		foreach (ToolCall toolCall in toolCalls)
		{
			if (IsExclusive(toolCall))
			{
				Flush();
				groups.Add(new List<ToolCall> { toolCall });
			}
			else if (IsParallelizable(toolCall))
			{
				if (current.Count >= Math.Max(1, maxConcurrency))
				{
					Flush();
				}
				current.Add(toolCall);
			}
			else
			{
				Flush();
				groups.Add(new List<ToolCall> { toolCall });
			}
		}
		Flush();
		return groups;
		void Flush()
		{
			if (current.Count > 0)
			{
				groups.Add(current);
				current = new List<ToolCall>();
			}
		}
	}

	public static Dictionary<string, string> RunGroups(List<List<ToolCall>> groups, Func<ToolCall, string> execute, int maxConcurrency = 4)
	{
		Dictionary<string, string> dictionary = new Dictionary<string, string>();
		foreach (List<ToolCall> group in groups)
		{
			if (group.Count == 0)
			{
				continue;
			}
			if (group.Count == 1)
			{
				dictionary[group[0].id] = execute(group[0]);
				continue;
			}
			int initialCount = Math.Min(Math.Max(1, maxConcurrency), group.Count);
			List<Task<string>> list = new List<Task<string>>();
			SemaphoreSlim sem = new SemaphoreSlim(initialCount);
			try
			{
				foreach (ToolCall item in group)
				{
					ToolCall t = item;
					list.Add(Task.Run(delegate
					{
						sem.Wait();
						try
						{
							return execute(t);
						}
						finally
						{
							sem.Release();
						}
					}));
				}
				try
				{
					Task[] tasks = list.ToArray();
					Task.WaitAll(tasks);
				}
				catch (AggregateException)
				{
				}
			}
			finally
			{
				if (sem != null)
				{
					((IDisposable)sem).Dispose();
				}
			}
			for (int num = 0; num < group.Count; num++)
			{
				Task<string> task = list[num];
				if (task.IsFaulted)
				{
					throw task.Exception.InnerExceptions[0];
				}
				dictionary[group[num].id] = task.Result;
			}
		}
		return dictionary;
	}
}
