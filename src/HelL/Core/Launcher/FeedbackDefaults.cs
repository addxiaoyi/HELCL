namespace HelL.Core.Launcher;

public static class FeedbackDefaults
{
	public const string DEFAULT_FEEDBACK_URL = "http://114.66.28.184:53611";

	public static readonly string CLIENT_HEADER = AppInfo.CLIENT_HEADER;

	public const int HEARTBEAT_SEC = 30;

	public const int COLLECT_CACHE_SEC = 120;

	public static readonly (string key, string label)[] CATEGORIES = new(string, string)[8]
	{
		("bug", "功能异常"),
		("crash", "崩溃闪退"),
		("download", "下载问题"),
		("multiplayer", "联机"),
		("ai", "AI 助手"),
		("ui", "界面体验"),
		("suggest", "建议"),
		("other", "其他")
	};
}
