using System.Text;

namespace HelL.Core.Launcher;

public sealed class AuthResponse
{
	public int StatusCode;

	public double? RetryAfterSeconds;

	public byte[] Content = new byte[0];

	public string Text => Encoding.UTF8.GetString(Content ?? new byte[0]);

	public string Text200 => Py.Slice(Text, 0, 200);

	public object Json()
	{
		string text = Text;
		if (text.Length > 0 && text[0] == '\ufeff')
		{
			text = text.Substring(1);
		}
		return PyJson.Loads(text);
	}
}
