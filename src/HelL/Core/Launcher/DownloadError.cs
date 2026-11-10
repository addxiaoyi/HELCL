using System;

namespace HelL.Core.Launcher;

public class DownloadError : PyException
{
	public DownloadError(string m, Exception inner = null)
		: base("DownloadError", m, inner)
	{
	}
}
