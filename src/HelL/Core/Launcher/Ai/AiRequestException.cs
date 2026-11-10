using System;

namespace HelL.Core.Launcher.Ai;

public class AiRequestException : Exception
{
	public AiRequestException(string m, Exception inner = null)
		: base(m, inner)
	{
	}
}
