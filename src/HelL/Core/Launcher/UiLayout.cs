using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace HelL.Core.Launcher;

public static class UiLayout
{
	public const int LAYOUT_VERSION = 1;

	public static readonly List<KeyValuePair<string, (long w, long h)>> CARD_MIN_SIZE = new List<KeyValuePair<string, (long, long)>>
	{
		new KeyValuePair<string, (long, long)>("banner", (340L, 125L)),
		new KeyValuePair<string, (long, long)>("config", (330L, 300L)),
		new KeyValuePair<string, (long, long)>("log", (260L, 180L)),
		new KeyValuePair<string, (long, long)>("news", (220L, 180L)),
		new KeyValuePair<string, (long, long)>("quick", (220L, 150L)),
		new KeyValuePair<string, (long, long)>("notes", (180L, 130L)),
		new KeyValuePair<string, (long, long)>("playtime", (220L, 96L)),
		new KeyValuePair<string, (long, long)>("tasks", (220L, 130L)),
		new KeyValuePair<string, (long, long)>("skin", (160L, 200L)),
		new KeyValuePair<string, (long, long)>("clock", (240L, 160L)),
		new KeyValuePair<string, (long, long)>("links", (240L, 170L)),
		new KeyValuePair<string, (long, long)>("image", (240L, 180L)),
		new KeyValuePair<string, (long, long)>("system", (260L, 160L)),
		new KeyValuePair<string, (long, long)>("screenshots", (300L, 220L)),
		new KeyValuePair<string, (long, long)>("servers", (280L, 200L))
	};

	public const string DEFAULT_PROFILE = "";

	public static readonly (long w, long h) FALLBACK_MIN = (w: 200L, h: 120L);

	public static bool IsKnownCard(string t)
	{
		return CARD_MIN_SIZE.Any((KeyValuePair<string, (long w, long h)> kv) => kv.Key == t) || Plugins.PluginManifest.IsModuleType(t);
	}

	public static (long w, long h) MinSizeFor(string card_type)
	{
		foreach (KeyValuePair<string, (long, long)> item in CARD_MIN_SIZE)
		{
			if (item.Key == card_type)
			{
				return item.Value;
			}
		}
		return FALLBACK_MIN;
	}

	public static LayoutDoc DefaultDoc()
	{
		return DashboardPresets.Create("standard");
	}

	public static LayoutDoc ParseDoc(object data)
	{
		if (!(data is PyDict pyDict) || !(pyDict.Get("items") is List<object> list))
		{
			return null;
		}
		LayoutDoc layoutDoc = LayoutDoc.FromDict(pyDict);
		layoutDoc.items = layoutDoc.items.Where((LayoutItem it) => IsKnownCard(it.type)).ToList();
		if (layoutDoc.items.Count <= 0 && list.Count > 0)
		{
			return null;
		}
		return layoutDoc;
	}

	private static LauncherConfig Cfg()
	{
		return Config.CONFIG;
	}

	private static T Txn<T>(Func<T> body)
	{
		lock (Cfg()._lock)
		{
			return body();
		}
	}

	private static void Txn(Action body)
	{
		lock (Cfg()._lock)
		{
			body();
		}
	}

	public static LayoutDoc LoadActiveDoc()
	{
		object obj = Cfg().Get("ui_layout");
		if (!Py.Truthy(obj))
		{
			return DefaultDoc();
		}
		return LayoutDoc.FromDict(obj);
	}

	public static (LayoutDoc doc, string profile, PyDict profiles) LayoutSnapshot()
	{
		return Patch_LayoutSnapshot();
	}

	public static void SaveActiveDoc(LayoutDoc doc, string profile = null)
	{
		Patch_SaveActiveDoc(doc, profile);
	}

	public static (bool saved, string name) SaveCurrent(LayoutDoc doc, string expected_profile = null)
	{
		return Patch_SaveCurrent(doc, expected_profile);
	}

	public static string ActiveProfile()
	{
		return Py.Str(Py.Or(Cfg().Get("ui_layout_profile"), ""));
	}

	public static PyDict ListProfiles()
	{
		if (!(Cfg().Get("ui_layouts") is PyDict other))
		{
			return new PyDict();
		}
		return new PyDict(other);
	}

	public static void SaveProfile(string name, LayoutDoc doc)
	{
		name = Py.Strip(name ?? "");
		if (name.Length != 0)
		{
			Txn(delegate
			{
				LauncherConfig launcherConfig = Cfg();
				PyDict pyDict = ListProfiles();
				pyDict[name] = doc.ToDict();
				launcherConfig.Set("ui_layouts", pyDict);
				launcherConfig.Set("ui_layout_profile", name);
				launcherConfig.Set("ui_layout", Py.DeepCopy(pyDict[name]));
				launcherConfig.Save();
			});
		}
	}

	public static LayoutDoc ActivateProfile(string name)
	{
		return Txn(() => ActivateProfileLocked(name));
	}

