using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace HelL.Core.Launcher;

public static class CatalogFiles
{
	public static readonly Dictionary<string, string> KIND_MR = new Dictionary<string, string>
	{
		{ "mod", "mod" },
		{ "modpack", "modpack" },
		{ "shader", "shader" },
		{ "resourcepack", "resourcepack" },
		{ "datapack", "datapack" }
	};

	public static readonly Dictionary<string, long> KIND_CF = new Dictionary<string, long>
	{
		{ "mod", 6L },
		{ "modpack", 4471L },
		{ "shader", 6552L },
		{ "resourcepack", 12L },
		{ "datapack", 6945L },
		{ "world", 17L }
	};

	public const long CF_CLASS_WORLD = 17L;

	private static readonly Regex _MC_VER = new Regex("^\\d+\\.\\d+");

	private static readonly HashSet<string> _LOADERS = new HashSet<string> { "forge", "fabric", "quilt", "neoforge", "rift", "liteloader", "cauldron", "nonspecific" };

	public static readonly Dictionary<string, string[]> TYPE_FACETS = D("optimization", new string[1] { "optimization" }, "technology", new string[1] { "technology" }, "magic", new string[1] { "magic" }, "adventure", new string[1] { "adventure" }, "survival", new string[2] { "utility", "food" }, "decoration", new string[1] { "decoration" }, "realistic", new string[1] { "realistic" }, "cartoon", new string[1] { "cartoon" }, "performance", new string[1] { "performance" }, "path-tracing", new string[2] { "path-tracing", "pbr" }, "16x", new string[1] { "16x" }, "32x", new string[1] { "32x" }, "64x", new string[1] { "64x" }, "modern", new string[1] { "modern" }, "animated", new string[1] { "animated" }, "skyblock", new string[1] { "skyblock" }, "creation", new string[1] { "creation" });

	private static readonly string[][] _TYPE_ALIASES = new string[14][]
	{
		new string[2] { "优化", "optimization" },
		new string[2] { "科技", "technology" },
		new string[2] { "魔法", "magic" },
		new string[2] { "冒险", "adventure" },
		new string[2] { "生存", "survival" },
		new string[2] { "装饰", "decoration" },
		new string[2] { "写实", "realistic" },
		new string[2] { "卡通", "cartoon" },
		new string[2] { "高性能", "performance" },
		new string[2] { "光追", "path-tracing" },
		new string[2] { "现代风", "modern" },
		new string[2] { "动态效果", "animated" },
		new string[2] { "空岛", "skyblock" },
		new string[2] { "创造", "creation" }
	};

	public static readonly Dictionary<string, string[]> CF_TYPE_TOKENS = D("optimization", new string[2] { "performance", "optimization" }, "technology", new string[2] { "technology", "tech" }, "magic", new string[1] { "magic" }, "adventure", new string[1] { "adventure" }, "survival", new string[3] { "survival", "util", "food" }, "decoration", new string[1] { "decor" }, "realistic", new string[1] { "realistic" }, "cartoon", new string[2] { "cartoon", "animated" }, "performance", new string[1] { "performance" }, "path-tracing", new string[2] { "path tracing", "shader" }, "16x", new string[1] { "16x" }, "32x", new string[1] { "32x" }, "64x", new string[1] { "64x" }, "modern", new string[1] { "modern" }, "animated", new string[1] { "animated" }, "skyblock", new string[1] { "skyblock" }, "creation", new string[1] { "creation" });

	private static readonly Dictionary<long, string> CF_RELEASE = new Dictionary<long, string>
	{
		{ 1L, "release" },
		{ 2L, "beta" },
		{ 3L, "alpha" }
	};

	private static Dictionary<string, string> _ALIAS_TABLE;

	private static HashSet<string> _ANY_LABELS;

	private static readonly object _aliasLock = new object();

	private static Dictionary<string, string[]> D(params object[] kv)
	{
		Dictionary<string, string[]> dictionary = new Dictionary<string, string[]>();
		for (int i = 0; i < kv.Length; i += 2)
		{
			dictionary[(string)kv[i]] = (string[])kv[i + 1];
		}
		return dictionary;
	}

