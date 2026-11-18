namespace HelL.Core;

public class JSONDecodeError : ValueError
{
	public JSONDecodeError(string msg, string doc, int pos)
		: base("JSONDecodeError", Format(msg, doc, pos), null)
	{
	}

	private static string Format(string msg, string doc, int pos)
	{
		int num = 1;
		int num2 = -1;
		for (int i = 0; i < pos && i < doc.Length; i++)
		{
			if (doc[i] == '\n')
			{
				num++;
				num2 = i;
			}
		}
		int num3 = pos - num2;
		return $"{msg}: line {num} column {num3} (char {pos})";
	}
}
