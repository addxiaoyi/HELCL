namespace HelL.Core.Launcher;

public class PathEscapeError : ValueError
{
	public PathEscapeError(string m)
		: base("PathEscapeError", m, null)
	{
	}
}
