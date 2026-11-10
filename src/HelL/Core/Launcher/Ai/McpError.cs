namespace HelL.Core.Launcher.Ai;

public class McpError : PyException
{
	public McpError(string m)
		: base("McpError", m)
	{
	}
}
