namespace HelL.Core;

public class NotImplementedError : RuntimeError
{
	public NotImplementedError(string m)
		: base("NotImplementedError", m, null)
	{
	}
}
