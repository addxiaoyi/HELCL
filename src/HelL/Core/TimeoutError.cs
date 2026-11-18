namespace HelL.Core;

public class TimeoutError : OSError
{
	public TimeoutError(string m)
		: base("TimeoutError", m, null)
	{
	}
}
