using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace HelL.Core.Launcher.Ai;

public static class ModelCaps
{
	private sealed class Entry
	{
		public string layer;

		public Regex rx;

		public object apiTypeMatch;

		public object baseUrlMatch;

		public PyDict props;
	}

	private static readonly object _lock = new object();

	private static List<Entry> _cache;

	private static readonly string[] _PROP_KEYS = new string[4] { "contextWindow", "inputFormat", "outputFormat", "supportsToolCall" };

	private static readonly string[] _INPUT_KEYS = new string[5] { "supportsText", "supportsImage", "supportsVideo", "supportsAudio", "supportsPdf" };

	private static PyDict _Fallback()
	{
		return PyDict.Of("contextWindow", 200000L, "inputFormat", PyDict.Of("supportsText", true, "supportsImage", false, "supportsVideo", false, "supportsAudio", false, "supportsPdf", false), "outputFormat", PyDict.Of("supportsText", true), "supportsToolCall", true);
	}

	private static Regex Full(string pat)
	{
		try
		{
			return new Regex("^(?:" + pat + ")\\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
		}
		catch (ArgumentException)
		{
			return null;
		}
	}

	private static List<Entry> _load()
	{
		if (_cache != null)
		{
			return _cache;
		}
		lock (_lock)
		{
			if (_cache != null)
			{
				return _cache;
			}
			object obj;
			try
			{
				obj = PyJson.Loads(CoreResources.ReadText("ai/model_rules.json"));
			}
			catch (Exception)
			{
				obj = null;
			}
			PyDict pyDict = (obj as PyDict) ?? new PyDict();
			List<Entry> list = new List<Entry>();
			string[] array = new string[3] { "modelRules", "modelApiRules", "providerSiteRules" };
			foreach (string text in array)
			{
				foreach (object item in Py.ListOr(pyDict.Get(text)))
				{
					if (item is PyDict rule)
					{
						Entry entry = _compile_rule(rule, text);
						if (entry != null)
						{
							list.Add(entry);
						}
					}
				}
			}
			_cache = list;
			return _cache;
		}
	}

	private static Entry _compile_rule(PyDict rule, string layer)
	{
		if (!(rule.Get("modelMatch") is string { Length: not 0 } text))
		{
			return null;
		}
		Regex regex = Full(text);
		if (regex == null)
		{
			return null;
		}
		PyDict pyDict = _slim_props(((Py.Or(rule.Get("config"), new PyDict()) as PyDict) ?? new PyDict()).Get("properties"));
		if (pyDict == null)
		{
			return null;
		}
		return new Entry
		{
			layer = layer,
			rx = regex,
			apiTypeMatch = rule.Get("apiTypeMatch"),
			baseUrlMatch = rule.Get("baseUrlMatch"),
			props = pyDict
		};
	}

	private static PyDict _slim_props(object props0)
	{
		if (!(props0 is PyDict pyDict))
		{
			return null;
		}
		PyDict pyDict2 = new PyDict();
		string[] pROP_KEYS = _PROP_KEYS;
		foreach (string text in pROP_KEYS)
		{
			object obj = pyDict.Get(text);
			if (obj == null)
			{
				continue;
			}
			if ((text == "inputFormat" || text == "outputFormat") && obj is PyDict pyDict3)
			{
				PyDict pyDict4 = new PyDict();
				foreach (KeyValuePair<string, object> item in pyDict3)
				{
					if (Array.IndexOf<string>(_INPUT_KEYS, item.Key) >= 0 && item.Value != null)
					{
						pyDict4[item.Key] = Py.Truthy(item.Value);
					}
				}
				if (pyDict4.Count > 0)
				{
					pyDict2[text] = pyDict4;
				}
			}
			else if (text == "contextWindow")
			{
				try
				{
					pyDict2[text] = Py.Int(obj);
				}
				catch (Exception ex) when (ex is TypeError || ex is ValueError)
				{
				}
			}
			else
			{
				pyDict2[text] = Py.Truthy(obj);
			}
		}
		if (pyDict2.Count <= 0)
		{
			return null;
		}
		return pyDict2;
	}

	public static string NormalizeBaseUrl(object url)
	{
		if (!Py.Truthy(url))
		{
			return null;
		}
		string text = Py.Strip(Py.Str(url));
		if (text.Length == 0)
		{
			return null;
		}
		int num = text.IndexOf("://", StringComparison.Ordinal);
		if (num <= 0)
		{
			return Py.RStrip(text, "/");
		}
		int num2 = text.IndexOfAny(new char[2] { '?', '#' }, num + 3);
		string text2 = ((num2 >= 0) ? text.Substring(0, num2) : text);
		string text3 = ((num2 >= 0) ? text.Substring(num2) : "");
		int num3 = text2.IndexOf('/', num + 3);
		if (num3 < 0)
		{
			return text2 + text3;
		}
		return text2.Substring(0, num3) + Py.RStrip(text2.Substring(num3), "/") + text3;
	}

	private static object _overlay(object bas, object patch)
	{
		if (patch == null)
		{
			return bas;
		}
		if (!(bas is PyDict pyDict) || !(patch is PyDict pyDict2))
		{
			return patch;
		}
		PyDict pyDict3 = pyDict.Copy();
		foreach (KeyValuePair<string, object> item in pyDict2)
		{
			if (item.Value != null)
			{
				if (pyDict3.Get(item.Key) is PyDict bas2 && item.Value is PyDict)
				{
					pyDict3[item.Key] = _overlay(bas2, item.Value);
				}
				else
				{
					pyDict3[item.Key] = item.Value;
				}
			}
		}
		return pyDict3;
	}

	private static List<Entry> _overrides(PyDict settings)
	{
		List<Entry> list = new List<Entry>();
		if (settings == null || !(settings.Get("ai_model_caps_overrides") is PyDict pyDict))
		{
			return list;
		}
		foreach (KeyValuePair<string, object> item in pyDict)
		{
			if (!(item.Value is PyDict props))
			{
				continue;
			}
			Regex regex = Full(item.Key);
			if (regex != null)
			{
				PyDict pyDict2 = _slim_props(props);
				if (pyDict2 != null)
				{
					list.Add(new Entry
					{
						rx = regex,
						props = pyDict2
					});
				}
			}
		}
		return list;
	}

	private static bool _match_any(object pattern, string text)
	{
		return Full(Py.AsStr(pattern, "__re__"))?.IsMatch(text) ?? false;
	}

	public static PyDict Resolve(object modelId, string apiType = null, object baseUrl = null, PyDict settings = null)
	{
		string input = Py.Strip(Py.Str(Py.Or(modelId, "")));
		string text = NormalizeBaseUrl(baseUrl);
		object obj = _Fallback();
		foreach (Entry item in _load())
		{
			if (item.rx.IsMatch(input) && (item.apiTypeMatch == null || (apiType != null && _match_any(item.apiTypeMatch, apiType))) && (item.baseUrlMatch == null || (text != null && _match_any(item.baseUrlMatch, text))))
			{
				obj = _overlay(obj, item.props);
			}
		}
		foreach (Entry item2 in _overrides(settings))
		{
			if (item2.rx.IsMatch(input))
			{
				obj = _overlay(obj, item2.props);
			}
		}
		return (PyDict)obj;
	}

	public static string ApiTypeForMode(object mode)
	{
		return "openai-chat-completions";
	}

	public static void ResetCache()
	{
		lock (_lock)
		{
			_cache = null;
		}
	}
}
