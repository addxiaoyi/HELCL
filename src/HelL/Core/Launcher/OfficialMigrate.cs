using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace HelL.Core.Launcher;

public static class OfficialMigrate
{
	private static readonly Regex _ISO_TS = new Regex("(\\d{4})-(\\d{2})-(\\d{2})[T ](\\d{2}):(\\d{2}):(\\d{2})", RegexOptions.CultureInvariant);

	private static bool IsDir(string p)
	{
		return Directory.Exists(Utils.AbsPath(p));
	}

	private static bool IsFile(string p)
	{
		return File.Exists(Utils.AbsPath(p));
	}

	public static string OfficialDir()
	{
		List<string> list = new List<string>();
		if (Utils.IS_WINDOWS)
		{
			list.Add(PPath.Join(PPath.Norm(Environment.GetEnvironmentVariable("APPDATA") ?? ""), ".minecraft"));
		}
		else if (Utils.IS_MAC)
		{
			list.Add(PPath.Join(Home(), "Library", "Application Support", "minecraft"));
		}
		else
		{
			list.Add(PPath.Join(Home(), ".minecraft"));
		}
		foreach (string item in list)
		{
			if (IsDir(item))
			{
				return item;
			}
		}
		return PPath.Norm("");
	}

	private static string Home()
	{
		string text = (Utils.IS_WINDOWS ? Environment.GetEnvironmentVariable("USERPROFILE") : Environment.GetEnvironmentVariable("HOME"));
		if (string.IsNullOrEmpty(text))
		{
			text = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		}
		return PPath.Norm(text);
	}

	public static bool DetectOfficial()
	{
		string text = OfficialDir();
		if (IsDir(text))
		{
			return IsDir(PPath.Join(text, "versions"));
		}
		return false;
	}

	private static List<string> SortedChildren(string dir)
	{
		return Py.Sorted(Directory.EnumerateFileSystemEntries(Utils.AbsPath(dir)).Select(Path.GetFileName).ToList(), (string n) => Utils.IS_WINDOWS ? n.ToLowerInvariant() : n);
	}

	public static List<object> ScanVersions(string src)
	{
		string text = PPath.Join(src, "versions");
		if (!IsDir(text))
		{
			return new List<object>();
		}
		List<object> list = new List<object>();
		foreach (string item in SortedChildren(text))
		{
			string text2 = PPath.Join(text, item);
			if (IsDir(text2) && IsFile(PPath.Join(text2, item + ".json")))
			{
				list.Add(item);
			}
		}
		return list;
	}

	private static PyDict _load_version_json(string src, string versionId)
	{
		object obj = Utils.ReadJson(PPath.Join(src, "versions", versionId, versionId + ".json"), new PyDict());
		if (!Py.Truthy(obj))
		{
			return new PyDict();
		}
		if (obj is PyDict result)
		{
			return result;
		}
		throw new AttributeError("'" + Py.TypeName(obj) + "' object has no attribute 'get'");
	}

	private static object G(object d, string k)
	{
		if (d is PyDict pyDict)
		{
			return pyDict[k];
		}
		throw new AttributeError("'" + Py.TypeName(d) + "' object has no attribute 'get'");
	}

	public static List<string> _inherit_chain(string src, string versionId)
	{
		List<string> list = new List<string>();
		HashSet<string> hashSet = new HashSet<string>();
		string text = versionId;
		while (!string.IsNullOrEmpty(text) && !hashSet.Contains(text))
		{
			hashSet.Add(text);
			list.Add(text);
			text = Py.Strip(Py.Str(Py.Or(_load_version_json(src, text)["inheritsFrom"], "")));
		}
		return list;
	}

