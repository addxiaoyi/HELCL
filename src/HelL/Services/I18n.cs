using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace HelL.Services;

public static class I18n
{
	public const string DefaultLang = "zh_CN";

	private static readonly Dictionary<string, Dictionary<string, string>> _tables = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

	private static Dictionary<string, string> _current = new Dictionary<string, string>(StringComparer.Ordinal);

	private static Dictionary<string, string> _en = new Dictionary<string, string>(StringComparer.Ordinal);

	private static readonly HashSet<string> _missing = new HashSet<string>(StringComparer.Ordinal);

	private static readonly object _gate = new object();

	private static bool _loaded;

	public static string Current { get; private set; } = "zh_CN";

	public static string LocalesDir { get; private set; } = "";

	public static IReadOnlyCollection<string> Missing
	{
		get
		{
			lock (_gate)
			{
				return (IReadOnlyCollection<string>)(object)_missing.ToArray();
			}
		}
	}

	public static IReadOnlyCollection<string> KnownKeys
	{
		get
		{
			lock (_gate)
			{
				Dictionary<string, string> value;
				return (IReadOnlyCollection<string>)(object)(_tables.TryGetValue("zh_CN", out value) ? value.Keys.ToArray() : Array.Empty<string>());
			}
		}
	}

	public static IReadOnlyList<string> Languages
	{
		get
		{
			EnsureTables();
			lock (_gate)
			{
				return _tables.Keys.OrderBy<string, string>((string k) => k, StringComparer.Ordinal).ToArray();
			}
		}
	}

	public static void Init(string[]? args = null)
	{
		Load(ArgValue(args, "--lang") ?? Environment.GetEnvironmentVariable("TIPCL_LANG") ?? ReadConfigLanguage());
	}

	public static void Load(string? lang)
	{
		EnsureTables();
		lang = (lang ?? "").Trim().Replace('-', '_');
		if (lang.Length == 0)
		{
			lang = "zh_CN";
		}
		lock (_gate)
		{
			if (!_tables.ContainsKey(lang))
			{
				lang = "zh_CN";
			}
			Current = lang;
			_current = _tables[lang];
			_en = (_tables.TryGetValue("en", out Dictionary<string, string> value) ? value : new Dictionary<string, string>(StringComparer.Ordinal));
			_missing.Clear();
		}
		CultureInfo.CurrentUICulture = ((!(lang == "en")) ? CultureInfo.GetCultureInfo("zh-CN") : CultureInfo.GetCultureInfo("en-US"));
	}

	public static string L(string zh)
	{
		if (string.IsNullOrEmpty(zh))
		{
			return zh;
		}
		if (_current.TryGetValue(zh, out string value) && value.Length > 0)
		{
			return value;
		}
		if (Current != "zh_CN")
		{
			if (Current != "en" && _en.TryGetValue(zh, out string value2) && value2.Length > 0)
			{
				return value2;
			}
			if (I18nCheck.HasCjk(zh))
			{
				lock (_gate)
				{
					_missing.Add(zh);
				}
			}
		}
		return zh;
	}

	public static string? Peek(string zh)
	{
		if (string.IsNullOrEmpty(zh))
		{
			return null;
		}
		if (_current.TryGetValue(zh, out string value) && value.Length > 0)
		{
			return value;
		}
		if (Current != "zh_CN" && Current != "en" && _en.TryGetValue(zh, out string value2) && value2.Length > 0)
		{
			return value2;
		}
		return null;
	}

	public static string L(string zh, params object?[] args)
	{
		string text = L(zh);
		if (args.Length == 0)
		{
			return text;
		}
		try
		{
			return string.Format(CultureInfo.CurrentCulture, text, args);
		}
		catch (FormatException)
		{
			try
			{
				return string.Format(CultureInfo.CurrentCulture, zh, args);
			}
			catch (FormatException)
			{
				return text;
			}
		}
	}

	public static bool Has(string lang, string key)
	{
		EnsureTables();
		lock (_gate)
		{
			Dictionary<string, string> value;
			string value2;
			return _tables.TryGetValue(lang, out value) && value.TryGetValue(key, out value2) && value2.Length > 0;
		}
	}

	private static void EnsureTables()
	{
		lock (_gate)
		{
			if (_loaded)
			{
				return;
			}
			_loaded = true;
			_tables["zh_CN"] = new Dictionary<string, string>(StringComparer.Ordinal);
			_tables["en"] = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string language in new[] { "zh_CN", "en" })
            {
                string? embedded = HelL.Core.CoreResources.ReadText("locales/" + language + ".json");
                if (embedded != null) MergeTable(language, embedded);
            }
			string text;
			try
			{
				text = Path.Combine(BridgeHost.FindRoot(), "mclauncher", "locales");
			}
			catch
			{
				return;
			}
			if (!Directory.Exists(text))
			{
				return;
			}
			LocalesDir = text;
			foreach (string item in Directory.EnumerateFiles(text, "*.json").OrderBy<string, string>((string f) => f, StringComparer.Ordinal))
			{
				try
				{
                    MergeTable(Path.GetFileNameWithoutExtension(item), File.ReadAllText(item));
				}
				catch (Exception ex) when (((ex is JsonException || ex is IOException) ? 1 : 0) != 0)
				{
                    System.Diagnostics.Trace.TraceWarning("I18n locale " + Path.GetFileName(item) + ": " + ex.Message);
				}
			}
		}
	}

    private static void MergeTable(string language, string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("Locale must be an object.");
        if (!_tables.TryGetValue(language, out var table)) _tables[language] = table = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in document.RootElement.EnumerateObject())
            if (entry.Value.ValueKind == JsonValueKind.String) table[entry.Name] = entry.Value.GetString() ?? "";
    }

	private static string ReadConfigLanguage()
	{
		try
		{
			string path = Path.Combine(BridgeHost.FindRoot(), "config.json");
			if (!File.Exists(path))
			{
				return "zh_CN";
			}
			using JsonDocument jsonDocument = JsonDocument.Parse(File.ReadAllText(path));
			JsonElement value;
			return (jsonDocument.RootElement.TryGetProperty("language", out value) && value.ValueKind == JsonValueKind.String) ? (value.GetString() ?? "zh_CN") : "zh_CN";
		}
		catch
		{
			return "zh_CN";
		}
	}

	private static string? ArgValue(string[]? args, string name)
	{
		if (args == null)
		{
			return null;
		}
		int num = Array.IndexOf<string>(args, name);
		if (num < 0 || num + 1 >= args.Length)
		{
			return null;
		}
		return args[num + 1];
	}
}
