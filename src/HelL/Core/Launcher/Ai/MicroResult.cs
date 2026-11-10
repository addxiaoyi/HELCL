using System.Collections.Generic;

namespace HelL.Core.Launcher.Ai;

public sealed class MicroResult
{
	public List<object> messages = new List<object>();

	public string decision_reason = "";

	public long cleared_count;

	public long tokens_saved;

	public bool changed;
}
