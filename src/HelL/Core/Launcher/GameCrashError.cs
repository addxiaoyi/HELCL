namespace HelL.Core.Launcher;

public class GameCrashError : PyException, ICrashReportException
{
	public PyDict Report { get; }

	public GameCrashError(PyDict report)
		: base("GameCrashError", Py.Str(Py.Or((report ?? new PyDict()).Get("summary"), "游戏崩溃")))
	{
		Report = report ?? new PyDict();
	}
}