	public static List<object>[] SplitCfGameVersions(object gvs)
	{
		List<object> list = new List<object>();
		List<object> list2 = new List<object>();
		foreach (object item2 in Mods.Iter(Py.Or(gvs, new List<object>())))
		{
			string text = Py.Strip(Py.S(item2));
			if (text.Length != 0)
			{
				string item = Py.Lower(text);
				if (_LOADERS.Contains(item))
				{
					list2.Add(item);
				}
				else if (_MC_VER.IsMatch(text))
				{
					list.Add(text);
				}
			}
		}
		return new List<object>[2] { list, list2 };
	}

	private static string _date(object text)
	{
		string text2 = Py.S(text);
		if (text2.Length == 0)
		{
			return "";
		}
		return Py.Slice(text2, null, 10);
	}

	private static PyDict _row(PyDict kw)
	{
		return PyDict.Of("id", Py.Or(kw["id"], ""), "name", Py.Or(kw["name"], ""), "version_number", Py.Or(kw["version_number"], ""), "filename", Py.Or(kw["filename"], ""), "game_versions", Py.ListCtor(Py.Or(kw["game_versions"], new List<object>())), "loaders", Py.ListCtor(Py.Or(kw["loaders"], new List<object>())), "date", Py.Or(kw["date"], ""), "downloads", Py.Int(Py.Or(kw["downloads"], 0L)), "size", Py.Int(Py.Or(kw["size"], 0L)), "release_type", Py.Or(kw["release_type"], "release"), "source", Py.Or(kw["source"], ""), "changelog", Mods.SliceObj(Py.Or(kw["changelog"], ""), 400));
	}

	private static string _source_of(PyDict extra)
	{
		string text = Py.Lower(Py.S(extra?["source"]));
		if (text.StartsWith("curse", StringComparison.Ordinal))
		{
			return "curseforge";
		}
		if (text.StartsWith("modrinth", StringComparison.Ordinal))
		{
			return "modrinth";
		}
		if (Py.Truthy(extra["id"]) && !Py.Truthy(extra["slug"]))
		{
			return "curseforge";
		}
		Py.Truthy(extra["slug"]);
		return "modrinth";
	}

	private static string _kind_of(PyDict extra)
	{
		string text = Py.Lower(Py.Str(Py.Or(extra?["kind"], extra["project_type"], "mod")));
		if (KIND_CF.ContainsKey(text) || KIND_MR.ContainsKey(text))
		{
			return text;
		}
		return "mod";
	}

	public static string _game_version(PyDict extra)
	{
		string text = Py.Strip(Py.Str(Py.Or(extra["game_version"], extra["mc_version"], Py.Or(extra["version"], ""))));
		string text2 = Py.Lower(text);
		if (text.Length == 0 || text2.StartsWith("全部", StringComparison.Ordinal) || text2.StartsWith("all", StringComparison.Ordinal))
		{
			return null;
		}
		return text;
	}

	private static string _loader(PyDict extra)
	{
		object obj = Py.Or(extra["loader"], extra["loaders"], "");
		if (obj is List<object> list)
		{
			obj = ((list.Count > 0) ? list[0] : "");
		}
		string text = Py.Lower(Py.Strip(Py.Str(obj)));
		if (text.Length != 0)
		{
			switch (text)
			{
			default:
				return text;
			case "全部":
			case "all":
			case "任意":
				break;
			}
		}
		return null;
	}

	public static List<object> ListProjectFiles(DownloadManager dm, PyDict extra)
	{
		extra = new PyDict(extra ?? new PyDict());
		dm = dm ?? new DownloadManager(2L);
		if (_source_of(extra) == "curseforge")
		{
			return _list_cf(dm, extra);
		}
		return _list_mr(dm, extra);
	}

