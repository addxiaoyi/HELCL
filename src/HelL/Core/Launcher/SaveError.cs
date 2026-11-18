namespace HelL.Core.Launcher;

public class SaveError : PyException
{
	public SaveError(string m)
		: base("SaveError", m)
	{
	}
}
