namespace HelL.Core;

public class PermissionError : OSError
{
	public PermissionError(string m)
		: base("PermissionError", m, null)
	{
	}
}