	private static List<object> _list_mr(DownloadManager dm, PyDict extra)
	{
		object obj = Py.Or(extra["slug"], extra["name"]);
		if (!Py.Truthy(obj))
		{
			return new List<object>();
		}
		string text = _game_version(extra);
		string text2 = _loader(extra);
		List<object> list = ((text2 != null) ? Py.L(text2) : null);
		List<object> list2 = Mods.ListVersions(dm, obj, text, list);
		List<object> list3 = new List<object>();
		foreach (PyDict item in list2)
		{
			PyDict pyDict2 = (Mods._primary_file(item) as PyDict) ?? new PyDict();
			list3.Add(_row(PyDict.Of("id", item["id"], "name", Py.Or(item["name"], item["version_number"], ""), "version_number", Py.Or(item["version_number"], ""), "filename", Py.Or(pyDict2["filename"], ""), "game_versions", Py.Or(item["game_versions"], new List<object>()), "loaders", Py.Or(item["loaders"], new List<object>()), "date", _date(Py.Or(item["date_published"], item["date"])), "downloads", Py.Or(item["downloads"], 0L), "size", Py.Or(pyDict2["size"], 0L), "release_type", Py.Or(item["version_type"], "release"), "source", "modrinth", "changelog", Py.Or(item["changelog"], ""))));
            ((PyDict)list3[list3.Count - 1])["version_source"] = item.Get("version_source", "");
            ((PyDict)list3[list3.Count - 1])["version_warning"] = item.Get("version_warning", "");
		}
		return list3;
	}

	private static List<object> _list_cf(DownloadManager dm, PyDict extra)
	{
		object obj = extra["id"];
		string api_key = (Py.Truthy(extra["api_key"]) ? Py.Str(extra["api_key"]) : null);
		if (!Py.Truthy(obj))
		{
			object obj2 = extra["slug"];
			string key = _kind_of(extra);
			obj = Mods.Gt(Py.Or(Py.Truthy(obj2) ? Mods.CfBySlug(dm, obj2, KIND_CF.TryGetValue(key, out var value) ? ((object)value) : null, api_key) : null, new PyDict()), "id");
		}
		if (!Py.Truthy(obj))
		{
			return new List<object>();
		}
		List<object> list = Mods.CfFiles(dm, obj, api_key, _game_version(extra), null, 50L);
		List<object> list2 = new List<object>();
		string text = _loader(extra);
		foreach (object item in list)
		{
			object obj3 = Py.Or(Mods.Gt(item, "gameVersions"), new List<object>());
			List<object>[] array = SplitCfGameVersions(obj3);
			List<object> list3 = array[0];
			List<object> list4 = array[1];
			if (text == null || list4.Contains(text) || list4.Count <= 0)
			{
				PyDict pyDict = (PyDict)item;
				object obj4 = pyDict["id"];
				object obj5 = pyDict["releaseType"];
				if (obj5 is long key2)
				{
					obj5 = (CF_RELEASE.TryGetValue(key2, out string value2) ? value2 : "release");
				}
				object obj6 = ((list3.Count > 0) ? list3 : (from x in Mods.Iter(obj3)
					where _MC_VER.IsMatch(Py.Str(x))
					select x).ToList());
				list2.Add(_row(PyDict.Of("id", obj4, "name", Py.Or(pyDict["displayName"], pyDict["fileName"], Py.Str(obj4)), "version_number", Py.Or(pyDict["displayName"], pyDict["fileName"], ""), "filename", Py.Or(pyDict["fileName"], ""), "game_versions", obj6, "loaders", list4, "date", _date(pyDict["fileDate"]), "downloads", Py.Or(pyDict["downloadCount"], 0L), "size", Py.Or(pyDict["fileLength"], pyDict["fileSizeOnDisk"], 0L), "release_type", Py.Or(obj5, "release"), "source", "curseforge")));
                ((PyDict)list2[list2.Count - 1])["version_source"] = pyDict.Get("version_source", "");
                ((PyDict)list2[list2.Count - 1])["version_warning"] = pyDict.Get("version_warning", "");
			}
		}
		return list2;
	}

