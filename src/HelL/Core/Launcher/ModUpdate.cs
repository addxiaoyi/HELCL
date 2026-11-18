using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HelL.Core.Launcher.Ai;

namespace HelL.Core.Launcher;

public static class ModUpdate
{
	public const string API = "https://api.modrinth.com/v2";

	private static string _game_mods(Instance instance, string mods_path)
	{
		if (!Py.Truthy(mods_path))
		{
			return PPath.Join(instance.path, "mods");
		}
		return PPath.Norm(mods_path);
	}

	public static List<string> ResolveTarget(Instance instance, object mc_version = null, object loader = null)
	{
		string text = Py.Strip(Py.S(mc_version));
		string text2 = Py.Strip(Py.S(loader));
		if (text.Length == 0)
		{
			try
			{
				text = Mods.DetectMcVersion(instance) ?? "";
			}
			catch (Exception)
			{
				text = "";
			}
		}
		if (text2.Length == 0)
		{
			try
			{
				text2 = Mods.DetectLoader(instance) ?? "";
			}
			catch (Exception)
			{
				text2 = "";
			}
		}
		return new List<string> { text, text2 };
	}

	public static List<object> CheckUpdates(Instance instance, DownloadManager dm = null, string mods_path = null, object mc_version = null, object loader = null)
	{
		dm = dm ?? new DownloadManager(4L);
		List<object> list = new List<object>();
		string text = _game_mods(instance, mods_path);
		if (!LocalFs.IsDir(text))
		{
			return list;
		}
		List<string> list2 = ResolveTarget(instance, mc_version, loader);
		string mc_version2 = list2[0];
		string loader2 = list2[1];
		foreach (PyDict item in (mods_path == null) ? Mods.ListInstanceModEntries(instance) : _entries(text))
		{
			string text2 = PPath.Join(text, Py.Str(item["filename"]));
			if (!LocalFs.IsFile(text2) || !Py.Truthy(item["enabled"]))
			{
				continue;
			}
			PyDict info = Conflict.InspectJar(text2);
			string digest = Utils.Sha1File(text2);
			PyDict pyDict2 = _modrinth_update(dm, text2, digest, mc_version2, loader2, info);
			if (pyDict2 != null)
			{
				list.Add(pyDict2);
				continue;
			}
			pyDict2 = _curseforge_update(dm, text2, mc_version2, loader2, info);
			if (pyDict2 != null)
			{
				list.Add(pyDict2);
			}
		}
		return list;
	}

	private static PyDict _modrinth_update(DownloadManager dm, string path, string digest, string mc_version, string loader, PyDict info)
	{
		object obj;
		try
		{
			obj = dm.FetchJson("https://api.modrinth.com/v2/version_file/" + digest, 12L);
		}
		catch (Exception)
		{
			return null;
		}
		if (!(obj is PyDict pyDict))
		{
			return null;
		}
		object obj2 = pyDict["project_id"];
		object obj3 = Py.Or(pyDict["version_number"], pyDict["name"], "");
		if (!Py.Truthy(obj2))
		{
			return null;
		}
		List<string> list = new List<string>();
		if (mc_version.Length > 0)
		{
			list.Add("game_versions=[\"" + mc_version + "\"]");
		}
		if (loader.Length > 0)
		{
			list.Add("loaders=[\"" + loader + "\"]");
		}
		string text = ((list.Count > 0) ? ("?" + string.Join("&", list)) : "");
		object obj4;
		try
		{
			obj4 = dm.FetchJson("https://api.modrinth.com/v2/project/" + Py.Str(obj2) + "/version" + text, 12L);
		}
		catch (Exception)
		{
			return null;
		}
		if (!(obj4 is List<object> { Count: not 0 } list2))
		{
			return null;
		}
		PyDict pyDict2 = (PyDict)list2[0];
		object obj5 = Py.Or(pyDict2["version_number"], pyDict2["name"], "");
		if (Py.Eq(pyDict2["id"], pyDict["id"]))
		{
			return null;
		}
		PyDict pyDict3 = null;
		foreach (PyDict item in Py.ListOr(Py.Or(pyDict2["files"], new List<object>())))
		{
			if (Py.Truthy(item["primary"]) || pyDict3 == null)
			{
				pyDict3 = item;
			}
		}
		PyDict pyDict5 = pyDict3 ?? new PyDict();
		PPath pPath = new PPath(path);
		return PyDict.Of("filename", pPath.Name, "name", Py.Or(info["name"], pPath.Stem), "current", obj3, "latest", obj5, "project", obj2, "url", Py.Or(pyDict5["url"], ""), "sha1", Py.Or(Py.DictOr(Py.Or(pyDict5["hashes"], new PyDict()))["sha1"], ""), "size", Py.Or(pyDict5["size"], 0L), "filename_new", Py.Or(pyDict5["filename"], ""), "source", "modrinth", "mc_version", mc_version, "game_versions", Py.ListCtor(Py.Or(pyDict2["game_versions"], new List<object>())));
	}

	public static long Murmur2(byte[] data, uint seed = 1u)
	{
		int num = data.Length;
		uint num2 = seed ^ (uint)num;
		int num3 = num / 4;
		for (int i = 0; i < num3; i++)
		{
			uint num4 = BitConverter.ToUInt32(data, i * 4);
			if (!BitConverter.IsLittleEndian)
			{
				num4 = (num4 >> 24) | ((num4 >> 8) & 0xFF00) | ((num4 << 8) & 0xFF0000) | (num4 << 24);
			}
			num4 *= 1540483477;
			num4 ^= num4 >> 24;
			num4 *= 1540483477;
			num2 *= 1540483477;
			num2 ^= num4;
		}
		int num5 = num - num3 * 4;
		int num6 = num3 * 4;
		if (num5 >= 3)
		{
			num2 ^= (uint)(data[num6 + 2] << 16);
		}
		if (num5 >= 2)
		{
			num2 ^= (uint)(data[num6 + 1] << 8);
		}
		if (num5 >= 1)
		{
			num2 ^= data[num6];
			num2 *= 1540483477;
		}
		num2 ^= num2 >> 13;
		num2 *= 1540483477;
		num2 ^= num2 >> 15;
		return num2;
	}