	private static LayoutDoc ActivateProfileLocked(string name)
	{
		LauncherConfig launcherConfig = Cfg();
		name = Py.Strip(name ?? "");
		if (name.Length == 0)
		{
			launcherConfig.Set("ui_layout", null);
			launcherConfig.Set("ui_layout_profile", "");
			launcherConfig.Save();
			return DefaultDoc();
		}
		object obj = ListProfiles().Get(name);
		if (obj == null)
		{
			launcherConfig.Set("ui_layout", null);
			launcherConfig.Set("ui_layout_profile", "");
			launcherConfig.Save();
			return DefaultDoc();
		}
		launcherConfig.Set("ui_layout", Py.DeepCopy(obj));
		launcherConfig.Set("ui_layout_profile", name);
		launcherConfig.Save();
		return LayoutDoc.FromDict(obj);
	}

	public static bool DeleteProfile(string name)
	{
		name = Py.Strip(name ?? "");
		return Txn(delegate
		{
			PyDict pyDict = ListProfiles();
			if (!pyDict.ContainsKey(name))
			{
				return false;
			}
			pyDict.Remove(name);
			LauncherConfig launcherConfig = Cfg();
			launcherConfig.Set("ui_layouts", pyDict);
			if (ActiveProfile() == name)
			{
				launcherConfig.Set("ui_layout", null);
				launcherConfig.Set("ui_layout_profile", "");
			}
			launcherConfig.Save();
			return true;
		});
	}

	public static void ResetToDefault()
	{
		Txn(delegate
		{
			LauncherConfig launcherConfig = Cfg();
			launcherConfig.Set("ui_layout", null);
			launcherConfig.Set("ui_layout_profile", "");
			launcherConfig.Save();
		});
	}

	public static bool ExportDoc(LayoutDoc doc, string path)
	{
		try
		{
			File.WriteAllText(Utils.AbsPath(path), PyJson.Dumps(doc.ToDict(), 2, ensureAscii: false), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			return true;
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
		{
			return false;
		}
	}

	public static LayoutDoc ImportDoc(string path)
	{
		object data;
		try
		{
			data = PyJson.Loads(Utils.DecodeUtf8Strict(File.ReadAllBytes(Utils.AbsPath(path))));
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JSONDecodeError)
		{
			return null;
		}
		return ParseDoc(data);
	}

	private static string Patch_ForkName(PyDict profiles)
	{
		string text = I18nCore.Tr("自定义布局");
		if (string.IsNullOrEmpty(text))
		{
			text = "自定义布局";
		}
		string text2 = text;
		int num = 2;
		while (profiles.ContainsKey(text2))
		{
			text2 = text + " " + num;
			num++;
		}
		return text2;
	}

	private static string Patch_WriteLocked(LauncherConfig cfg, PyDict data, string profile)
	{
		PyDict pyDict = ListProfiles();
		if (profile.Length == 0)
		{
			profile = Patch_ForkName(pyDict);
		}
		pyDict[profile] = Py.DeepCopy(data);
		cfg.Set("ui_layouts", pyDict);
		cfg.Set("ui_layout_profile", profile);
		cfg.Set("ui_layout", data);
		return profile;
	}

	private static (bool, string) Patch_SaveCurrent(LayoutDoc doc, string expected_profile)
	{
		LauncherConfig cONFIG = Config.CONFIG;
		lock (cONFIG._lock)
		{
			string text = ActiveProfile();
			if (expected_profile != null && Py.Strip(expected_profile) != text)
			{
				return (false, text);
			}
			string text2 = Patch_WriteLocked(cONFIG, doc.ToDict(), text);
			cONFIG.Save();
			return (true, text2);
		}
	}

	private static void Patch_SaveActiveDoc(LayoutDoc doc, string profile)
	{
		LauncherConfig cONFIG = Config.CONFIG;
		lock (cONFIG._lock)
		{
			string profile2 = Py.Strip(profile ?? ActiveProfile());
			Patch_WriteLocked(cONFIG, doc.ToDict(), profile2);
			cONFIG.Save();
		}
	}

	private static void Patch_MigrateLocked()
	{
		LauncherConfig cONFIG = Config.CONFIG;
		if (ActiveProfile().Length != 0)
		{
			return;
		}
		object obj = cONFIG.Get("ui_layout");
		if (!Py.Truthy(obj))
		{
			return;
		}
		LayoutDoc layoutDoc = ParseDoc(obj);
		if (layoutDoc == null)
		{
			cONFIG.Set("ui_layout", null);
			cONFIG.Save();
			return;
		}
		string text = PyJson.Dumps(layoutDoc.ToDict());
		string text2 = PyJson.Dumps(DefaultDoc().ToDict());
		if (text != text2)
		{
			Patch_WriteLocked(cONFIG, layoutDoc.ToDict(), "");
		}
		else
		{
			cONFIG.Set("ui_layout", null);
		}
		cONFIG.Save();
	}

	private static (LayoutDoc, string, PyDict) Patch_LayoutSnapshot()
	{
		lock (Config.CONFIG._lock)
		{
			Patch_MigrateLocked();
			return (LoadActiveDoc(), ActiveProfile(), ListProfiles());
		}
	}
}
