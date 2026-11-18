namespace HelL.Core.Launcher;

public class ReError : PyException
{
	public ReError(string m)
		: base("error", m)
	{
	}
}
