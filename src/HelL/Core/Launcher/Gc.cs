using System.Linq;

namespace HelL.Core.Launcher;

public static class Gc
{
	public static readonly PyDict LABELS = PyDict.Of("auto", "G1（推荐）", "g1", "G1", "g1_tuned", "调优 G1", "zgc", "ZGC", "none", "不指定");

	public static readonly PyDict ARGS = PyDict.Of("auto", "-XX:+UseG1GC -XX:+UnlockExperimentalVMOptions -XX:G1NewSizePercent=20 -XX:G1ReservePercent=20 -XX:MaxGCPauseMillis=50 -XX:G1HeapRegionSize=32M", "g1", "-XX:+UseG1GC", "g1_tuned", "-XX:+UseG1GC -XX:+UnlockExperimentalVMOptions -XX:G1NewSizePercent=20 -XX:G1ReservePercent=20 -XX:MaxGCPauseMillis=50 -XX:G1HeapRegionSize=32M -XX:+DisableExplicitGC -XX:+AlwaysPreTouch -XX:+ParallelRefProcEnabled", "zgc", "-XX:+UseZGC -XX:+UnlockExperimentalVMOptions", "none", "");

	private static readonly string[] _GC_FLAGS = new string[6] { "-XX:+UseG1GC", "-XX:+UseZGC", "-XX:+UseShenandoahGC", "-XX:+UseParallelGC", "-XX:+UseConcMarkSweepGC", "-XX:+UseSerialGC" };

	public static string PresetArgs(object key)
	{
		string key2 = Py.Lower(Py.Strip(Py.AsStr(Py.Or(key, "auto"), "strip")));
		return (string)(ARGS.ContainsKey(key2) ? ARGS[key2] : ARGS["auto"]);
	}

	public static string Apply(object preset, object existing = null)
	{
		string text = Py.S(existing);
		if (ArgSplit.SplitArgs(text).Any((string a) => _GC_FLAGS.Contains<string>(a) || (a.StartsWith("-XX:+Use") && a.Contains("GC"))))
		{
			return Py.Strip(text);
		}
		string text2 = PresetArgs(preset);
		if (text2.Length == 0)
		{
			return Py.Strip(text);
		}
		if (!Py.Truthy(existing))
		{
			return text2;
		}
		return Py.Strip(text2 + " " + Py.Str(existing));
	}
}
