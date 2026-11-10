using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace HelL.Core.Launcher;

public static class Mods
{
	public const string MODRINTH_API = "https://api.modrinth.com/v2";

	public const string CF_OFFICIAL_API = "https://api.curseforge.com/v1";

	public const string CF_WEB_API = "https://www.curseforge.com/api/v1/mods";

	public static readonly List<object> API_TIMEOUT = Py.L(3, 8);

	public const long CF_CLASS_MOD = 6L;

	public const long CF_CLASS_MODPACK = 4471L;

	public const long CF_CLASS_RESOURCEPACK = 12L;

	public const long CF_CLASS_SHADER = 6552L;

	public const long CF_CLASS_DATAPACK = 6945L;

	public const long CF_CLASS_WORLD = 17L;

	public const long CF_MAX_PAGE_SIZE = 50L;

	public const long CF_REL_REQUIRED = 3L;

	public const int CF_DEP_MAX_DEPTH = 3;

	private static readonly Regex _CF_RE = new Regex("curseforge\\.com/minecraft/(mc-mods|modpacks)/[^/]+/files/(\\d+)");

	private static readonly Regex _CF_SLUG_RE = new Regex("curseforge\\.com/minecraft/(mc-mods|modpacks)/([^/?#]+)");

	private static readonly Regex HttpPrefix = new Regex("^https?://");

	private static readonly Dictionary<string, Dictionary<string, double>> PLATFORM_WEIGHT = new Dictionary<string, Dictionary<string, double>>
	{
		{
			"mod",
			new Dictionary<string, double>
			{
				{ "curseforge", 1.0 },
				{ "modrinth", 5.0 }
			}
		},
		{
			"modpack",
			new Dictionary<string, double>
			{
				{ "curseforge", 1.0 },
				{ "modrinth", 5.0 }
			}
		},
		{
			"datapack",
			new Dictionary<string, double>
			{
				{ "curseforge", 10.0 },
				{ "modrinth", 1.0 }
			}
		},
		{
			"resourcepack",
			new Dictionary<string, double>
			{
				{ "curseforge", 1.0 },
				{ "modrinth", 4.0 }
			}
		},
		{
			"shader",
			new Dictionary<string, double>
			{
				{ "curseforge", 1.0 },
				{ "modrinth", 4.0 }
			}
		}
	};

	private const double POPULARITY_DIVISOR = 9.0;

	private const double ALIAS_BONUS = 0.2;

	private const double DESCRIPTION_WEIGHT = 0.05;

	public static readonly Dictionary<string, PyDict> CONTENT_KINDS = new Dictionary<string, PyDict>
	{
		{
			"shader",
			PyDict.Of("mr", "shader", "subdir", "shaderpacks", "cf", 6552L)
		},
		{
			"resourcepack",
			PyDict.Of("mr", "resourcepack", "subdir", "resourcepacks", "cf", 12L)
		},
		{
			"datapack",
			PyDict.Of("mr", "datapack", "subdir", "datapacks", "cf", 6945L)
		}
	};

	public static List<string> ModrinthDownloadUrls(object urls)
	{
		return Source.ModrinthFileUrls(urls);
	}

	public static string MirrorModrinthUrl(string url)
	{
		return Source.RewriteToMcim(url) ?? url;
	}

	public static string WithParams(string url, PyDict ps)
	{
		if (ps == null || ps.Count == 0)
		{
			return url;
		}
		StringBuilder stringBuilder = new StringBuilder();
		foreach (KeyValuePair<string, object> p in ps)
		{
			if (p.Value != null)
			{
				if (stringBuilder.Length > 0)
				{
					stringBuilder.Append('&');
				}
				stringBuilder.Append(QuotePlus(p.Key)).Append('=').Append(QuotePlus(Py.Str(p.Value)));
			}
		}
		if (stringBuilder.Length == 0)
		{
			return url;
		}
		return url + (url.Contains("?") ? "&" : "?") + stringBuilder;
	}

	private static string QuotePlus(string s)
	{
		StringBuilder stringBuilder = new StringBuilder();
		byte[] bytes = Encoding.UTF8.GetBytes(s);
		for (int i = 0; i < bytes.Length; i++)
		{
			byte b = bytes[i];
			char c = (char)b;
			if ((c < 'a' || c > 'z') && (c < 'A' || c > 'Z') && (c < '0' || c > '9'))
			{
				switch (c)
				{
				case ' ':
					stringBuilder.Append('+');
					continue;
				default:
					stringBuilder.Append('%').Append(b.ToString("X2"));
					continue;
				case '-':
				case '.':
				case '_':
				case '~':
					break;
				}
			}
			stringBuilder.Append(c);
		}
		return stringBuilder.ToString();
	}

	public static Dictionary<string, string> CfApiHeaders(string api_key = null)
	{
		Dictionary<string, string> dictionary = new Dictionary<string, string>
		{
			{ "Accept", "application/json" },
			{ "User-Agent", AppInfo.CLIENT_HEADER }
		};
		if (!string.IsNullOrEmpty(api_key))
		{
			dictionary["x-api-key"] = api_key;
		}
		return dictionary;
	}

	public static string CfFileCdnUrl(long file_id, string filename, string host = "edge.forgecdn.net")
	{
		string text = Skin.Quote(filename, "._-+()[]");
		return "https://" + host + "/files/" + Py.Str(file_id / 1000) + "/" + Py.Str(file_id % 1000) + "/" + text;
	}

	public static List<object> CfModDownloadUrls(object addon_id, object file_id, object filename = null, object download_url = null)
	{
		List<string> list = new List<string>();
		if (Py.Truthy(download_url))
		{
			list.Add(Py.Str(download_url));
		}
		if (Py.Truthy(filename))
		{
			string[] array = new string[2] { "mediafilez.forgecdn.net", "edge.forgecdn.net" };
			foreach (string host in array)
			{
				string text = CfFileCdnUrl(Py.Int(file_id), Py.Str(filename), host);
				list.Add(text);
				string text2 = Source.RewriteToMcim(text);
				if (!string.IsNullOrEmpty(text2))
				{
					list.Add(text2);
				}
			}
		}
		string text3 = Py.Str(addon_id);
		string text4 = Py.Str(file_id);
		list.Add("https://api.curseforge.com/v1/mods/" + text3 + "/files/" + text4 + "/download");
		list.Add("https://mod.mcimirror.top/curseforge/v1/mods/" + text3 + "/files/" + text4 + "/download");
		list.Add("https://www.curseforge.com/api/v1/mods/" + text3 + "/files/" + text4 + "/download");
		HashSet<string> hashSet = new HashSet<string>();
		List<object> list2 = new List<object>();
		foreach (string item in list)
		{
			if (!string.IsNullOrEmpty(item) && hashSet.Add(item))
			{
				list2.Add(item);
			}
		}
		return list2;
	}

	public static List<object> CfItems(object data)
	{
		if (data is List<object> result)
		{
			return result;
		}
		if (data is PyDict pyDict && pyDict["data"] is List<object> result2)
		{
			return result2;
		}
		return new List<object>();
	}

	public static object CfNormalizeFile(object file_obj)
	{
		if (!(file_obj is PyDict pyDict))
		{
			return file_obj;
		}
		PyDict pyDict2 = new PyDict();
		foreach (KeyValuePair<string, object> item in pyDict)
		{
			pyDict2[item.Key] = item.Value;
		}
		object o = pyDict2["id"];
		object o2 = pyDict2["fileName"];
		if (!Py.Truthy(pyDict2["downloadUrl"]) && Py.Truthy(o) && Py.Truthy(o2))
		{
			pyDict2["downloadUrl"] = CfFileCdnUrl(Py.Int(o), Py.Str(o2));
		}
		return pyDict2;
	}