	public static List<string> _library_relpaths(string src, string versionId)
	{
		List<string> list = new List<string>();
		HashSet<string> hashSet = new HashSet<string>();
		foreach (string item in _inherit_chain(src, versionId))
		{
			foreach (object item2 in Iter(Py.Or(_load_version_json(src, item)["libraries"], new List<object>())))
			{
				if (!(item2 is PyDict pyDict))
				{
					continue;
				}
				string text = Py.Str(Py.Or(pyDict["name"], ""));
				object d = Py.Or(pyDict["downloads"], new PyDict());
				object o = Py.Or(G(Py.Or(G(d, "artifact"), new PyDict()), "path"), (text.Length > 0) ? Utils.MavenArtifactPath(text) : "");
				if (Py.Truthy(o) && !hashSet.Contains(Py.Str(o)))
				{
					hashSet.Add(Py.Str(o));
					list.Add(Py.Str(o));
				}
				string text2 = Installer.SelectNativeClassifier(pyDict);
				if (!string.IsNullOrEmpty(text2))
				{
					object o2 = Py.Or(G(Py.Or(G(Py.Or(G(d, "classifiers"), new PyDict()), text2), new PyDict()), "path"), (text.Length > 0) ? Utils.MavenArtifactPath(text + ":" + text2) : "");
					if (Py.Truthy(o2) && !hashSet.Contains(Py.Str(o2)))
					{
						hashSet.Add(Py.Str(o2));
						list.Add(Py.Str(o2));
					}
				}
			}
		}
		return list;
	}

	private static IEnumerable<object> Iter(object o)
	{
		if (!(o is List<object> result))
		{
			if (!(o is PyDict pyDict))
			{
				if (o is string source)
				{
					return ((IEnumerable<char>)source).Select((Func<char, object>)((char c) => c.ToString())).ToList();
				}
				throw new TypeError("'" + Py.TypeName(o) + "' object is not iterable");
			}
			return pyDict.Keys.Cast<object>().ToList();
		}
		return result;
	}

	private static void Copy2(string src, string dst)
	{
		string text = Utils.AbsPath(src);
		string text2 = Utils.AbsPath(dst);
		File.Copy(text, text2, overwrite: true);
		try
		{
			File.SetLastWriteTimeUtc(text2, File.GetLastWriteTimeUtc(text));
		}
		catch (Exception)
		{
		}
	}

	public static long CopyLibraries(string src, Instance dest, string versionId)
	{
		string text = PPath.Join(src, "libraries");
		if (!IsDir(text))
		{
			return 0L;
		}
		string text2 = dest.LibrariesDir();
		long num = 0L;
		foreach (string item in _library_relpaths(src, versionId))
		{
			string text3 = PPath.Join(text, item);
			if (!IsFile(text3))
			{
				continue;
			}
			string text4 = PPath.Join(text2, item);
			if (!IsFile(text4) || new FileInfo(Utils.AbsPath(text4)).Length != new FileInfo(Utils.AbsPath(text3)).Length)
			{
				Directory.CreateDirectory(Utils.AbsPath(new PPath(text4).Parent.ToString()));
				try
				{
					Copy2(text3, text4);
					num++;
				}
				catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
				{
					Utils.log.Warning("复制依赖库失败 %s: %s", item, Py.ExcStr(ex));
				}
			}
		}
		return num;
	}

	private static string _copy_version(string src, Instance dest, string versionId)
	{
		if (!IsDir(PPath.Join(src, "versions", versionId)))
		{
			return "";
		}
		foreach (string item in _inherit_chain(src, versionId))
		{
			string text = PPath.Join(src, "versions", item);
			if (!IsDir(text))
			{
				continue;
			}
			string text2 = PPath.Join(dest.VersionsDir(), item);
			Directory.CreateDirectory(Utils.AbsPath(text2));
			string[] array = new string[2]
			{
				item + ".json",
				item + ".jar"
			};
			foreach (string text3 in array)
			{
				string text4 = PPath.Join(text, text3);
				if (IsFile(text4))
				{
					Copy2(text4, PPath.Join(text2, text3));
				}
			}
		}
		CopyLibraries(src, dest, versionId);
		return versionId;
	}