	public static long CfFingerprint(string path)
	{
		return Murmur2((from b in File.ReadAllBytes(PPath.Abs(path))
			where b != 9 && b != 10 && b != 13 && b != 32
			select b).ToArray());
	}

	private static PyDict _curseforge_update(DownloadManager dm, string path, string mc_version, string loader, PyDict info)
	{
		long num;
		try
		{
			num = CfFingerprint(path);
		}
		catch (IOException)
		{
			return null;
		}
		catch (UnauthorizedAccessException)
		{
			return null;
		}
		object a;
		try
		{
			a = Mods.CfPost(dm, "/fingerprints", PyDict.Of("fingerprints", new List<object> { num }), null, 20L);
		}
		catch (Exception)
		{
			return null;
		}
		List<object> list = Py.ListOr(Py.Or(Py.DictOr(Py.Or(Py.DictOr(Py.Or(a, new PyDict()))["data"], new PyDict()))["exactMatches"], new List<object>()));
		if (list.Count == 0)
		{
			return null;
		}
		PyDict obj = (PyDict)list[0];
		PyDict pyDict = Py.DictOr(Py.Or(obj["file"], new PyDict()));
		object obj2 = Py.Or(obj["id"], pyDict["modId"]);
		if (!Py.Truthy(obj2))
		{
			return null;
		}
		List<object> list2;
		try
		{
			list2 = Mods.CfFiles(dm, obj2, null, (mc_version.Length > 0) ? mc_version : null, null, 20L);
		}
		catch (Exception)
		{
			list2 = new List<object>();
		}
		if (list2.Count == 0)
		{
			return null;
		}
		PyDict pyDict2 = (PyDict)list2[0];
		PPath pPath = new PPath(path);
		if (Py.Str(pyDict2["id"]) == Py.Str(pyDict["id"]))
		{
			return null;
		}
		string text = Py.Str(Py.Or(pyDict2["fileName"], pPath.Name));
		string text2 = Py.Str(Py.Or(pyDict2["downloadUrl"], ""));
		if (text2.Length == 0)
		{
			List<object> list3 = Mods.CfModDownloadUrls(obj2, pyDict2["id"], text);
			text2 = ((list3.Count > 0) ? ((string)list3[0]) : "");
		}
		return PyDict.Of("filename", pPath.Name, "name", Py.Or(info["name"], pPath.Stem), "current", Py.Or(pyDict["displayName"], pyDict["fileName"], pPath.Stem), "latest", Py.Or(pyDict2["displayName"], text), "project", Py.Str(obj2), "url", text2, "sha1", "", "size", Py.Or(pyDict2["fileLength"], 0L), "filename_new", text, "source", "curseforge", "file_id", pyDict2["id"], "mc_version", mc_version, "game_versions", ((IEnumerable<object>)Py.ListOr(Py.Or(pyDict2["gameVersions"], new List<object>()))).Select((Func<object, object>)((object g) => Py.Str(g))).ToList());
	}

	private static List<object> _entries(string folder)
	{
		List<object> list = new List<object>();
		foreach (string item in LocalFs.SortedChildren(folder))
		{
			if (LocalFs.IsFile(item))
			{
				string name = new PPath(item).Name;
				string text = Py.Lower(name);
				if (text.EndsWith(".jar", StringComparison.Ordinal))
				{
					list.Add(PyDict.Of("filename", name, "enabled", true));
				}
				else if (text.EndsWith(".jar.disabled", StringComparison.Ordinal))
				{
					list.Add(PyDict.Of("filename", name, "enabled", false));
				}
			}
		}
		return list;
	}

	public static string ApplyUpdate(Instance instance, PyDict row, DownloadManager dm = null, string mods_path = null)
	{
		dm = dm ?? new DownloadManager(2L);
		string text = _game_mods(instance, mods_path);
		object obj = row["url"];
		if (!Py.Truthy(obj))
		{
			throw new RuntimeError("没有可下载的更新地址");
		}
		string text2 = Py.Str(Py.Or(row["filename_new"], row["filename"]));
		string text3 = PPath.Join(text, text2);
		object obj2 = row["size"];
		dm.Download(obj, text3, Py.Truthy(row["sha1"]) ? Py.Str(row["sha1"]) : null, Py.Truthy(obj2) ? new long?(Py.Int(obj2)) : ((long?)null));
		if (!LocalFs.IsFile(text3) || LocalFs.Size(text3) <= 0)
		{
			throw new RuntimeError("更新包没有正确落地，已保留原文件: " + text2);
		}
		long num = Py.Int(Py.Or(obj2, 0L));
		if (num > 0 && LocalFs.Size(text3) != num)
		{
			throw new RuntimeError("更新包大小与清单不符（" + LocalFs.Size(text3) + " != " + num + "），已保留原文件");
		}
		string text4 = PPath.Join(text, Py.Str(row.Req("filename")));
		if (new PPath(text4).Resolve().ToString() != new PPath(text3).Resolve().ToString() && LocalFs.IsFile(text4))
		{
			LocalFs.Unlink(text4);
		}
		return new PPath(text3).Name;
	}
}
