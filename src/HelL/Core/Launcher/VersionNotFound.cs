namespace HelL.Core.Launcher;

public class VersionNotFound : PyException
{
	public VersionNotFound(string m)
		: base("VersionNotFound", m)
	{
	}
}
