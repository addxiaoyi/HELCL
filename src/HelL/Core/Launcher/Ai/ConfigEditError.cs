namespace HelL.Core.Launcher.Ai;

public class ConfigEditError : PyException
{
	public ConfigEditError(string m)
		: base("ConfigEditError", m)
	{
	}
}
