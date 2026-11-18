namespace HelL.Core.Launcher;

public class OverflowError : PyException
{
	public OverflowError(string m)
		: base("OverflowError", m)
	{
	}
}
