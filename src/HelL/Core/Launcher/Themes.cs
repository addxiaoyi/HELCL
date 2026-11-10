using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace HelL.Core.Launcher;

public static class Themes
{
	public const string THEMES_DIR = "themes";

	public const string THEME_EXT = ".json";

	private static readonly string[] _KEYS = new string[]
	{
		"theme_color", "ui_dark", "ui_background", "ui_sidebar_opacity", "ui_background_blur", "ui_background_dim", "ui_background_folder", "ui_background_shuffle", "ui_background_interval", "ui_bg_preset",
		"window_mode", "custom_homepage", "homepage_mode"
	}.Concat(AppearanceProfiles.Keys).ToArray();

	private static string _themes_dir()
	{
		string text = PPath.Join(Utils.ROOT, "themes");
		Directory.CreateDirectory(PPath.Abs(text));
		return text;
	}

	private static string _theme_path(object name)
	{
		return PPath.Join(_themes_dir(), _sanitize(name) + ".json");
	}

	public static bool IsAlnum(string s, int i)
	{
		UnicodeCategory unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(s, i);
		if ((uint)unicodeCategory <= 4u || (uint)(unicodeCategory - 8) <= 2u)
		{
			return true;
		}
		return false;
	}

	public static string _sanitize(object name)
	{
		string text = Py.AsStr(name, "__iter__");
		StringBuilder stringBuilder = new StringBuilder();
		int num;
		for (num = 0; num < text.Length; num++)
		{
			int num2 = ((!char.IsHighSurrogate(text[num]) || num + 1 >= text.Length || !char.IsLowSurrogate(text[num + 1])) ? 1 : 2);
			char value = text[num];
			if (IsAlnum(text, num) || (num2 == 1 && " _-".IndexOf(value) >= 0))
			{
				stringBuilder.Append(text, num, num2);
			}
			else
			{
				stringBuilder.Append('_');
			}
			num += num2 - 1;
		}
		string text2 = Py.Strip(stringBuilder.ToString());
		if (text2.Length <= 0)
		{
			return "untitled";
		}
		return text2;
	}

	private static PyDict _current_theme()
	{
		LauncherConfig cONFIG = Config.CONFIG;
        var theme = PyDict.Of("name", "当前主题", "format", 2L);
        foreach (string key in _KEYS)
        {
            object value = cONFIG.Get(key, Config.DEFAULT_CONFIG.Get(key));
            if (value != null) theme[key] = Py.DeepCopy(value);
        }
        return theme;
	}

    public static PyDict Current()
    {
        lock (Config.CONFIG._lock) return _current_theme();
    }

    public static void Apply(PyDict settings)
    {
        var patch = new PyDict();
        foreach (string key in _KEYS) if (settings.ContainsKey(key)) patch[key] = settings[key];
        AppearanceProfiles.Validate(patch);
        var config = Config.CONFIG;
        lock (config._lock)
        {
            PyDict before = config.data.Copy();
            AppearanceProfiles.RememberSource(patch, Py.S(config.Get("ui_wallpaper_mode")));
            string oldPath = Py.S(config.Get("ui_background")), oldFolder = Py.S(config.Get("ui_background_folder"));
            if (Py.S(patch.Get("ui_background", oldPath)) != oldPath || Py.S(patch.Get("ui_background_folder", oldFolder)) != oldFolder)
            {
                var history = Config.PushBackgroundHistory(oldPath, oldFolder);
                patch["ui_background_history"] = history.images; patch["ui_background_folder_history"] = history.folders;
            }
            try { config.Update(patch); config.Save(); }
            catch { config.data = before; throw; }
        }
    }

	public static List<object> ListThemes()
	{
		string text = _themes_dir();
		List<object> list = new List<object>();
		if (!LocalFs.IsDir(text))
		{
			return list;
		}
		foreach (string item in LocalFs.SortedChildren(text))
		{
			PPath pPath = new PPath(item);
			if (!(pPath.Suffix.ToLowerInvariant() != ".json") && Utils.ReadJson(item) is PyDict pyDict)
			{
				list.Add(PyDict.Of("name", pyDict.ContainsKey("name") ? pyDict["name"] : pPath.Stem, "file", pPath.Name, "theme_color", pyDict.ContainsKey("theme_color") ? pyDict["theme_color"] : "#2E9B6B", "ui_dark", Py.Truthy(pyDict.ContainsKey("ui_dark") ? pyDict["ui_dark"] : ((object)false))));
			}
		}
		return list;
	}

	public static PyDict SaveTheme(object name)
	{
		PyDict pyDict = Current();
		pyDict["name"] = name;
		Utils.WriteJson(_theme_path(name), pyDict);
		return pyDict;
	}

	public static object LoadTheme(object name)
	{
		string text = _theme_path(name);
		if (!LocalFs.IsFile(text))
		{
			throw new FileNotFoundError("主题包不存在: " + Py.Str(name));
		}
		if (!(Utils.ReadJson(text, new PyDict()) is PyDict pyDict))
		{
			throw new ValueError("主题包数据损坏: " + Py.Str(name));
		}
        Apply(pyDict);
		return pyDict;
	}

	public static void DeleteTheme(object name)
	{
		string text = _theme_path(name);
		if (LocalFs.IsFile(text))
		{
			File.Delete(PPath.Abs(text));
		}
	}

	public static void Copy2(string src, string dst)
	{
		string text = PPath.Abs(dst);
		if (Directory.Exists(text))
		{
			text = Path.Combine(text, new PPath(src).Name);
		}
		File.Copy(PPath.Abs(src), text, overwrite: true);
		try
		{
			File.SetLastWriteTimeUtc(text, File.GetLastWriteTimeUtc(PPath.Abs(src)));
		}
		catch (IOException)
		{
		}
	}

	public static string ExportTheme(object name, object dest)
	{
		string text = _theme_path(name);
		if (!LocalFs.IsFile(text))
		{
			throw new FileNotFoundError("主题包不存在: " + Py.Str(name));
		}
		string text2 = PPath.Norm(Py.AsStr(dest, "__fspath__"));
		Copy2(text, text2);
		return text2;
	}

	public static string ImportTheme(object path)
	{
		string text = PPath.Norm(Py.AsStr(path, "__fspath__"));
		if (!LocalFs.IsFile(text))
		{
			throw new FileNotFoundError("文件不存在: " + Py.Str(path));
		}
		if (!(Utils.ReadJson(text) is PyDict pyDict))
		{
			throw new ValueError("文件不是有效的主题包");
		}
		string text2 = Py.Str(pyDict.ContainsKey("name") ? pyDict["name"] : new PPath(text).Stem);
        AppearanceProfiles.Validate(pyDict);
		string dst = _theme_path(text2);
		Copy2(text, dst);
		return text2;
	}
}
