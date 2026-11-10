using System;

namespace HelL.Core;

public class ValueError : PyException
{
	public ValueError(string m, Exception inner = null)
		: base("ValueError", m, inner)
	{
	}

	protected ValueError(string t, string m, Exception inner)
		: base(t, m, inner)
	{
	}
}
