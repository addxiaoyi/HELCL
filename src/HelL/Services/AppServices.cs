namespace HelL.Services;

public static class AppServices
{
	public static BridgeClient Client { get; set; }

	public static BridgeHost? Host { get; set; }

	public static MainWindow? Window { get; set; }

	public static bool Ready => Client != null;

	public static void Toast(string title, string body = "", ToastKind kind = ToastKind.Info)
	{
		Window?.Toast(title, body, kind);
	}
}
