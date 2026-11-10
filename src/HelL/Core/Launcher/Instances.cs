using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace HelL.Core.Launcher;

public static class Instances
{
	public const string INSTANCE_META = ".instance.json";

	public const string JAVA_AUTO = "自动选择";

	private const int _MAX_INSTANCE_NAME = 48;

	private static readonly Regex _ILLEGAL_NAME = new Regex("[\\\\/:*?\"<>|\\x00-\\x1f]");

	private static readonly HashSet<string> _WIN_RESERVED = new HashSet<string>(new string[4] { "CON", "PRN", "AUX", "NUL" }.Concat(Enumerable.Range(1, 9).Select(delegate(int i)
	{
		int num = i;
		return "COM" + num;
	})).Concat(Enumerable.Range(1, 9).Select(delegate(int i)
	{
		int num = i;
		return "LPT" + num;
	})));

	public static readonly string[] _STANDARD_DIRS = new string[18]
	{
		"mods", "config", "saves", "resourcepacks", "shaderpacks", "datapacks", "screenshots", "crash-reports", "logs", "options",
		"screenshots", "crash-reports", "logs", "options", "servers", "texturepacks", "versions", "libraries"
	};

	private static readonly Regex _NAME_OK = new Regex("\\A[^\\\\/:*?\"<>|]+\\z");

	public static string RootDir()
	{
		return Config.CONFIG.instances_dir;
	}

	public static string RootName()
	{
		string name = new PPath(RootDir()).Name;
		if (name.Length <= 0)
		{
			return ".minecraft";
		}
		return name;
	}

	public static bool SingleRootMode()
	{
		return LocalFs.IsFile(PPath.Join(RootDir(), ".instance.json"));
	}

	public static List<object> ListLegacyInstances()
	{
		string text = RootDir();
		if (!LocalFs.IsDir(text))
		{
			return new List<object>();
		}
		List<string> list = new List<string>();
		foreach (string item in Directory.EnumerateDirectories(PPath.Abs(text)))
		{
			if (File.Exists(Path.Combine(item, ".instance.json")))
			{
				list.Add(Path.GetFileName(item));
			}
		}
		list.Sort(string.CompareOrdinal);
		return list.Cast<object>().ToList();
	}

	public static List<object> ListInstances()
	{
		List<object> list = ListLegacyInstances();
		if (SingleRootMode() || list.Count == 0)
		{
			List<object> list2 = new List<object>();
			list2.Add(RootName());
			list2.AddRange(list);
			return list2;
		}
		return list;
	}

	public static string ResolveName(object name)
	{
		string text = Py.SS(name);
		if (text.Length == 0 || text == RootName())
		{
			return "";
		}
		if (SingleRootMode() && !LocalFs.IsFile(PPath.Join(RootDir(), text, ".instance.json")))
		{
			return "";
		}
		return text;
	}

	public static string SanitizeInstanceName(object raw, string fallback = "游戏")
	{
		string input = _ILLEGAL_NAME.Replace(Py.SS(raw), "-");
		input = Py.Strip(Regex.Replace(input, "\\s+", " "), " .");
		if (input.Length == 0)
		{
			input = fallback;
		}
		if (input.Length > 48)
		{
			input = Py.RStrip(input.Substring(0, 48), " .");
		}
		if (_WIN_RESERVED.Contains(input.Split(new char[1] { '.' }, 2)[0].ToUpperInvariant()))
		{
			input = "游戏-" + input;
		}
		if (input.Length > 48)
		{
			input = Py.RStrip(input.Substring(0, 48), " .");
		}
		if (input.Length == 0)
		{
			input = fallback;
		}
		return input;
	}

	public static string UniqueInstanceName(object raw, string fallback = "游戏")
	{
		string text = SanitizeInstanceName(raw, fallback);
		HashSet<string> hashSet = new HashSet<string>(from x in ListInstances()
			select (string)x);
		string instances_dir = Config.CONFIG.instances_dir;
		string text2 = text;
		int num = 2;
		while (hashSet.Contains(text2) || LocalFs.Exists(PPath.Join(instances_dir, text2)))
		{
			string text3 = "-" + num;
			string text4 = text;
			int num2 = 48 - text3.Length;
			if (text4.Length > num2)
			{
				text4 = Py.RStrip(text4.Substring(0, num2), " .");
				if (text4.Length == 0)
				{
					text4 = fallback;
				}
			}
			text2 = text4 + text3;
			num++;
		}
		return text2;
	}

	public static string GetInstancePath(object name)
	{
		PPath pPath = new PPath(Config.CONFIG.instances_dir).Resolve();
		if (!Py.Truthy(name) || Py.Str(name) == pPath.Name)
		{
			return pPath.ToString();
		}
		string text = Py.Str(name);
		if (text == "." || text == ".." || !_NAME_OK.IsMatch(text))
		{
			throw new InstanceError(Py.Fmt("非法实例名: {0!r}", name));
		}
		PPath pPath2 = (pPath / text).Resolve();
		if (!pPath2.Parent.Equals(pPath))
		{
			throw new InstanceError(Py.Fmt("非法实例名: {0!r}", name));
		}
		return pPath2.ToString();
	}

	public static Instance CreateUniqueInstance(object raw, string fallback = "游戏", PyDict meta = null)
	{
		Instance instance = new Instance(UniqueInstanceName(raw, fallback), forCreation: true);
		instance.Create(meta);
		return instance;
	}
}
