using System;

namespace HelL.Core;

public class OSError : PyException
{
	public OSError(string m, Exception inner = null)
		: base("OSError", m, inner)
	{
	}

	protected OSError(string t, string m, Exception inner)
		: base(t, m, inner)
	{
	}
}
