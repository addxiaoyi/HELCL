using System;

namespace HelL.Core.Launcher.Ai;

public sealed class HttpCancel
{
	public volatile bool flag;

	private AiResponse _resp;

	private AiSession _sess;

	public void Bind(AiSession session = null, AiResponse resp = null)
	{
		if (session != null)
		{
			_sess = session;
		}
		if (resp != null)
		{
			_resp = resp;
		}
		if (flag)
		{
			Abort();
		}
	}

	public void Abort()
	{
		flag = true;
		try
		{
			_resp?.Dispose();
		}
		catch (Exception)
		{
		}
		try
		{
			_sess?.Close();
		}
		catch (Exception)
		{
		}
	}

	public bool Cancelled()
	{
		return flag;
	}
}
