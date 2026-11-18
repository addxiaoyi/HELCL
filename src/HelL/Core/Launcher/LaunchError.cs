namespace HelL.Core.Launcher;

public class LaunchError : PyException
{
	public LaunchError(string m)
		: base("LaunchError", m)
	{
	}
}
