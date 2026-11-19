using System;

namespace HelL.Services;

public sealed class BridgeCallException : Exception
{
	public string Method { get; }

	public BridgeCallException(string method, string message, Exception? innerException = null)
		: base(message, innerException)
	{
		Method = method;
	}
}
