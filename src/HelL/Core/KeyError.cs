namespace HelL.Core;

public class KeyError : PyException
{
	public object Key { get; }

	public KeyError(object key)
		: base("KeyError", Py.Repr(key))
	{
		Key = key;
	}
}
