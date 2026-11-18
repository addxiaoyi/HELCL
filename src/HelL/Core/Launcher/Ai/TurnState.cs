using System;
using System.Collections.Generic;

namespace HelL.Core.Launcher.Ai;

public sealed class TurnState
{
	public string id;

	public string session_id;

	public long turn_number;

	public string phase = "idle";

	public readonly List<ToolCall> tool_calls = new List<ToolCall>();

	public readonly List<object> tool_results = new List<object>();

	public List<object> parallel_groups = new List<object>();

	public long rounds_used;

	private readonly object _lock = new object();

	public TurnState(string id, string sessionId, long turnNumber)
	{
		this.id = id;
		session_id = sessionId;
		turn_number = turnNumber;
	}

	public void Transition(string to)
	{
		if (Array.IndexOf<string>(TurnPhase.ALLOWED[phase], to) < 0)
		{
			throw new IllegalTransition(phase + " → " + to);
		}
		phase = to;
	}

	public ToolCall AddToolCall(ToolCall tc)
	{
		tool_calls.Add(tc);
		return tc;
	}

	public ToolCall SetToolStatus(string tcId, string status, string result = "", string error = "")
	{
		lock (_lock)
		{
			ToolCall toolCall = Find(tcId);
			if (toolCall == null)
			{
				return null;
			}
			toolCall.Mark(status, result, error);
			switch (status)
			{
			case "completed":
			case "failed":
			case "denied":
				tool_results.Add(PyDict.Of("tool_call_id", toolCall.id, "name", toolCall.name, "status", status, "result", toolCall.result, "error", toolCall.error, "duration", (toolCall.started_at != 0.0) ? ((object)Py.Round(toolCall.ended_at - toolCall.started_at, 3)) : ((object)0L)));
				break;
			}
			return toolCall;
		}
	}

	public ToolCall Find(string tcId)
	{
		foreach (ToolCall tool_call in tool_calls)
		{
			if (tool_call.id == tcId)
			{
				return tool_call;
			}
		}
		return null;
	}
}
