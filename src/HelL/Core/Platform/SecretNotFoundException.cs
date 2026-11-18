namespace HelL.Core.Platform;

public class SecretNotFoundException : PyException
{
	public SecretNotFoundException(string m = "Password not found")
		: base("PasswordDeleteError", m)
	{
	}
}
