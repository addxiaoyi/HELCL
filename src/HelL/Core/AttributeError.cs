namespace HelL.Core;

public class AttributeError : PyException
{
	public AttributeError(string m)
		: base("AttributeError", m)
	{
	}
}
