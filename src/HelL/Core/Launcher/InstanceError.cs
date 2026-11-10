namespace HelL.Core.Launcher;

public class InstanceError : PyException
{
	public InstanceError(string m)
		: base("InstanceError", m)
	{
	}
}
