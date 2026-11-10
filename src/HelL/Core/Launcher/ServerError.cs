namespace HelL.Core.Launcher;

public class ServerError : PyException
{
	public ServerError(string m)
		: base("ServerError", m)
	{
	}
}