	private static Dictionary<string, string> _alias_table()
	{
		lock (_aliasLock)
		{
			if (_ALIAS_TABLE != null)
			{
				return _ALIAS_TABLE;
			}
			Dictionary<string, string> dictionary = new Dictionary<string, string>();
			string[][] tYPE_ALIASES = _TYPE_ALIASES;
			foreach (string[] array in tYPE_ALIASES)
			{
				dictionary[Py.Lower(Py.Strip(array[0]))] = array[1];
			}
			try
			{
				foreach (string key in I18nCore.AvailableLanguages().Keys)
				{
					tYPE_ALIASES = _TYPE_ALIASES;
					foreach (string[] array2 in tYPE_ALIASES)
					{
						string text = Py.Lower(Py.Strip(Py.S(I18nCore.Tr(array2[0], key))));
						if (text.Length > 0 && !dictionary.ContainsKey(text))
						{
							dictionary[text] = array2[1];
						}
					}
				}
			}
			catch (Exception)
			{
			}
			_ALIAS_TABLE = dictionary;
			return dictionary;
		}
	}

	private static HashSet<string> _any_labels()
	{
		lock (_aliasLock)
		{
			if (_ANY_LABELS != null)
			{
				return _ANY_LABELS;
			}
			HashSet<string> hashSet = new HashSet<string> { "全部", "all", "any" };
			try
			{
				foreach (string key in I18nCore.AvailableLanguages().Keys)
				{
					string text = Py.Lower(Py.Strip(Py.S(I18nCore.Tr("全部", key))));
					if (text.Length > 0)
					{
						hashSet.Add(text);
					}
				}
			}
			catch (Exception)
			{
			}
			_ANY_LABELS = hashSet;
			return hashSet;
		}
	}

	public static void ResetAliasCache()
	{
		lock (_aliasLock)
		{
			_ALIAS_TABLE = null;
			_ANY_LABELS = null;
		}
	}

	public static string TypeKey(object label)
	{
		string text = Py.Lower(Py.Strip(Py.S(label)));
		if (text.Length == 0 || _any_labels().Contains(text))
		{
			return "";
		}
		if (!_alias_table().TryGetValue(text, out string value))
		{
			return text;
		}
		return value;
	}

	public static List<object> CategoryFacets(object label)
	{
		string text = TypeKey(label);
		if (text.Length == 0)
		{
			return new List<object>();
		}
		if (!TYPE_FACETS.TryGetValue(text, out string[] value))
		{
			return new List<object>();
		}
		return value.Cast<object>().ToList();
	}

	public static List<object> CfCategoryTokens(object label)
	{
		string text = TypeKey(label);
		if (text.Length == 0)
		{
			return new List<object>();
		}
		if (!CF_TYPE_TOKENS.TryGetValue(text, out string[] value))
		{
			return new List<object>();
		}
		return value.Cast<object>().ToList();
	}

	public static List<object> SearchProjects(DownloadManager dm, string kind, object query, object source, PyDict extra = null)
	{
        CommunityNames.Ensure();
        return CatalogSearch.Search(kind, Py.S(query), Py.S(source), extra ?? new PyDict());
	}

	private static PyDict _hit_row(PyDict h, string default_source)
	{
		var row = PyDict.Of("name", Py.Or(h["title"], h["name"], "?"), "author", Py.Or(h["author"], "?"), "downloads", Py.Int(Py.Or(h["downloads"], 0L)), "id", h["id"], "slug", h["slug"], "source", Py.Or(h["source"], default_source), "description", Py.Or(h["description"], h["summary"], ""), "tags", Py.Or(h["tags"], new List<object>()), "updated", Py.Slice(Py.Str(Py.Or(h["updated"], h["date_modified"], "")), null, 10));
        row.Update(CatalogPresentation.Artwork(h));
        row["original_description"] = Py.Or(h.Get("original_description"), h.Get("description"), Py.Or(h.Get("summary"), ""));
        return row;
	}

	public static object PickMrVersion(List<object> versions, object version_id)
	{
		if (Py.Truthy(version_id))
		{
			foreach (object item in versions ?? new List<object>())
			{
				if (Py.Str(Mods.Gt(item, "id")) == Py.Str(version_id))
				{
					return item;
				}
			}
		}
		List<object> list = versions ?? new List<object>();
		if (list.Count <= 0)
		{
			return null;
		}
		return list[0];
	}
}
