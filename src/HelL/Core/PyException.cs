using System;

namespace HelL.Core;

public class PyException : Exception
{
	public string PyType { get; }

	public PyException(string pyType, string message, Exception inner = null)
		: base(message ?? "", inner)
	{
		PyType = pyType;
	}

	public override string ToString()
	{
		return PyType + ": " + Message;
	}
}
