using System;

namespace HelL.Core.Launcher.Ai;

public sealed class Hook
{
	public string name;

	public Func<string, PyDict, object> before;

	public Func<string, string, object> after;

	public Hook(string name, Func<string, PyDict, object> before = null, Func<string, string, object> after = null)
	{
		this.name = name;
		this.before = before;
		this.after = after;
	}
}
