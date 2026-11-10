namespace HelL.Core.Launcher;

public class HTTPError : PyException
{
	public long StatusCode { get; }

	public HTTPError(string m, long code)
		: base("HTTPError", m)
	{
		StatusCode = code;
	}
}
