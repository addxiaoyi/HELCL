using System.Collections.Generic;

namespace HelL.Core.Launcher;

public static class I18nCore
{
	public static readonly PyDict LANGUAGES = PyDict.Of("zh_CN", "简体中文", "en", "English");

	public const string _DEFAULT_LANG = "zh_CN";

	private static volatile string _current_lang = "zh_CN";

	private static Dictionary<string, PyDict> _strings = new Dictionary<string, PyDict>();

	private static readonly List<string> _order = new List<string>();

	private static volatile bool _loaded;

	private static readonly object _lock = new object();

	public static string CurrentLang
	{
		get
		{
			return _current_lang;
		}
		set
		{
			_current_lang = value;
		}
	}

	private static void LoadStrings()
	{
		Dictionary<string, PyDict> dictionary = new Dictionary<string, PyDict>();
		_order.Clear();
		foreach (string item in CoreResources.ListDir("locales"))
		{
			if (!item.EndsWith(".json"))
			{
				continue;
			}
			string text = item.Substring(0, item.Length - 5);
			try
			{
				if (PyJson.Loads(CoreResources.ReadText("locales/" + item)) is PyDict value)
				{
					dictionary[text] = value;
					_order.Add(text);
				}
			}
			catch (PyException)
			{
			}
		}
		if (!dictionary.ContainsKey("zh_CN"))
		{
			dictionary["zh_CN"] = new PyDict();
			_order.Add("zh_CN");
		}
		if (!dictionary.ContainsKey("en"))
		{
			dictionary["en"] = new PyDict();
			_order.Add("en");
		}
		_strings = dictionary;
		_loaded = true;
	}

	private static void Ensure()
	{
		if (_loaded)
		{
			return;
		}
		lock (_lock)
		{
			if (!_loaded)
			{
				LoadStrings();
			}
		}
	}

	public static PyDict AvailableLanguages()
	{
		Ensure();
		PyDict pyDict = new PyDict(LANGUAGES);
		foreach (string item in _order)
		{
			pyDict.SetDefault(item, item);
		}
		return pyDict;
	}

	public static string CurrentLanguage()
	{
		return _current_lang;
	}

	public static void SetLanguage(object lang)
	{
		Ensure();
		string s = Py.AsStr(Py.Or(lang, "zh_CN"), "strip");
		s = Py.Strip(s).Replace("-", "_");
		if (!_strings.ContainsKey(s))
		{
			s = "zh_CN";
		}
		_current_lang = s;
		Config.CONFIG.Set("language", s);
		Config.CONFIG.Save();
	}

	public static void InitLanguage()
	{
		SetLanguage(Config.CONFIG.Get("language", ""));
	}

	private static object Lookup(string key, string lang)
	{
		Ensure();
		if (lang != null && _strings.TryGetValue(lang, out PyDict value) && value.TryGetValue(key, out object value2))
		{
			return value2;
		}
		if (lang != "zh_CN" && lang != "en" && _strings.TryGetValue("en", out PyDict value3) && value3.TryGetValue(key, out object value4))
		{
			return value4;
		}
		return null;
	}

	public static string Tr(string key, string lang = null, string dflt = null)
	{
		if (lang == null)
		{
			lang = CurrentLanguage();
		}
		object obj = Lookup(key, lang);
		if (obj != null)
		{
			return (obj as string) ?? Py.Str(obj);
		}
		return dflt ?? key;
	}

	public static object TrAny(object key, object lang)
	{
		if (key is PyDict || key is List<object>)
		{
			throw new TypeError("unhashable type: '" + Py.TypeName(key) + "'");
		}
		if (lang is PyDict || lang is List<object>)
		{
			throw new TypeError("unhashable type: '" + Py.TypeName(lang) + "'");
		}
		string lang2 = ((lang == null) ? CurrentLanguage() : ((lang as string) ?? ("\0" + Py.Repr(lang))));
		if (!(key is string text))
		{
			return key;
		}
		return Lookup(text, lang2) ?? text;
	}

	public static void AddTranslations(string lang, PyDict mapping)
	{
		Ensure();
		lock (_lock)
		{
			if (!_strings.ContainsKey(lang))
			{
				_strings[lang] = new PyDict();
				_order.Add(lang);
			}
			_strings[lang].Update(mapping);
		}
	}

	public static void SaveTranslations(string lang)
	{
		Ensure();
		if (_strings.ContainsKey(lang))
		{
			Utils.WriteJson(PPath.Join(Utils.ROOT, "mclauncher", "locales", lang + ".json"), _strings[lang]);
		}
	}

	internal static void Reload()
	{
		lock (_lock)
		{
			_loaded = false;
			LoadStrings();
		}
	}
}
