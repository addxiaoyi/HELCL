using System.Reflection;

namespace HelL.Core.Launcher;

public static class AppInfo
{
	public const string APP_NAME = "HelL";

	public const string APP_DISPLAY_NAME = "HelL 启动器";

	public static readonly string APP_VERSION = ReadVersion();

	public static readonly string CLIENT_HEADER = APP_NAME + "/" + APP_VERSION;

	public const string APP_ID = "tipcl";

	public const string LAUNCHER_NAME = "HelL";

	public static readonly string LAUNCHER_VERSION = APP_VERSION;

	public static readonly string __version__ = APP_VERSION;

	private static string ReadVersion()
	{
		Assembly assembly = typeof(AppInfo).Assembly;
		string version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
		return string.IsNullOrWhiteSpace(version)
			? assembly.GetName().Version.ToString(3)
			: version.Split('+')[0];
	}
}
