namespace HelL.Core.Launcher;

public class FileExistsError : OSError
{
	public FileExistsError(string m)
		: base("FileExistsError", m, null)
	{
	}
}
