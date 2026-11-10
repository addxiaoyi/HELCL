namespace HelL.Core.Launcher.Ai;

public static class Builtin
{
	private const string _UNAVAILABLE = "unavailable-token-removed-in-build";

	private const string _M = "deepseek-v4-flash";

	public static string PublicBase()
	{
		return "";
	}

	public static string PublicToken()
	{
		return "unavailable-token-removed-in-build";
	}

	public static string PublicModel()
	{
		return "deepseek-v4-flash";
	}

	public static PyDict PublicEndpoint()
	{
		return PyDict.Of("base", "", "token", "unavailable-token-removed-in-build", "model", "deepseek-v4-flash");
	}
}
