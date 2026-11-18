using System;

namespace HelL.Core;

public class RuntimeError : PyException
{
	public RuntimeError(string m, Exception inner = null)
		: base("RuntimeError", m, inner)
	{
	}

	protected RuntimeError(string t, string m, Exception inner)
		: base(t, m, inner)
	{
	}
}
