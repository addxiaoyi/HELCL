namespace HelL.Core;

public class FileNotFoundError : OSError
{
	public FileNotFoundError(string m)
		: base("FileNotFoundError", m, null)
	{
	}

	public static FileNotFoundError ForPath(string p)
	{
		return new FileNotFoundError("[Errno 2] No such file or directory: " + Py.Repr(p));
	}
}