	public static object CfFetch(DownloadManager dm, string path, string api_key = null, PyDict ps = null, object timeout = null)
	{
		if (string.IsNullOrEmpty(api_key))
		{
			api_key = Config.CurseforgeKey();
		}
		object e = null;
		Dictionary<string, string> headers = CfApiHeaders(api_key);
		foreach (string item in Source.CfApiBases())
		{
			string text = item + path;
			try
			{
				object obj = dm.FetchJson(WithParams(text, ps), timeout ?? API_TIMEOUT, expand: false, headers);
				if (obj == null)
				{
					e = text + " 返回空响应";
					continue;
				}
				return obj;
			}
			catch (Exception ex)
			{
				e = ex;
				Utils.log.Warning("CurseForge 端点不可用 %s: %s", text, Py.ExcStr(ex));
			}
		}
		throw new ModError("CurseForge 请求失败 " + path + ": " + ErrStr(e));
	}

	private static string ErrStr(object e)
	{
		if (!(e is Exception e2))
		{
			return Py.Str(e);
		}
		return Py.ExcStr(e2);
	}

	public static object CfPost(DownloadManager dm, string path, object body, string api_key = null, object timeout = null)
	{
		timeout = timeout ?? ((object)60L);
		if (string.IsNullOrEmpty(api_key))
		{
			api_key = Config.CurseforgeKey();
		}
		object e = null;
		Dictionary<string, string> dictionary = CfApiHeaders(api_key);
		dictionary["Content-Type"] = "application/json";
		double[] array = Downloader.RequestTimeout(Py.Truthy(timeout) ? timeout : API_TIMEOUT);
		foreach (string item in Source.CfApiBases())
		{
			string text = item + path;
			try
			{
				Net.RequestOptions o = new Net.RequestOptions
				{
					Method = "POST",
					Headers = dictionary,
					ConnectTimeout = array[0],
					ReadTimeout = array[1],
					Body = Encoding.UTF8.GetBytes(PyJson.Dumps(body)),
					ContentType = "application/json",
					Token = dm.Token
				};
				using NetResponse netResponse = Net.Request(text, o);
				netResponse.RaiseForStatus();
				object obj = netResponse.Json();
				if (obj == null)
				{
					e = text + " 返回空响应";
					continue;
				}
				return obj;
			}
			catch (Exception ex)
			{
				e = ex;
				Utils.log.Warning("CurseForge POST 不可用 %s: %s", text, Py.ExcStr(ex));
			}
		}
		throw new ModError("CurseForge POST 失败 " + path + ": " + ErrStr(e));
	}

	public static Dictionary<long, PyDict> CfFilesByIds(DownloadManager dm, IEnumerable<object> file_ids, string api_key = null)
	{
		List<long> list = new List<long>();
		foreach (object item in file_ids ?? new List<object>())
		{
			try
			{
				list.Add(Py.Int(item));
			}
			catch (Exception)
			{
			}
		}
		Dictionary<long, PyDict> dictionary = new Dictionary<long, PyDict>();
		if (list.Count == 0)
		{
			return dictionary;
		}
		for (int i = 0; i < list.Count; i += 50)
		{
			List<object> list2 = list.Skip(i).Take(50).Select((Func<long, object>)((long v) => v))
				.ToList();
			object data;
			try
			{
				data = CfPost(dm, "/mods/files", PyDict.Of("fileIds", list2), api_key);
			}
			catch (Exception e)
			{
				Utils.log.Warning("批量查询 CurseForge 文件失败: %s", Py.ExcStr(e));
				continue;
			}
			foreach (object item2 in CfItems(data))
			{
				if (CfNormalizeFile(item2) is PyDict pyDict)
				{
					object obj = pyDict["id"];
					if (obj != null)
					{
						dictionary[Py.Int(obj)] = pyDict;
					}
				}
			}
		}
		return dictionary;
	}

	public static PyDict CfDetail(DownloadManager dm, object addon_id, string api_key = null)
	{
		object obj = CfFetch(dm, "/mods/" + Py.Str(addon_id), api_key);
		if (((obj is PyDict pyDict2) ? pyDict2["data"] : obj) is PyDict pyDict && Py.Truthy(pyDict) && Py.Truthy(pyDict["id"]))
		{
			return pyDict;
		}
		throw new ModError("获取 CurseForge 详情失败: 项目 " + Py.Str(addon_id) + " 无有效数据");
	}

	public static List<object> CfFiles(DownloadManager dm, object addon_id, string api_key = null, object game_version = null, object mod_loader = null, long page_size = 50L)
	{
		if (string.IsNullOrEmpty(api_key))
		{
			api_key = Config.CurseforgeKey();
		}
		PyDict pyDict = PyDict.Of("pageSize", page_size, "index", 0L);
		if (Py.Truthy(game_version))
		{
			pyDict["gameVersion"] = game_version;
		}
		if (Py.Truthy(mod_loader))
		{
			pyDict["modLoaderType"] = mod_loader;
		}
		return CatalogVersions.CurseForge(dm, Py.Str(addon_id), pyDict, CfApiHeaders(api_key)).Select(CfNormalizeFile).ToList();
	}

	public static List<string> ListInstanceMods(Instance instance)
	{
		return (from PyDict r in ListModEntriesAt(PPath.Join(instance.path, "mods"))
			where Py.Truthy(r["enabled"])
			select (string)r["path"]).ToList();
	}

	public static List<object> ListInstanceModEntries(Instance instance)
	{
		return ListModEntriesAt(PPath.Join(instance.path, "mods"));
	}

	public static List<object> ListModEntriesAt(string mods_dir)
	{
		List<object> list = new List<object>();
		if (!LocalFs.IsDir(mods_dir))
		{
			return list;
		}
		foreach (string item in LocalFs.SortedChildren(mods_dir))
		{
			if (LocalFs.IsFile(item))
			{
				string name = new PPath(item).Name;
				string text = Py.Lower(name);
				if (text.EndsWith(".jar"))
				{
					list.Add(PyDict.Of("filename", name, "enabled", true, "bytes", LocalFs.Size(item), "path", item));
				}
				else if (text.EndsWith(".jar.disabled") || text.EndsWith(".disabled"))
				{
					list.Add(PyDict.Of("filename", name, "enabled", false, "bytes", LocalFs.Size(item), "path", item));
				}
			}
		}
		return list;
	}

