using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace HelL.Core.Launcher;

public static class Manifest
{
	public const string VERSION_MANIFEST_URL = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

	public static readonly string[] VERSION_MANIFEST_URLS = new string[2] { "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json", "https://launchermeta.mojang.com/mc/game/version_manifest_v2.json" };

	public const long MANIFEST_TTL = 14400L;

	public const long VERSION_JSON_TTL = 86400L;

	private static readonly Regex SnapRe = new Regex("^\\d{2}w\\d{2}[a-z]$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

	private static readonly Regex PlayableRe = new Regex("^(1\\.\\d+(?:\\.\\d+)?|\\d{2}\\.\\d+(?:\\.\\d+)?)(?:[-_+].+)$", RegexOptions.CultureInvariant);

	public static object GetVersionManifest(DownloadManager dm, bool force = false)
	{
		string text = PPath.Join(Utils.ROOT, "cache");
		string text2 = PPath.Join(text, "version_manifest.json");
		string path = PPath.Join(text, "version_manifest.meta");
		if (!force)
		{
			object obj = Utils.ReadJson(path);
			if (Py.Truthy(obj) && new PPath(text2).IsFile())
			{
				if (!(obj is PyDict pyDict))
				{
					throw new AttributeError("'" + Py.TypeName(obj) + "' object has no attribute 'get'");
				}
				if (Py.Time() - Py.Float(pyDict.Get("fetched_at", 0L)) < 14400.0)
				{
					object obj2 = Utils.ReadJson(text2);
					if (Py.Truthy(obj2))
					{
						return obj2;
					}
				}
			}
		}
		Exception ex = null;
		List<string> list = Source.VersionManifestUrls();
		IEnumerable<string> enumerable2;
		if (list.Count <= 0)
		{
			IEnumerable<string> enumerable = VERSION_MANIFEST_URLS.ToList();
			enumerable2 = enumerable;
		}
		else
		{
			IEnumerable<string> enumerable = list;
			enumerable2 = enumerable;
		}
		foreach (string item in enumerable2)
		{
			try
			{
				object obj3 = dm.FetchJson(item, 20L, expand: false);
				Utils.EnsureDir(text);
				Utils.WriteJson(text2, obj3);
				Utils.WriteJson(path, PyDict.Of("fetched_at", Py.Time(), "url", item));
				return obj3;
			}
			catch (Exception ex2) when (!(ex2 is TaskCancelled))
			{
				ex = ex2;
				Utils.log.Warning("拉取版本清单失败 %s: %s", item, Py.ExcStr(ex2));
			}
		}
		object obj4 = Utils.ReadJson(text2);
		if (Py.Truthy(obj4))
		{
			Utils.log.Warning("使用缓存中的旧版本清单。");
			return obj4;
		}
		throw new DownloadError("无法获取版本清单: " + ((ex == null) ? "None" : Py.ExcStr(ex)));
	}

	public static PyDict ListRemoteVersions(DownloadManager dm, bool force = false)
	{
		object versionManifest = GetVersionManifest(dm, force);
		PyDict pyDict = new PyDict();
		foreach (object item in Py.ListOr((versionManifest as PyDict)?.Get("versions", new List<object>())))
		{
			if (item is string)
			{
				throw new TypeError("string indices must be integers, not 'str'");
			}
			if (item is List<object>)
			{
				throw new TypeError("list indices must be integers or slices, not str");
			}
			if (!(item is PyDict))
			{
				throw new TypeError("'" + ((item == null) ? "NoneType" : ((item is bool) ? "bool" : ((item is long) ? "int" : ((item is double) ? "float" : "object")))) + "' object is not subscriptable");
			}
			pyDict[Py.Str(((PyDict)item).Req("id"))] = item;
		}
		return pyDict;
	}

	public static List<long> McVersionTuple(object version_id)
	{
		if (!Py.Truthy(version_id))
		{
			return null;
		}
		string text = Py.Strip(Py.Str(version_id).Split('-')[0].Split('+')[0]);
		if (SnapRe.IsMatch(text))
		{
			return null;
		}
		List<long> list = new List<long>();
		string[] array = text.Split('.');
		foreach (string text2 in array)
		{
			if (text2.Length == 0 || !text2.All(char.IsDigit))
			{
				return null;
			}
			long num = 0L;
			string text3 = text2;
			foreach (char c in text3)
			{
				num = num * 10 + (long)char.GetNumericValue(c);
			}
			list.Add(num);
		}
		if (list.Count == 0)
		{
			return null;
		}
		while (list.Count < 3)
		{
			list.Add(0L);
		}
		return list.Take(3).ToList();
	}

	public static bool LooksLikeMinecraftVersion(object version_id)
	{
		if (!Py.Truthy(version_id))
		{
			return false;
		}
		string text = Py.Strip(Py.Str(version_id));
		if (SnapRe.IsMatch(text))
		{
			return true;
		}
		List<long> list = McVersionTuple(text);
		if (list == null)
		{
			return false;
		}
		if (3 <= list[0] && list[0] <= 19)
		{
			return false;
		}
		if (list[0] != 1)
		{
			return list[0] >= 20;
		}
		return true;
	}

	private static int CmpTuple(List<long> a, List<long> b)
	{
		for (int i = 0; i < 3; i++)
		{
			int num = a[i].CompareTo(b[i]);
			if (num != 0)
			{
				return num;
			}
		}
		return 0;
	}

	public static string ClosestRelease(PyDict versions, string declared)
	{
		List<long> want = McVersionTuple(declared);
		if (want == null)
		{
			return null;
		}
		List<KeyValuePair<List<long>, string>> list = new List<KeyValuePair<List<long>, string>>();
		foreach (KeyValuePair<string, object> item in versions ?? new PyDict())
		{
			if (item.Value is PyDict pyDict)
			{
				object obj = pyDict.Get("type");
				if (obj != null && !object.Equals(obj, "release"))
				{
					continue;
				}
			}
			List<long> list2 = McVersionTuple(item.Key);
			if (list2 != null && list2[0] == want[0] && list2[1] == want[1])
			{
				list.Add(new KeyValuePair<List<long>, string>(list2, item.Key));
			}
		}
		if (list.Count == 0)
		{
			return null;
		}
		list.Sort(delegate(KeyValuePair<List<long>, string> x, KeyValuePair<List<long>, string> y)
		{
			int num = CmpTuple(x.Key, y.Key);
			return (num != 0) ? num : string.CompareOrdinal(x.Value, y.Value);
		});
		List<KeyValuePair<List<long>, string>> list3 = list.Where<KeyValuePair<List<long>, string>>((KeyValuePair<List<long>, string> x) => CmpTuple(x.Key, want) <= 0).ToList();
		if (list3.Count > 0)
		{
			return list3[list3.Count - 1].Value;
		}
		return list[0].Value;
	}

	public static string ResolvePlayableVersion(DownloadManager dm, object version_id)
	{
		return ResolvePlayableVersionIn(null, dm, version_id);
	}

	internal static string ResolvePlayableVersionIn(PyDict versions, DownloadManager dm, object version_id)
	{
		if (!Py.Truthy(version_id))
		{
			return null;
		}
		string text = Py.Strip(Py.Str(version_id));
		versions = versions ?? ListRemoteVersions(dm);
		if (versions.ContainsKey(text))
		{
			return text;
		}
		Match match = PlayableRe.Match(text);
		if (match.Success)
		{
			string value = match.Groups[1].Value;
			if (versions.ContainsKey(value))
			{
				return value;
			}
			text = value;
		}
		if (versions.ContainsKey(text))
		{
			return text;
		}
		if (!LooksLikeMinecraftVersion(text))
		{
			return null;
		}
		return ClosestRelease(versions, text);
	}

	public static PyDict GetVersionJson(DownloadManager dm, string version_id, bool force = false)
	{
		string text = PPath.Join(Utils.ROOT, "cache", "versions");
		string path = PPath.Join(text, version_id + ".json");
		if (!force)
		{
			object obj = Utils.ReadJson(path);
			if (Py.Truthy(obj) && obj is PyDict pyDict && Py.Time() - Py.Float(pyDict.Get("__tipcl_cached_at", 0L)) < 86400.0)
			{
				FileMirrors.RegisterVersion(pyDict);
				return pyDict;
			}
		}
		object obj2 = ListRemoteVersions(dm).Get(version_id);
		if (!Py.Truthy(obj2))
		{
			throw new VersionNotFound("找不到版本 " + version_id + "，它可能不在官方版本列表中（例如整合包自定义版本）。");
		}
		PyDict pyDict2 = (PyDict)dm.FetchJson(Py.Str(((PyDict)obj2).Req("url")), 60L);
		FileMirrors.RegisterVersion(pyDict2);
		pyDict2["__tipcl_cached_at"] = Py.Time();
		Utils.EnsureDir(text);
		Utils.WriteJson(path, pyDict2);
		return pyDict2;
	}

	public static PyDict GetVersionJsonFrom(DownloadManager dm, string url, string version_id)
	{
		string text = PPath.Join(Utils.ROOT, "cache", "versions");
		string path = PPath.Join(text, version_id + ".json");
		PyDict pyDict = (PyDict)dm.FetchJson(url, 60L);
		FileMirrors.RegisterVersion(pyDict);
		pyDict["__tipcl_cached_at"] = Py.Time();
		Utils.EnsureDir(text);
		Utils.WriteJson(path, pyDict);
		return pyDict;
	}

	public static string LibraryIdentity(PyDict lib)
	{
		string text = (Py.Truthy(lib.Get("name")) ? Py.Str(lib.Get("name")) : "");
		string[] array = text.Split(':');
		string text2 = ((array.Length != 0) ? array[0] : "");
		string text3 = ((array.Length > 1) ? array[1] : text);
		string text4 = ((array.Length > 3) ? array[3] : null);
		if (Py.Truthy(lib.Get("natives")))
		{
			return text2 + ":" + text3 + ":natives";
		}
		if (!string.IsNullOrEmpty(text4))
		{
			return text2 + ":" + text3 + ":" + text4;
		}
		return text2 + ":" + text3;
	}

	public static List<object> MergeLibraries(object parent_libs, object child_libs)
	{
		List<object> list = new List<object>();
		Dictionary<string, int> dictionary = new Dictionary<string, int>();
		object obj = (Py.Truthy(parent_libs) ? ((IEnumerable<object>)parent_libs) : new List<object>());
		foreach (object item in (IEnumerable<object>)obj)
		{
			string key = LibraryIdentity((PyDict)item);
			dictionary[key] = list.Count;
			list.Add(item);
		}
		object obj2 = (Py.Truthy(child_libs) ? ((IEnumerable<object>)child_libs) : new List<object>());
		foreach (object item2 in (IEnumerable<object>)obj2)
		{
			string key2 = LibraryIdentity((PyDict)item2);
			if (dictionary.TryGetValue(key2, out var value))
			{
				list[value] = item2;
				continue;
			}
			dictionary[key2] = list.Count;
			list.Add(item2);
		}
		return list;
	}

	private static List<object> L(object x)
	{
		if (!Py.Truthy(x))
		{
			return new List<object>();
		}
		return new List<object>((IEnumerable<object>)x);
	}

	public static PyDict Merge(PyDict b, PyDict child)
	{
		PyDict pyDict = new PyDict(b);
		foreach (KeyValuePair<string, object> item in child)
		{
			if (item.Key != "inheritsFrom")
			{
				pyDict[item.Key] = item.Value;
			}
		}
		pyDict["libraries"] = MergeLibraries(b.Get("libraries"), child.Get("libraries"));
		if (b.Get("arguments") is PyDict pyDict2 && child.Get("arguments") is PyDict pyDict3)
		{
			List<object> list = L(pyDict2.Get("game"));
			list.AddRange(L(pyDict3.Get("game")));
			List<object> list2 = L(pyDict2.Get("jvm"));
			list2.AddRange(L(pyDict3.Get("jvm")));
			pyDict["arguments"] = PyDict.Of("game", list, "jvm", list2);
		}
		else if (child.ContainsKey("minecraftArguments") && !child.ContainsKey("arguments"))
		{
			pyDict["minecraftArguments"] = child["minecraftArguments"];
			pyDict.Pop("arguments");
		}
		PyDict pyDict4 = (Py.Truthy(b.Get("downloads")) ? new PyDict((PyDict)b.Get("downloads")) : new PyDict());
		if (Py.Truthy(child.Get("downloads")))
		{
			pyDict4.Update((PyDict)child.Get("downloads"));
		}
		if (pyDict4.Count > 0)
		{
			pyDict["downloads"] = pyDict4;
		}
		string[] array = new string[8] { "assetIndex", "assets", "javaVersion", "mainClass", "logging", "type", "releaseTime", "time" };
		foreach (string key in array)
		{
			if (child.Get(key) != null)
			{
				pyDict[key] = child[key];
			}
		}
		return pyDict;
	}

	public static PyDict ResolveInherits(PyDict version_json, Func<string, PyDict> load_parent)
	{
		PyDict pyDict = new PyDict(version_json);
		HashSet<string> hashSet = new HashSet<string>();
		while (Py.Truthy(pyDict.Get("inheritsFrom")))
		{
			string text = Py.Str(pyDict["inheritsFrom"]);
			if (hashSet.Contains(text))
			{
				throw new DownloadError("版本继承链出现循环: " + text);
			}
			hashSet.Add(text);
			PyDict pyDict2 = load_parent(text);
			if (!Py.Truthy(pyDict2))
			{
				throw new DownloadError("缺少被继承的父版本 " + text + "，请先安装该版本。");
			}
			pyDict = Merge(pyDict2, pyDict);
		}
		return pyDict;
	}

	public static bool IsLegacyVersion(PyDict version_json)
	{
		if (object.Equals(version_json.Get("assets"), "pre-1.6"))
		{
			return true;
		}
		if (!Py.Truthy(version_json.Get("assetIndex")) && !Py.Truthy(version_json.Get("assets")) && !version_json.ContainsKey("arguments"))
		{
			return true;
		}
		return false;
	}

	public static bool IsVanillaId(string version_id)
	{
		if (version_id == "release" || version_id == "snapshot")
		{
			return false;
		}
		if (version_id.Contains("-"))
		{
			return new string[7] { "1.", "0.", "c0.", "inf-", "rd-", "a1.", "b1." }.Any((string p) => version_id.StartsWith(p, StringComparison.Ordinal));
		}
		return true;
	}
}
