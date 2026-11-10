namespace HelL.Core.Launcher;

public class ModpackError : PyException
{
	public ModpackError(string m)
		: base("ModpackError", m)
	{
	}
}
