using System;

namespace HelL.Core.Launcher;

public class NetError : PyException
{
	public NetError(string pyType, string m, Exception inner = null)
		: base(pyType, m, inner)
	{
	}
}
