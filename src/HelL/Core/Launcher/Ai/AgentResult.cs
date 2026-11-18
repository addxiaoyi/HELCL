using System.Collections.Generic;

namespace HelL.Core.Launcher.Ai;

public sealed class AgentResult
{
	public string Text;

	public string stop_reason;

	public string detail;

	public List<object> pending_tasks;

	public long rounds_used;

	public List<object> messages = new List<object>();

	public List<object> turn_messages = new List<object>();

	public List<object> plan = new List<object>();

	public PyDict usage = new PyDict();

	public string usage_source = "estimate";

	internal bool _summary_recorded;

	public AgentResult(string text, string stopReason = "completed", string detail = "", List<object> pendingTasks = null, long roundsUsed = 0L)
	{
		Text = text ?? "";
		stop_reason = stopReason;
		this.detail = detail ?? "";
		pending_tasks = ((pendingTasks != null) ? new List<object>(pendingTasks) : new List<object>());
		rounds_used = roundsUsed;
	}

	public override string ToString()
	{
		return Text;
	}
}
