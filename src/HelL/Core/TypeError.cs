namespace HelL.Core;

public class TypeError : PyException
{
	public TypeError(string m)
		: base("TypeError", m)
	{
	}
}
