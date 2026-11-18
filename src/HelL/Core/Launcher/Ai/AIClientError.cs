using System;

namespace HelL.Core.Launcher.Ai;

public class AIClientError : PyException
{
	public int Status { get; }

	public string Category { get; }

	public AIClientError(string message, int status = 0, Exception inner = null)
		: base("AIClientError", message, inner)
	{
		Status = status;
		Category = Client.Categorize(message, Status);
	}

	public bool Fatal()
	{
		if (Status != 401 && Status != 403)
		{
			return Category == "auth";
		}
		return true;
	}

	public bool Retryable()
	{
		if (Status == 429 || Status == 500 || Status == 502 || Status == 503 || Status == 504)
		{
			return true;
		}
		if (!(Category == "provider_timeout") && !(Category == "provider_network_error") && !(Category == "provider_stream_error"))
		{
			return Category == "rate_limited";
		}
		return true;
	}
}
