using System.Collections.Generic;

namespace HelL.Core.Launcher.Ai;

public class NoopMemory
{
	public virtual string Load(string chatId)
	{
		return "";
	}

	public virtual void Save(string chatId, string text)
	{
	}

	public virtual List<object> Search(string chatId, string query, int k = 3)
	{
		return new List<object>();
	}
}
