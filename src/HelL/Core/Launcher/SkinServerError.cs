namespace HelL.Core.Launcher;

public class SkinServerError : RuntimeError
{
	public SkinServerError(string m)
		: base("SkinServerError", m, null)
	{
	}
}
