namespace HelL.Core.Launcher;

public class AuthError : PyException
{
	public AuthError(string m)
		: base("AuthError", m)
	{
	}
}
