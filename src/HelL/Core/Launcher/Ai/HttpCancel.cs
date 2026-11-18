using System;

namespace HelL.Core.Launcher.Ai;

public sealed class HttpCancel : IDisposable
{
	private readonly object _guard = new object();
	private bool _disposed;
	public volatile bool flag;

	private AiResponse _resp;

	private AiSession _sess;

	public void Bind(AiSession session = null, AiResponse resp = null)
	{
		bool dispose;
		lock (_guard)
		{
			dispose = _disposed;
			if (!dispose)
			{
				if (session != null) _sess = session;
				if (resp != null) _resp = resp;
			}
		}
		if (dispose)
		{
			Release(resp, session);
			return;
		}
		if (flag) Abort();
	}

	public void Abort()
	{
		flag = true;
		AiResponse response;
		AiSession session;
		lock (_guard) { response = _resp; session = _sess; }
		Release(response, session);
	}

	public void Dispose()
	{
		AiResponse response;
		AiSession session;
		lock (_guard)
		{
			if (_disposed) return;
			_disposed = true;
			flag = true;
			response = _resp;
			session = _sess;
			_resp = null;
			_sess = null;
		}
		Release(response, session);
	}

	private static void Release(AiResponse response, AiSession session)
	{
		// Stop the connection before disposing its body, which otherwise waits for the HTTP drain timeout.
		try
		{
			session?.Dispose();
		}
		catch (Exception)
		{
		}
		try
		{
			response?.Dispose();
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
