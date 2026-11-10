using System;

namespace HelL.Core.Launcher.Ai;

public class AiStreamBroken : Exception
{
	public AiStreamBroken(string m, Exception inner = null)
		: base(m, inner)
	{
	}
}
