namespace HelL.Core.Launcher;

public class WorldError : PyException
{
	public WorldError(string m)
		: base("WorldError", m)
	{
	}
}