	public static string _mod_file_at(string mods_dir, object filename)
	{
		PPath pPath = new PPath(mods_dir);
		string text = Py.SS(filename);
		string text2 = (Utils.LooksLikePathSpelling(text) ? Utils.NormPathSpelling(text) : text);
		if (text2.Length == 0 || text2 == "." || text2 == ".." || text2.Contains("/") || text2.Contains("\\") || text2.Contains(":"))
		{
			throw new ModError(Py.Fmt("非法模组路径: {0}", filename));
		}
		PPath other;
		PPath pPath2;
		try
		{
			other = pPath.Resolve();
			pPath2 = (pPath / text2).Resolve();
		}
		catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is NotSupportedException)
		{
			throw new ModError(Py.Fmt("非法模组路径: {0}", filename));
		}
		if (!pPath2.Parent.Equals(other))
		{
			throw new ModError(Py.Fmt("非法模组路径: {0}", filename));
		}
		return (pPath / text2).ToString();
	}

	public static string _download_dest(string folder, object filename)
	{
		string text = Py.SS(filename);
		if (text.Length == 0)
		{
			throw new ModError("远端未返回文件名");
		}
		return _mod_file_at(folder, text);
	}

	public static void DeleteMod(Instance instance, object filename, string mods_dir = null)
	{
		string text = _mod_file_at(Py.Truthy(mods_dir) ? mods_dir : PPath.Join(instance.path, "mods"), filename);
		if (!LocalFs.IsFile(text))
		{
			throw new ModError(Py.Fmt("模组文件不存在: {0}", filename));
		}
		File.Delete(PPath.Abs(text));
	}

	public static string SetModEnabled(Instance instance, object filename, bool enabled, string mods_dir = null)
	{
		string mods_dir2 = (Py.Truthy(mods_dir) ? mods_dir : PPath.Join(instance.path, "mods"));
		PPath pPath = new PPath(_mod_file_at(mods_dir2, filename));
		if (!pPath.IsFile())
		{
			string text = Py.AsStr(filename, "lower");
			string filename2 = text + (Py.Lower(text).EndsWith(".disabled") ? "" : ".disabled");
			PPath pPath2 = new PPath(_mod_file_at(mods_dir2, filename2));
			if (!enabled || !pPath2.IsFile())
			{
				throw new ModError(Py.Fmt("模组文件不存在: {0}", filename));
			}
			pPath = pPath2;
		}
		string name = pPath.Name;
		if (enabled)
		{
			if (Py.Lower(name).EndsWith(".jar.disabled") || Py.Lower(name).EndsWith(".disabled"))
			{
				PPath pPath3 = pPath.WithName(name.Substring(0, name.Length - ".disabled".Length));
				if (pPath3.Exists())
				{
					throw new ModError(Py.Fmt("启用失败，已存在: {0}", pPath3.Name));
				}
				File.Move(pPath.Abs(), pPath3.Abs());
				return pPath3.Name;
			}
			return name;
		}
		if (Py.Lower(name).EndsWith(".disabled"))
		{
			return name;
		}
		PPath pPath4 = pPath.WithName(name + ".disabled");
		if (pPath4.Exists())
		{
			throw new ModError(Py.Fmt("禁用失败，已存在: {0}", pPath4.Name));
		}
		File.Move(pPath.Abs(), pPath4.Abs());
		return pPath4.Name;
	}

	public static List<string> ListContentFiles(Instance instance, string subdir)
	{
		string text = PPath.Join(instance.path, subdir);
		if (!LocalFs.IsDir(text))
		{
			return new List<string>();
		}
		return (from p in LocalFs.Children(text)
			where LocalFs.IsFile(p) && (Py.Lower(new PPath(p).Suffix) == ".zip" || Py.Lower(new PPath(p).Suffix) == ".jar")
			select p).OrderBy<string, string>((string p) => Py.Lower(new PPath(p).Name), StringComparer.Ordinal).ToList();
	}

	public static void DeleteContentFile(Instance instance, string subdir, object filename)
	{
		PPath pPath = new PPath(PPath.Join(instance.path, subdir)).Resolve();
		PPath pPath2 = (pPath / Py.AsStr(filename, "__fspath__")).Resolve();
		if (!pPath2.Parent.Equals(pPath))
		{
			throw new ModError(Py.Fmt("非法路径: {0}", filename));
		}
		if (pPath2.IsFile())
		{
			File.Delete(pPath2.Abs());
		}
	}

	public static string DetectLoader(Instance instance)
	{
		foreach (string item in instance.InstalledIds())
		{
			string text = Py.Lower(item);
			if (text.Contains("fabric"))
			{
				return "fabric";
			}
			if (text.Contains("quilt"))
			{
				return "quilt";
			}
			if (text.Contains("neoforge"))
			{
				return "neoforge";
			}
			if (text.Contains("forge"))
			{
				return "forge";
			}
		}
		return null;
	}

	internal static string _mc_version_from_text(object text)
	{
		if (!Py.Truthy(text))
		{
			return null;
		}
		string text2 = Py.Strip(Py.Str(text));
		if (Regex.IsMatch(text2, "^(?:1\\.\\d+(\\.\\d+)?|2\\d\\.\\d+(\\.\\d+)?)\\z"))
		{
			return text2;
		}
		Match match = Regex.Match(text2, "(?<![\\d.])(1\\.\\d+(?:\\.\\d+)?)(?![\\d])");
		if (match.Success)
		{
			return match.Groups[1].Value;
		}
		// 26.1 起的年份版本号（26.3、26.1.2）；NeoForge 四段号 26.3.0.22 取前三段并去掉 .0
		match = Regex.Match(text2, "(?<![\\d.])(2\\d\\.\\d+(?:\\.\\d+)?)(?![\\d])");
		if (!match.Success)
		{
			match = Regex.Match(text2, "(?<![\\d.])(2\\d\\.\\d+\\.\\d+)\\.\\d+");
		}
		if (!match.Success)
		{
			return null;
		}
		string v = match.Groups[1].Value;
		return v.EndsWith(".0") && v.Split('.').Length == 3 ? v.Substring(0, v.Length - 2) : v;
	}

	public static string DetectMcVersion(Instance instance)
	{
		PyDict pyDict = Py.DictOr(Py.Or(instance.Meta(), new PyDict()));
		PyDict pyDict2 = (pyDict["modpack"] as PyDict) ?? new PyDict();
		object[] array = new object[2]
		{
			pyDict2["mc_version"],
			pyDict["mc_version"]
		};
		for (int i = 0; i < array.Length; i++)
		{
			string text = _mc_version_from_text(array[i]);
			if (text != null)
			{
				return text;
			}
		}
		List<KeyValuePair<List<long>, string>> list = new List<KeyValuePair<List<long>, string>>();
		foreach (object item in instance.InstalledIds())
		{
			string text2 = _mc_version_from_text(item);
			if (text2 != null)
			{
				list.Add(new KeyValuePair<List<long>, string>((from x in text2.Split('.')
					select Py.Int(x)).ToList(), text2));
			}
		}
		if (list.Count == 0)
		{
			return null;
		}
		list.Sort(delegate(KeyValuePair<List<long>, string> a, KeyValuePair<List<long>, string> b)
		{
			for (int j = 0; j < Math.Min(a.Key.Count, b.Key.Count); j++)
			{
				int num = a.Key[j].CompareTo(b.Key[j]);
				if (num != 0)
				{
					return num;
				}
			}
			int num2 = a.Key.Count.CompareTo(b.Key.Count);
			return (num2 != 0) ? num2 : string.CompareOrdinal(a.Value, b.Value);
		});
		return list[list.Count - 1].Value;
	}

	private static long? SizeOf(object x)
	{
		if (x != null)
		{
			return Py.Int(x);
		}
		return null;
	}

	private static string StrOrNull(object x)
	{
		if (x != null)
		{
			return Py.Str(x);
		}
		return null;
	}

	public static string _resolve_mods_dir(Instance instance, string mods_dir = null)
	{
		string obj = (Py.Truthy(mods_dir) ? PPath.Norm(mods_dir) : PPath.Join(instance.path, "mods"));
		Utils.EnsureDir(obj);
		return obj;
	}

	private static PyDict _pick_version(DownloadManager dm, object slug, object mc_version, string loader)
	{
		List<object> list = ListVersions(dm, slug, mc_version, (loader != null && loader.Length > 0) ? Py.L(loader) : null);
		if (list.Count == 0 && Py.Truthy(mc_version))
		{
			Utils.log.Warning("%s 没有与 MC %s + %s 完全匹配的版本，尝试仅按 MC 版本匹配", Py.Str(slug), Py.Str(mc_version), Py.Truthy(loader) ? loader : "任意加载器");
			list = ListVersions(dm, slug, mc_version);
		}
		if (list.Count == 0)
		{
			throw new ModError("模组 " + Py.Str(slug) + " 没有支持 MC " + Py.Str(mc_version) + " 的版本");
		}
		if (Py.Truthy(loader))
		{
			foreach (object item in list)
			{
				if (Py.In(loader, Iter(Gd(item, "loaders", new List<object>()))))
				{
					return (PyDict)item;
				}
			}
			Utils.log.Warning("%s 的最新版本未声明支持 %s，可能不兼容", Py.Str(slug), loader);
		}
		return (PyDict)list[0];
	}

	public static PyDict InstallModrinthMod(DownloadManager dm, object slug, Instance instance, object mc_version = null, string loader = null, Action<string, long, long> on_progress = null, bool use_mirror = true, object version_id = null, string mods_dir = null, bool loaderGiven = false)
	{
		instance.EnsureStandardDirs();
		string dest_dir = _resolve_mods_dir(instance, mods_dir);
		if (!Py.Truthy(mc_version))
		{
			mc_version = DetectMcVersion(instance);
		}
		if (!loaderGiven && loader == null)
		{
			loader = DetectLoader(instance);
		}
		object obj;
		if (Py.Truthy(version_id))
		{
			try
			{
				obj = dm.FetchJson("https://api.modrinth.com/v2/version/" + Py.Str(version_id), API_TIMEOUT);
			}
			catch (Exception e)
			{
				throw new ModError("获取模组版本 " + Py.Str(version_id) + " 失败: " + Py.ExcStr(e));
			}
			if (!(obj is PyDict))
			{
				throw new ModError("模组版本 " + Py.Str(version_id) + " 无效");
			}
		}
		else
		{
			obj = _pick_version(dm, slug, mc_version, loader);
		}
		HashSet<string> seen = new HashSet<string>();
		List<string> downloaded = new List<string>();
		Download(obj, 0);
		if (downloaded.Count == 0)
		{
			throw new ModError("模组 " + Py.Str(slug) + " 下载失败");
		}
		return PyDict.Of("slug", slug, "version", Gt(obj, "version_number"), "files", ((IEnumerable<string>)downloaded).Select((Func<string, object>)((string p) => new PPath(p).Name)).ToList());
		void Download(object v, int depth)
		{
			object obj2 = Gt(v, "id");
			if (Py.Truthy(obj2) && !seen.Contains(Py.HashKey(obj2)) && depth <= 3)
			{
				seen.Add(Py.HashKey(obj2));
				object o = _primary_file(v);
				if (Py.Truthy(o) && Py.Truthy(Gt(o, "url")))
				{
					string text = _download_dest(dest_dir, Gt(o, "filename"));
					on_progress?.Invoke("下载模组 " + new PPath(text).Name, 0L, 1L);
					string text2 = Py.Str(Gt(o, "url"));
					List<string> list = (use_mirror ? ModrinthDownloadUrls(text2) : new List<string> { text2 });
					CatalogDownload.Modrinth(dm, list.Cast<object>(), text, o);
					downloaded.Add(text);
					foreach (object item in Iter(Py.Or(Gt(v, "dependencies"), new List<object>())))
					{
						if (Py.Eq(Gt(item, "dependency_type"), "required"))
						{
							object obj3 = Gt(item, "version_id");
							if (Py.Truthy(obj3))
							{
								try
								{
									Download(dm.FetchJson("https://api.modrinth.com/v2/version/" + Py.Str(obj3), 60L), depth + 1);
								}
								catch (Exception e2)
								{
									Utils.log.Warning("下载依赖 %s 失败: %s", Py.Str(Py.Or(Gt(item, "file_name"), obj3)), Py.ExcStr(e2));
								}
							}
						}
					}
				}
			}
		}
	}

	private static bool LoaderIn(object f, string loader)
	{
		string low = Py.Lower(loader);
		return Iter(Py.Or(Gt(f, "gameVersions"), new List<object>())).Any((object gv) => Py.Lower(Py.S(gv)).Contains(low));
	}

	private static bool McIn(object f, object mc_version)
	{
		if (Py.Truthy(mc_version))
		{
			return Py.In(mc_version, Iter(Py.Or(Gt(f, "gameVersions"), new List<object>())));
		}
		return true;
	}

	public static PyDict InstallCurseforgeMod(DownloadManager dm, object addon_id, Instance instance, object mc_version = null, string loader = null, string api_key = null, Action<string, long, long> on_progress = null, object file_id = null, string mods_dir = null)
	{
		instance.EnsureStandardDirs();
		string text = _resolve_mods_dir(instance, mods_dir);
		if (!Py.Truthy(mc_version))
		{
			mc_version = DetectMcVersion(instance);
		}
		if (loader == null)
		{
			loader = DetectLoader(instance);
		}
		PyDict pyDict = CfDetail(dm, addon_id, api_key);
		List<object> list = Iter(Py.Or(pyDict["latestFiles"], new List<object>()));
		if (list.Count == 0)
		{
			try
			{
				list = CfFiles(dm, addon_id, api_key, null, null, 100L);
			}
			catch (Exception e)
			{
				Utils.log.Warning("cf_files 兜底也失败: %s", Py.ExcStr(e));
			}
		}
		object obj2;
		if (Py.Truthy(file_id))
		{
			object obj = list.FirstOrDefault((object x) => Py.Str(Gt(x, "id")) == Py.Str(file_id));
			if (obj == null)
			{
				try
				{
					list = CfFiles(dm, addon_id, api_key, null, null, 100L);
				}
				catch (Exception)
				{
				}
				obj = list.FirstOrDefault((object x) => Py.Str(Gt(x, "id")) == Py.Str(file_id));
			}
			if (obj == null)
			{
				throw new ModError("找不到 CurseForge 文件 " + Py.Str(file_id));
			}
			obj2 = obj;
		}
		else
		{
			if (list.Count == 0)
			{
				throw new ModError("该模组没有可下载的文件");
			}
			List<object> list2 = list.Where((object x) => McIn(x, mc_version)).ToList();
			if (Py.Truthy(loader))
			{
				List<object> list3 = list2.Where((object x) => LoaderIn(x, loader)).ToList();
				if (list3.Count > 0)
				{
					list2 = list3;
				}
			}
			if (list2.Count == 0)
			{
				Utils.log.Warning("没有与 MC %s 完全匹配的文件，使用最新文件", Py.Str(mc_version));
				list2 = list;
			}
			obj2 = list2[0];
		}
		file_id = Gt(obj2, "id");
		if (file_id == null)
		{
			throw new ModError("模组文件信息缺失");
		}
		string filename = Py.Str(Py.Or(Gt(obj2, "fileName"), "mod-" + Py.Str(addon_id) + "-" + Py.Str(file_id) + ".jar"));
		object download_url = Gt(obj2, "downloadUrl");
		string text2 = _download_dest(text, filename);
		_download_cf_file(dm, addon_id, file_id, filename, download_url, text2, on_progress, obj2);
		List<object> list4 = Py.L(PyDict.Of("addon_id", addon_id, "title", pyDict["name"], "files", Py.L(new PPath(text2).Name)));
		HashSet<string> seen_addons = new HashSet<string> { Py.Str(addon_id) };
		List<object> list5 = _install_cf_required_deps(dm, obj2, text, mc_version, loader, api_key, on_progress, seen_addons, 0);
		return PyDict.Of("source", "curseforge", "title", pyDict["name"], "files", Py.L(new PPath(text2).Name), "dependencies", list5, "installed", list4);
	}

	private static void _download_cf_file(DownloadManager dm, object addon_id, object file_id, string filename, object download_url, string dest, Action<string, long, long> on_progress, object metadata = null)
	{
		var urls = new List<object>();
		if (Py.Truthy(download_url))
		{
			urls.Add(download_url);
		}
		urls.AddRange(CfModDownloadUrls(addon_id, file_id, filename));
		urls.AddRange(CfModDownloadUrls(addon_id, file_id));
		on_progress?.Invoke("下载 CurseForge 模组 " + filename, 0L, 1L);
		CatalogDownload.CurseForge(dm, urls, dest, metadata);
	}

	private static List<object> _install_cf_required_deps(DownloadManager dm, object file_obj, string dest_dir, object mc_version, string loader, string api_key, Action<string, long, long> on_progress, HashSet<string> seen_addons, int depth)
	{
		if (depth >= 3)
		{
			return new List<object>();
		}
		List<object> list = new List<object>();
		foreach (object item in Iter(Py.Or(Gt(file_obj, "dependencies"), new List<object>())))
		{
			if (!Py.Eq(Gt(item, "relationType"), 3L))
			{
				continue;
			}
			object obj = Py.Or(Gt(item, "modId"), Gt(item, "addonId"));
			if (!Py.Truthy(obj) || seen_addons.Contains(Py.Str(obj)))
			{
				continue;
			}
			seen_addons.Add(Py.Str(obj));
			try
			{
				PyDict pyDict = CfDetail(dm, obj, api_key);
				object obj2 = _pick_cf_file(pyDict, Gt(item, "fileId"), mc_version, loader);
				if (!Py.Truthy(obj2))
				{
					Utils.log.Warning("依赖 %s 没有匹配的文件，跳过", Py.Str(obj));
					continue;
				}
				object obj3 = Gt(obj2, "id");
				if (obj3 != null)
				{
					string text = Py.Str(Py.Or(Gt(obj2, "fileName"), "mod-" + Py.Str(obj) + "-" + Py.Str(obj3) + ".jar"));
					string dest = _download_dest(dest_dir, text);
					_download_cf_file(dm, obj, obj3, text, Gt(obj2, "downloadUrl"), dest, on_progress, obj2);
					list.Add(Py.Or(pyDict["name"], text));
					list.AddRange(_install_cf_required_deps(dm, obj2, dest_dir, mc_version, loader, api_key, on_progress, seen_addons, depth + 1));
				}
			}
			catch (Exception e)
			{
				if (Downloader.IsCancel(e) || e is OperationCanceledException || dm.Token.IsCancellationRequested) throw;
				throw new ModError("必需依赖 " + Py.Str(obj) + " 安装失败，请重试：" + Py.ExcStr(e));
			}
		}
		return list;
	}

	private static object _pick_cf_file(PyDict mod, object want_file_id, object mc_version, string loader)
	{
		List<object> list = Iter(Py.Or(mod["latestFiles"], new List<object>()));
		if (list.Count == 0)
		{
			return null;
		}
		if (Py.Truthy(want_file_id))
		{
			object obj = list.FirstOrDefault((object x) => Py.Str(Gt(x, "id")) == Py.Str(want_file_id));
			if (obj != null)
			{
				return obj;
			}
		}
		List<object> list2 = list.Where((object x) => McIn(x, mc_version)).ToList();
		if (Py.Truthy(loader))
		{
			List<object> list3 = list2.Where((object x) => LoaderIn(x, loader)).ToList();
			if (list3.Count > 0)
			{
				list2 = list3;
			}
		}
		return ((list2.Count > 0) ? list2 : list)[0];
	}

	public static PyDict InstallCfMod(DownloadManager dm, string url, Instance instance, Action<string, long, long> on_progress = null, string mods_dir = null)
	{
		string text = _resolve_mods_dir(instance, mods_dir);
		Match match = _CF_RE.Match(url);
		if (match.Success)
		{
			string value = match.Groups[2].Value;
			string input = dm.FetchText(url, 60L);
			Match match2 = Regex.Match(input, "\"projectID\"\\s*:\\s*(\\d+)");
			if (!match2.Success)
			{
				match2 = Regex.Match(input, "projectID=(\\d+)");
			}
			if (!match2.Success)
			{
				throw new ModError("无法从页面解析项目 ID（CurseForge 页面结构变化或网络受限），请改用 Modrinth 或本地文件");
			}
			string value2 = match2.Groups[1].Value;
			string text2 = PPath.Join(text, "mod-" + value2 + "-" + value + ".jar");
			on_progress?.Invoke("下载 CurseForge 模组", 0L, 1L);
			CatalogDownload.CurseForge(dm, CfModDownloadUrls(value2, value), text2, null);
			return PyDict.Of("source", "curseforge", "files", Py.L(new PPath(text2).Name));
		}
		match = _CF_SLUG_RE.Match(url);
		if (match.Success)
		{
			string value3 = match.Groups[2].Value;
			List<object> list = SearchCurseforge(dm, null, 5L, null, 6L, value3);
			if (list.Count == 0)
			{
				throw new ModError("找不到该 CurseForge 模组");
			}
			return InstallCurseforgeMod(dm, ((PyDict)list[0]).Req("id"), instance, null, null, null, on_progress, null, mods_dir);
		}
		throw new ModError("无法识别的 CurseForge 链接");
	}

	public static PyDict InstallModFromSource(DownloadManager dm, object source, Instance instance, object mc_version = null, string loader = null, Action<string, long, long> on_progress = null, object version_id = null, string mods_dir = null)
	{
		instance.EnsureStandardDirs();
		string text = _resolve_mods_dir(instance, mods_dir);
		string text2 = Py.Str(source);
		if (HttpPrefix.IsMatch(text2))
		{
			Match match = Regex.Match(text2, "modrinth\\.com/mod(?:s)?/([^/?#]+)");
			if (match.Success)
			{
				return InstallModrinthMod(dm, match.Groups[1].Value, instance, mc_version, loader, on_progress, use_mirror: true, version_id, text);
			}
			if (text2.Contains("curseforge.com"))
			{
				return InstallCfMod(dm, text2, instance, on_progress, text);
			}
			if (Py.Lower(text2.Split('?')[0]).EndsWith(".jar", StringComparison.Ordinal))
			{
				string text3 = text2.Split('/').Last().Split('?')[0];
				if (text3.Length == 0)
				{
					text3 = "mod.jar";
				}
				dm.Download(text2, PPath.Join(text, text3), null, null, force: false, 600L);
				return PyDict.Of("source", "url", "files", Py.L(text3));
			}
			throw new ModError("无法识别的链接：支持 Modrinth 模组链接、CurseForge 文件页链接、.jar 直链");
		}
		PPath pPath = new PPath(text2);
		if (LocalFs.IsFile(text2))
		{
			if (Py.Lower(pPath.Suffix) != ".jar")
			{
				throw new ModError("本地文件只支持 .jar 模组");
			}
			LocalFs.Copy2(pPath.ToString(), PPath.Join(text, pPath.Name));
			return PyDict.Of("source", "file", "files", Py.L(pPath.Name));
		}
		return InstallModrinthMod(dm, text2, instance, mc_version, loader, on_progress, use_mirror: true, version_id, text);
	}

	public static PyDict InstallModrinthContent(DownloadManager dm, object slug, Instance instance, string subdir, object mc_version = null, Action<string, long, long> on_progress = null, object version_id = null)
	{
		instance.EnsureStandardDirs();
		string text = PPath.Join(instance.path, subdir);
		Utils.EnsureDir(text);
		List<object> list;
		if (Py.Truthy(version_id))
		{
			object obj;
			try
			{
				obj = dm.FetchJson("https://api.modrinth.com/v2/version/" + Py.Str(version_id), API_TIMEOUT);
			}
			catch (Exception e)
			{
				throw new ModError("获取版本失败: " + Py.ExcStr(e));
			}
			list = (Py.Truthy(obj) ? Py.L(obj) : new List<object>());
		}
		else
		{
			list = ListVersions(dm, slug, Py.Truthy(mc_version) ? mc_version : null);
			if (list.Count == 0)
			{
				list = ListVersions(dm, slug);
			}
		}
		if (list.Count == 0)
		{
			throw new ModError(Py.Str(slug) + " 没有可下载版本");
		}
		object o = _primary_file(list[0]);
		if (!Py.Truthy(o) || !Py.Truthy(Gt(o, "url")))
		{
			throw new ModError(Py.Str(slug) + " 没有可下载文件");
		}
		string text2 = _download_dest(text, Gt(o, "filename"));
		on_progress?.Invoke("下载 " + new PPath(text2).Name, 0L, 1L);
		List<string> list2 = ModrinthDownloadUrls(Py.Str(Gt(o, "url")));
		CatalogDownload.Modrinth(dm, list2.Cast<object>(), text2, o);
		return PyDict.Of("source", "modrinth", "slug", slug, "files", Py.L(new PPath(text2).Name));
	}

	public static PyDict InstallCfContent(DownloadManager dm, object addon_id, Instance instance, string subdir, object mc_version = null, string api_key = null, Action<string, long, long> on_progress = null, object file_id = null)
	{
		instance.EnsureStandardDirs();
		string text = PPath.Join(instance.path, subdir);
		Utils.EnsureDir(text);
		List<object> list = Iter(Py.Or(CfDetail(dm, addon_id, api_key)["latestFiles"], new List<object>()));
		if (list.Count == 0)
		{
			list = CfFiles(dm, addon_id, api_key, null, null, 50L);
		}
		if (list.Count == 0)
		{
			throw new ModError("没有可下载文件");
		}
		object obj;
		if (Py.Truthy(file_id))
		{
			obj = list.FirstOrDefault((object x) => Py.Str(Gt(x, "id")) == Py.Str(file_id));
			if (obj == null)
			{
				list = CfFiles(dm, addon_id, api_key, null, null, 50L);
				obj = list.FirstOrDefault((object x) => Py.Str(Gt(x, "id")) == Py.Str(file_id));
			}
			if (obj == null)
			{
				throw new ModError("找不到文件 " + Py.Str(file_id));
			}
		}
		else
		{
			List<object> list2 = list.Where((object x) => McIn(x, mc_version)).ToList();
			obj = ((list2.Count > 0) ? list2 : list)[0];
		}
		file_id = Gt(obj, "id");
		string text2 = Py.Str(Py.Or(Gt(obj, "fileName"), "file-" + Py.Str(addon_id) + "-" + Py.Str(file_id) + ".zip"));
		string text3 = _download_dest(text, text2);
		object obj2 = Gt(obj, "downloadUrl");
		List<object> list3 = new List<object>();
		if (Py.Truthy(obj2))
		{
			list3.Add(obj2);
		}
		list3.AddRange(CfModDownloadUrls(addon_id, file_id, text2));
		on_progress?.Invoke("下载 " + text2, 0L, 1L);
		CatalogDownload.CurseForge(dm, list3, text3, obj);
		return PyDict.Of("source", "curseforge", "id", addon_id, "files", Py.L(new PPath(text3).Name));
	}

	public static PyDict InstallContentFromSource(DownloadManager dm, Instance instance, string subdir, PyDict extra = null, Action<string, long, long> on_progress = null)
	{
		extra = extra ?? new PyDict();
		instance.EnsureStandardDirs();
		string text = PPath.Join(instance.path, subdir);
		Utils.EnsureDir(text);
		object obj = Py.Or(extra["game_version"], extra["mc_version"]);
		if (obj is string text2 && (Py.Strip(text2).Length == 0 || text2.StartsWith("全部", StringComparison.Ordinal)))
		{
			obj = null;
		}
		object o = extra["path"];
		if (Py.Truthy(o) && LocalFs.IsFile(Py.Str(o)))
		{
			PPath pPath = new PPath(PPath.Norm(Py.Str(o)));
			string text3 = PPath.Join(text, pPath.Name);
			LocalFs.Copy2(pPath.ToString(), text3);
			return PyDict.Of("source", "file", "files", Py.L(new PPath(text3).Name));
		}
		object o2 = extra["url"];
		if (Py.Truthy(o2) && Py.Str(o2).StartsWith("http", StringComparison.Ordinal))
		{
			string text4 = Py.Str(o2).Split('/').Last()
				.Split('?')[0];
			if (text4.Length == 0)
			{
				text4 = "pack.zip";
			}
			string text5 = PPath.Join(text, text4);
			on_progress?.Invoke("下载 " + text4, 0L, 1L);
			dm.Download(Py.Str(o2), text5, null, null, force: false, 900L);
			return PyDict.Of("source", "url", "files", Py.L(new PPath(text5).Name));
		}
		string text6 = Py.Lower(Py.S(extra["source"]));
		object file_id = Py.Or(extra["file_id"], extra["version_id"]);
		if (Py.Truthy(extra["id"]) && (text6.StartsWith("curse", StringComparison.Ordinal) || Py.Truthy(extra["file_id"])))
		{
			return InstallCfContent(dm, extra["id"], instance, subdir, obj, null, on_progress, file_id);
		}
		object obj2 = Py.Or(extra["slug"], extra["name"]);
		if (!Py.Truthy(obj2))
		{
			throw new ModError("缺少 slug / 文件 / 链接");
		}
		return InstallModrinthContent(dm, obj2, instance, subdir, obj, on_progress, extra["version_id"]);
	}

	internal static object Gt(object o, string k)
	{
		if (o is PyDict pyDict)
		{
			return pyDict[k];
		}
		throw new AttributeError("'" + Py.TypeName(o) + "' object has no attribute 'get'");
	}

	internal static object Gd(object o, string k, object dflt)
	{
		if (o is PyDict pyDict)
		{
			if (!pyDict.ContainsKey(k))
			{
				return dflt;
			}
			return pyDict[k];
		}
		throw new AttributeError("'" + Py.TypeName(o) + "' object has no attribute 'get'");
	}

	internal static List<object> Iter(object o)
	{
		if (o != null)
		{
			if (o is List<object> result)
			{
				return result;
			}
			return Py.ListCtor(o);
		}
		throw new TypeError("'NoneType' object is not iterable");
	}

	internal static object SliceObj(object o, int n)
	{
		if (o is string s)
		{
			return Py.Slice(s, null, n);
		}
		if (o is List<object> s2)
		{
			return Py.Slice(s2, null, n);
		}
		throw new TypeError("'" + Py.TypeName(o) + "' object is not subscriptable");
	}

	private static string ErrText(object e)
	{
		if (!(e is Exception e2))
		{
			return Py.Str(e);
		}
		return Py.ExcStr(e2);
	}

	public static string _mr_facets(string project_type, object game_version = null, object categories = null)
	{
		List<object> list = new List<object>();
		list.Add(Py.L("project_type:" + project_type));
		List<object> list2 = list;
		if (Py.Truthy(game_version))
		{
			list2.Add(Py.L("versions:" + Py.Str(game_version)));
		}
		List<object> list3 = Iter(Py.Or(categories, new List<object>())).Where(Py.Truthy).ToList();
		if (list3.Count > 0)
		{
			list2.Add(((IEnumerable<object>)list3).Select((Func<object, object>)((object c) => "categories:" + Py.Str(c))).ToList());
		}
		return PyJson.Dumps(list2);
	}

	private static PyDict SearchParams(object query, string project_type, long limit, object game_version, object categories)
	{
		string text = Py.Strip(Py.S(query));
		return PyDict.Of("query", text, "facets", _mr_facets(project_type, game_version, categories), "limit", limit, "index", (text.Length > 0) ? "relevance" : "downloads");
	}

	public static List<object> SearchMods(DownloadManager dm, object query, long limit = 30L, object game_version = null, object categories = null)
	{
		PyDict ps = SearchParams(query, "mod", limit, game_version, categories);
		object obj = null;
		object o = null;
		bool flag = false;
		foreach (string item in Source.ModrinthApiBases())
		{
			try
			{
				o = dm.FetchJson(WithParams(item + "/search", ps), API_TIMEOUT, expand: false);
				flag = true;
			}
			catch (Exception ex)
			{
				obj = ex;
				o = null;
				continue;
			}
			break;
		}
		if (!flag)
		{
			throw new ModError("搜索模组失败（官方+镜像均不可用）: " + ((obj == null) ? "None" : ErrText(obj)));
		}
		List<object> list = new List<object>();
		foreach (object item2 in Iter(Gd(o, "hits", new List<object>())))
		{
			object obj2 = Gt(item2, "slug");
			list.Add(PyDict.Of("slug", obj2, "title", Gd(item2, "title", obj2), "description", Catalog.ModDescCn(obj2, Gt(item2, "title"), SliceObj(Py.Or(Gt(item2, "description"), ""), 120)), "author", Gd(item2, "author", "?"), "downloads", Gd(item2, "downloads", 0L), "tags", Tags(item2), "updated", Py.Slice(Py.S(Gt(item2, "date_modified")), null, 10), "source", "modrinth", "icon_url", Py.Or(Gt(item2, "icon_url"), ""), "featured_gallery", Gt(item2, "featured_gallery"), "gallery", Gt(item2, "gallery"), "original_description", Gt(item2, "description")));
		}
		return list;
	}

	private static List<object> Tags(object h)
	{
		object obj = Py.Or(Gt(h, "display_categories"), Gt(h, "categories"), new List<object>());
		return ((IEnumerable<object>)Iter(SliceObj((obj is string || obj is List<object>) ? obj : Iter(obj), 6))).Select((Func<object, object>)((object c) => Py.Str(c))).ToList();
	}

	public static List<object> ListVersions(DownloadManager dm, object slug, object game_version = null, List<object> loaders = null)
	{
		PyDict pyDict = new PyDict();
		if (Py.Truthy(game_version))
		{
			pyDict["game_versions"] = PyJson.Dumps(Py.L(game_version));
		}
		if (loaders != null && loaders.Count > 0)
		{
			pyDict["loaders"] = PyJson.Dumps(loaders);
		}
		object o = CatalogVersions.Modrinth(dm, Py.Str(slug), pyDict);
		List<object> list = new List<object>();
		foreach (object item in Iter(o))
		{
			List<object> list2 = new List<object>();
			foreach (object item2 in Iter(Gd(item, "files", new List<object>())))
			{
				object o2 = Py.Or(Gt(item2, "hashes"), new PyDict());
				list2.Add(PyDict.Of("url", Gt(item2, "url"), "filename", Gt(item2, "filename"), "primary", Gd(item2, "primary", false), "size", Gd(item2, "size", 0L), "sha1", Gt(o2, "sha1"), "sha512", Gt(o2, "sha512")));
			}
			list.Add(PyDict.Of("id", Gt(item, "id"), "name", Gt(item, "name"), "version_number", Gt(item, "version_number"), "version_type", Py.Or(Gt(item, "version_type"), "release"), "game_versions", Gd(item, "game_versions", new List<object>()), "loaders", Gd(item, "loaders", new List<object>()), "files", list2, "dependencies", Gd(item, "dependencies", new List<object>()), "date_published", Py.Slice(Py.S(Gt(item, "date_published")), null, 19), "downloads", Py.Or(Gt(item, "downloads"), 0L), "changelog", SliceObj(Py.Or(Gt(item, "changelog"), ""), 400)));
            var row = (PyDict)list[list.Count - 1];
            row["version_source"] = Gt(item, "version_source"); row["version_warning"] = Gt(item, "version_warning");
            row["date_published"] = Gt(item, "date_published");
		}
		return list;
	}

	public static object _primary_file(object version)
	{
		object o = Gt(version, "files");
		if (!Py.Truthy(o))
		{
			return null;
		}
		foreach (object item in Iter(o))
		{
			if (Py.Truthy(Gt(item, "primary")))
			{
				return item;
			}
		}
		return Iter(o)[0];
	}

	public static List<object> SearchModsChinese(DownloadManager dm, string query, long limit = 30L, string api_key = null)
	{
		string text = Py.Strip(query);
		if (text.Length == 0)
		{
			return new List<object>();
		}
		List<object> list = new List<object>();
		HashSet<string> hashSet = new HashSet<string>();
		object[] array = Catalog.LookupModAlias(text);
		string text2 = array[0] as string;
		object obj = array[1];
		object title = array[2];
		if (Py.Truthy(text2))
		{
			List<object> list2 = _alias_to_modrinth_hits(dm, text2, title, limit);
			if (list2.Count > 0)
			{
				list.AddRange(list2);
			}
			else
			{
				hashSet.Add(text2);
				Utils.log.Warning("别名 slug 无效，改走全文搜索: %s", text2);
			}
		}
		if (Py.Truthy(obj))
		{
			try
			{
				list.AddRange(_alias_to_cf_hits(dm, obj, title, api_key));
			}
			catch (Exception e)
			{
				Utils.log.Warning("别名命中后 CurseForge 查询失败 %s: %s", Py.Str(obj), Py.ExcStr(e));
			}
		}
		if (list.Count > 0)
		{
			return Py.Slice(list, null, (int)limit).ToList();
		}
		List<object[]> source = Catalog.FuzzyMatchMod(text);
		HashSet<string> hashSet2 = new HashSet<string>();
		HashSet<string> hashSet3 = new HashSet<string>();
		foreach (object[] item in source.Take(3))
		{
			string text3 = item[0] as string;
			object obj2 = item[1];
			object title2 = item[2];
			if (Py.Truthy(text3) && !hashSet2.Contains(text3) && !hashSet.Contains(text3))
			{
				hashSet2.Add(text3);
				List<object> list3 = _alias_to_modrinth_hits(dm, text3, title2, 1L);
				if (list3.Count > 0)
				{
					list.AddRange(list3);
				}
				else
				{
					hashSet.Add(text3);
				}
			}
			if (Py.Truthy(obj2) && !hashSet3.Contains(Py.Str(obj2)) && list.Count < 3)
			{
				hashSet3.Add(Py.Str(obj2));
				try
				{
					list.AddRange(_alias_to_cf_hits(dm, obj2, title2, api_key));
				}
				catch (Exception e2)
				{
					Utils.log.Warning("模糊匹配 CurseForge 查询失败 %s: %s", Py.Str(obj2), Py.ExcStr(e2));
				}
			}
			if (list.Count >= 4)
			{
				break;
			}
		}
		if (list.Count > 0)
		{
			return Py.Slice(list, null, (int)limit).ToList();
		}
		try
		{
			list.AddRange(SearchMods(dm, text, limit));
		}
		catch (Exception e3)
		{
			Utils.log.Warning("中文搜索回退 Modrinth 失败: %s", Py.ExcStr(e3));
		}
		try
		{
			list.AddRange(SearchCurseforge(dm, text, limit, api_key, 6L));
		}
		catch (Exception e4)
		{
			Utils.log.Warning("中文搜索回退 CurseForge 失败: %s", Py.ExcStr(e4));
		}
		return Py.Slice(RankHits(list, text), null, (int)limit).ToList();
	}

	private static List<object> _alias_to_modrinth_hits(DownloadManager dm, string slug, object title = null, long limit = 30L)
	{
		List<object> list = new List<object>();
		try
		{
			object o = dm.FetchJson("https://api.modrinth.com/v2/project/" + slug, API_TIMEOUT);
			object obj = Gd(o, "slug", slug);
			list.Add(PyDict.Of("source", "modrinth", "slug", obj, "title", Py.Or(Gt(o, "title"), title, slug), "author", CatalogPresentation.ModrinthAuthors(dm, Gt(o, "id") ?? obj), "downloads", Gd(o, "downloads", 0L), "description", Catalog.ModDescCn(obj, Py.Or(Gt(o, "title"), title), SliceObj(Py.Or(Gt(o, "description"), ""), 120)), "matched_alias", true, "icon_url", Py.Or(Gt(o, "icon_url"), ""), "gallery", Gt(o, "gallery"), "original_description", Gt(o, "description")));
		}
		catch (Exception e)
		{
			Utils.log.Warning("Modrinth 项目 %s 查询失败: %s", slug, Py.ExcStr(e));
		}
		return list;
	}

	private static List<object> _alias_to_cf_hits(DownloadManager dm, object cf_id, object title = null, string api_key = null)
	{
		try
		{
			PyDict pyDict = CfDetail(dm, cf_id, api_key);
			object obj = Py.Or(pyDict["links"], new PyDict());
			List<object> list = new List<object>();
            var row = PyDict.Of("source", "curseforge", "id", pyDict["id"], "slug", pyDict["slug"], "title", Py.Or(pyDict["name"], title, Py.Str(cf_id)), "author", Authors(pyDict), "downloads", Py.Or(pyDict["downloadCount"], 0L), "description", Catalog.ModDescCn(pyDict["slug"], Py.Or(pyDict["name"], title), SliceObj(Py.Or(pyDict["summary"], ""), 120)), "original_description", pyDict.Get("summary"), "matched_alias", true);
            row.Update(CatalogPresentation.Artwork(pyDict));
            list.Add(row);
			return list;
		}
		catch (Exception e)
		{
			Utils.log.Warning("CurseForge 项目 %s 查询失败: %s", Py.Str(cf_id), Py.ExcStr(e));
			return new List<object>();
		}
	}

	private static string Authors(PyDict m)
	{
		List<string> values = (from a in Iter(Py.Or(m["authors"], new List<object>()))
			select Py.Str(Gd(a, "name", ""))).ToList();
		string text = string.Join(", ", values);
		if (text.Length <= 0)
		{
			return "?";
		}
		return text;
	}

	public static double WeightedDownloads(PyDict hit, string kind = "mod")
	{
		if (!PLATFORM_WEIGHT.TryGetValue(kind ?? "", out Dictionary<string, double> value))
		{
			value = PLATFORM_WEIGHT["mod"];
		}
		if (!value.TryGetValue(Py.Lower(Py.S(hit["source"])), out var value2))
		{
			value2 = 1.0;
		}
		return Py.Max(Py.FloatOr(Py.Or(hit["downloads"], 0L), 0.0), 0.0) * value2;
	}

	private static double _similarity(object text0, string query)
	{
		string text1 = Py.Lower(Py.Strip(Py.S(text0)));
		if (text1.Length == 0 || string.IsNullOrEmpty(query))
		{
			return 0.0;
		}
		if (text1 == query)
		{
			return 1.0;
		}
		if (text1.Contains(query))
		{
			return 0.8 + 0.2 * (double)PyDifflib.PyLen(query) / (double)PyDifflib.PyLen(text1);
		}
		return PyDifflib.Ratio(query, text1);
	}

	public static List<object> RankHits(IEnumerable<object> hits, object query = null, string kind = "mod")
	{
		List<PyDict> list = hits.Where((object h) => h is PyDict).Cast<PyDict>().ToList();
		string text = Py.Lower(Py.Strip(Py.S(query)));
		if (list.Count == 0)
		{
			return new List<object>();
		}
		if (text.Length == 0)
		{
			return Py.Sorted(list, (PyDict h) => WeightedDownloads(h, kind), reverse: true).Cast<object>().ToList();
		}
		List<double> list2 = new List<double>();
		foreach (PyDict item in list)
		{
			list2.Add(Math.Max(Math.Max(_similarity(Py.Or(item["title"], item["name"]), text), _similarity(item["slug"], text)), 0.05 * _similarity(Py.Or(item["description"], item["summary"]), text)));
		}
		double num = list2.Max();
		if (num == 0.0)
		{
			num = 1.0;
		}
		List<KeyValuePair<double, PyDict>> list3 = new List<KeyValuePair<double, PyDict>>();
		for (int num2 = 0; num2 < list.Count; num2++)
		{
			double num3 = list2[num2] / num;
			num3 += Math.Log10(Math.Max(WeightedDownloads(list[num2], kind), 1.0)) / 9.0;
			if (Py.Truthy(list[num2]["matched_alias"]))
			{
				num3 += 0.2;
			}
			list3.Add(new KeyValuePair<double, PyDict>(num3, list[num2]));
		}
		return (from p in Py.Sorted<KeyValuePair<double, PyDict>>(list3, (KeyValuePair<double, PyDict> p) => p.Key, reverse: true)
			select p.Value).Cast<object>().ToList();
	}

	public static List<object> SearchCurseforge(DownloadManager dm, object query = null, long limit = 30L, string api_key = null, long class_id = 6L, object slug = null, object game_version = null, List<object> categories = null, string loader = "")
	{
		bool flag = categories != null && categories.Count > 0;
		PyDict pyDict = PyDict.Of("gameId", 432L, "classId", class_id, "sortField", 2L, "sortOrder", "desc", "pageSize", Math.Min(50L, flag ? (limit * 2) : limit), "index", 0L);
        if (CatalogLoader.CurseForge(loader) > 0) pyDict["modLoaderType"] = CatalogLoader.CurseForge(loader);
		if (Py.Truthy(query))
		{
			pyDict["searchFilter"] = query;
		}
		if (Py.Truthy(slug))
		{
			pyDict["slug"] = slug;
		}
		if (Py.Truthy(game_version))
		{
			pyDict["gameVersion"] = game_version;
		}
		List<object> list = ((IEnumerable<object>)CfItems(CfFetch(dm, "/mods/search", api_key, pyDict))).Select((Func<object, object>)((object m) => _cf_norm(m))).ToList();
		if (flag)
		{
			List<string> tokens = (from t in categories.Where(Py.Truthy)
				select Py.Lower(Py.Str(t))).ToList();
			list = list.Where((object h) => tokens.Any((string tok) => tok.Length > 0 && Iter(Py.Or(((PyDict)h)["cf_categories"], new List<object>())).Any((object c) => Py.Str(c).Contains(tok)))).ToList();
		}
		return Py.Slice(list, null, (int)limit).ToList();
	}

	private static object _cf_logo_url(object m)
	{
		if (Gt(m, "logo") is PyDict pyDict)
		{
			return Py.Or(pyDict["thumbnailUrl"], pyDict["url"], "");
		}
		return Py.Or(Gt(m, "logoUrl"), "");
	}

	private static PyDict _cf_norm(object m0)
	{
		object obj = Py.Or(Gt(m0, "name"), Gt(m0, "title"), "?");
		PyDict pyDict = (PyDict)m0;
		object obj2 = Py.Or(pyDict["links"], new PyDict());
		List<object> list = new List<object>();
		foreach (object item in Iter(Py.Or(pyDict["categories"], new List<object>())))
		{
			if (item is PyDict pyDict2)
			{
				list.Add(Py.Lower(Py.S(pyDict2["name"])));
			}
		}
		var row = PyDict.Of("source", "curseforge", "id", pyDict["id"], "slug", pyDict["slug"], "title", obj, "author", Authors(pyDict), "downloads", Py.Or(pyDict["downloadCount"], pyDict["downloads"], 0L), "summary", Catalog.ModDescCn(pyDict["slug"], obj, SliceObj(Py.Or(pyDict["summary"], ""), 120)), "website_url", Py.Or((obj2 is PyDict pyDict3) ? pyDict3["websiteUrl"] : "", ""), "cf_categories", list);
        row.Update(CatalogPresentation.Artwork(pyDict));
        row["original_description"] = pyDict.Get("summary");
        return row;
	}

	public static object CfBySlug(DownloadManager dm, object slug, object class_id, string api_key = null)
	{
		if (!Py.Truthy(slug))
		{
			return null;
		}
		PyDict pyDict = PyDict.Of("gameId", 432L, "slug", slug, "pageSize", 5L, "index", 0L);
		if (class_id != null)
		{
			pyDict["classId"] = class_id;
		}
		List<object> list = CfItems(CfFetch(dm, "/mods/search", api_key, pyDict));
		if (list.Count > 0)
		{
			return list[0];
		}
		if (class_id != null)
		{
			return CfBySlug(dm, slug, null, api_key);
		}
		return null;
	}

	public static List<object> SearchModrinthProjects(DownloadManager dm, object query, string project_type, long limit = 30L, object game_version = null, object categories = null, string loader = "")
	{
		PyDict ps = SearchParams(query, project_type, limit, game_version, categories);
        loader = CatalogLoader.Normalize(loader);
        if (loader.Length > 0)
        {
            var facets = (List<object>)PyJson.Loads(Py.S(ps["facets"]));
            facets.Add(Py.L("categories:" + loader)); ps["facets"] = PyJson.Dumps(facets);
        }
		object obj = null;
		object obj2 = null;
		foreach (string item in Source.ModrinthApiBases())
		{
			try
			{
				obj2 = dm.FetchJson(WithParams(item + "/search", ps), API_TIMEOUT, expand: false);
			}
			catch (Exception ex)
			{
				obj = ex;
				continue;
			}
			break;
		}
		if (obj2 == null)
		{
			throw new ModError("搜索 " + project_type + " 失败: " + ((obj == null) ? "None" : ErrText(obj)));
		}
		List<object> list = new List<object>();
		foreach (object item2 in Iter(Py.Or(Gt(obj2, "hits"), new List<object>())))
		{
			list.Add(PyDict.Of("source", "modrinth", "slug", Gt(item2, "slug"), "title", Py.Or(Gt(item2, "title"), Gt(item2, "slug")), "description", Catalog.ModDescCn(Gt(item2, "slug"), Gt(item2, "title"), SliceObj(Py.Or(Gt(item2, "description"), ""), 160)), "author", Py.Or(Gt(item2, "author"), "?"), "downloads", Py.Or(Gt(item2, "downloads"), 0L), "tags", Tags(item2), "updated", Py.Slice(Py.S(Gt(item2, "date_modified")), null, 10), "icon_url", Py.Or(Gt(item2, "icon_url"), ""), "featured_gallery", Gt(item2, "featured_gallery"), "gallery", Gt(item2, "gallery"), "original_description", Gt(item2, "description")));
		}
		return list;
	}
}
