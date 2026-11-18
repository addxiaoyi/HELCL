namespace HelL.Core.Launcher.Ai;

public class IllegalTransition : PyException
{
	public IllegalTransition(string m)
		: base("IllegalTransition", m)
	{
	}
}
