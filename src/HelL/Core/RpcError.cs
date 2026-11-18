using System;

namespace HelL.Core;

public sealed class RpcError : Exception
{
	public long Code { get; }

	public RpcError(long code, string message)
		: base(message)
	{
		Code = code;
	}
}
