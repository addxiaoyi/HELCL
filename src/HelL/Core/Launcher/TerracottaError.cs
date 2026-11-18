namespace HelL.Core.Launcher;

public class TerracottaError : PyException
{
	public TerracottaError(string m)
		: base("TerracottaError", m)
	{
	}
}
