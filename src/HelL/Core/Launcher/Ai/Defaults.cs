using System;
using System.Text;

namespace HelL.Core.Launcher.Ai;

public static class Defaults
{
	private const int _GW_MASK = 90;

	private static readonly byte[] _GW_BYTES = new byte[19]
	{
		50, 46, 46, 42, 41, 96, 117, 117, 52, 63,
		45, 116, 105, 43, 116, 50, 59, 51, 40
	};





	public static readonly string DEFAULT_GATEWAY_URL = DefaultGatewayUrl();

	public static readonly string DEFAULT_API_BASE = DefaultApiBase();

	public static readonly string DEFAULT_API_KEY = DefaultApiKey();

	public const string DEFAULT_MODEL = "deepseek-v4-flash";

	public static readonly string CLIENT_HEADER = AppInfo.CLIENT_HEADER;

	public const int MAX_TOOL_ROUNDS_MAIN = 20;

	public const int MAX_TOOL_ROUNDS_SUBAGENT = 4;

	public const int MAX_TOOL_ROUNDS = 20;

	public const int MAX_HISTORY = 24;

	public const int MAX_TOOL_RESULT = 8000;

	public const int STREAM_CONNECT_TIMEOUT = 15;

	public const int STREAM_READ_TIMEOUT = 90;

	public const int ONCE_TIMEOUT = 90;

	private static string Unmask(byte[] data, int mask)
	{
		byte[] array = new byte[data.Length];
		for (int i = 0; i < data.Length; i++)
		{
			array[i] = (byte)((data[i] ^ mask) & 0xFF);
		}
		return Encoding.UTF8.GetString(array);
	}

	public static string DefaultGatewayUrl()
	{
		try
		{
			return Py.RStrip(Py.Strip(Unmask(_GW_BYTES, 90)), "/");
		}
		catch (Exception)
		{
			return "";
		}
	}

	public static string DefaultApiBase()
	{
		string text = DefaultGatewayUrl();
		if (text.Length <= 0)
		{
			return "";
		}
		return text + "/v1";
	}

	public static string DefaultApiKey() => (Environment.GetEnvironmentVariable("TIPCL_AI_API_KEY") ?? "").Trim();
}