	private static void _copy_tree(string src, string dest)
	{
		string text = Utils.AbsPath(src);
		foreach (string item in Directory.EnumerateFiles(text, "*", SearchOption.AllDirectories))
		{
			string path = item.Substring(text.TrimEnd(Path.DirectorySeparatorChar).Length + 1);
			string text2 = Path.Combine(Utils.AbsPath(dest), path);
			Directory.CreateDirectory(Path.GetDirectoryName(text2));
			try
			{
				Copy2(item, text2);
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
			{
			}
		}
	}

	public static List<object> ImportVersions(string src, object instanceName = null, List<object> versions = null)
	{
		Instance instance = new Instance(instanceName);
		if (!IsDir(instance.path))
		{
			instance.Create();
		}
		List<object> obj = ((versions != null && versions.Count > 0) ? versions : ScanVersions(src));
		List<object> list = new List<object>();
		foreach (string item in obj)
		{
			try
			{
				_copy_version(src, instance, item);
				list.Add(item);
			}
			catch (Exception e)
			{
				Utils.log.Warning("导入版本失败 %s: %s", item, Py.ExcStr(e));
			}
		}
		if (list.Count > 0)
		{
			instance.SetMeta("mc_version", list[list.Count - 1]);
		}
		return list;
	}

	public static double _expires_at(PyDict acc)
	{
		Match m = _ISO_TS.Match(Py.Str(Py.Or(acc["accessTokenExpiresAt"], "")));
		if (!m.Success)
		{
			return 0.0;
		}
		try
		{
			return (double)(new DateTime(P(1), P(2), P(3), P(4), P(5), P(6), DateTimeKind.Utc) - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).Ticks / 10000000.0;
		}
		catch (ArgumentOutOfRangeException)
		{
			return 0.0;
		}
		int P(int i)
		{
			return int.Parse(m.Groups[i].Value);
		}
	}

	public static List<object> ReadOfficialAccounts(string src)
	{
		if (!(Utils.ReadJson(PPath.Join(src, "launcher_accounts.json")) is PyDict pyDict))
		{
			return new List<object>();
		}
		List<object> list = new List<object>();
		object obj = Py.Or(pyDict["accounts"], new PyDict());
		foreach (KeyValuePair<string, object> item in (obj as PyDict) ?? throw new AttributeError("'" + Py.TypeName(obj) + "' object has no attribute 'items'"))
		{
			if (item.Value is PyDict pyDict2)
			{
				object d = Py.Or(pyDict2["minecraftProfile"], new PyDict());
				string text = Py.Strip(Py.Str(Py.Or(G(d, "name"), "")));
				string text2 = Py.Strip(Py.Str(Py.Or(G(d, "id"), "")));
				if (text.Length != 0 && text2.Length != 0)
				{
					list.Add(PyDict.Of("type", "microsoft", "name", text, "uuid", Utils.DashedUuid(text2), "access_token", Py.Str(Py.Or(pyDict2["accessToken"], "")), "refresh_token", "", "xuid", "", "expires_at", _expires_at(pyDict2), "updated_at", Py.Time(), "imported_from", "official-launcher", "local_id", item.Key));
				}
			}
		}
		return list;
	}

	public static List<object> ImportAccounts(string src, AccountManager manager = null)
	{
		List<object> list = ReadOfficialAccounts(src);
		if (list.Count == 0)
		{
			return new List<object>();
		}
		manager = manager ?? new AccountManager();
		List<object> list2 = new List<object>();
		foreach (PyDict item in list)
		{
			try
			{
				manager.AddAccount(item);
				list2.Add(item["name"]);
			}
			catch (Exception e)
			{
				Utils.log.Warning("导入账号失败 %s: %s", item["name"], Py.ExcStr(e));
			}
		}
		return list2;
	}

	public static PyDict Migrate(string officialRoot, object instanceName = null, bool wantVersions = true, bool wantAssets = true, bool wantAccounts = true)
	{
		string text = PPath.Norm(officialRoot);
		if (!IsDir(text))
		{
			throw new FileNotFoundError("官方启动器目录不存在: " + text);
		}
		Instance instance = new Instance(instanceName);
		if (!IsDir(instance.path))
		{
			instance.Create();
		}
		PyDict pyDict = PyDict.Of("versions", new List<object>(), "accounts", new List<object>());
		if (wantVersions)
		{
			pyDict["versions"] = ImportVersions(text, instanceName);
		}
		if (wantAssets)
		{
			string text2 = PPath.Join(text, "assets");
			if (IsDir(text2) && !new PPath(text2).Equals(new PPath(instance.AssetsDir())))
			{
				_copy_tree(text2, instance.AssetsDir());
			}
		}
		if (wantAccounts)
		{
			pyDict["accounts"] = ImportAccounts(text);
		}
		return pyDict;
	}
}
