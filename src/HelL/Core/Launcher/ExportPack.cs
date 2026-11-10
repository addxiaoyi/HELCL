using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace HelL.Core.Launcher;

public static class ExportPack
{
	public const string MODRINTH_HASH = "https://api.modrinth.com/v2/version_file/{hash}";

	public static readonly string[] OVERRIDE_DIRS = new string[4] { "config", "resourcepacks", "shaderpacks", "datapacks" };

	public static string ExportMrpack(Instance inst, string dest, DownloadManager dm = null, Action<string, long, long> on_note = null)
	{
		dest = PPath.Norm(dest);
		Utils.EnsureDir(new PPath(dest).Parent.ToString());
		dm = dm ?? new DownloadManager(4L);
		string text = PPath.Join(inst.path, "mods");
		List<object> list = new List<object>();
		List<KeyValuePair<string, string>> list2 = new List<KeyValuePair<string, string>>();
		if (LocalFs.IsDir(text))
		{
			List<string> list3 = (from p in LocalFs.Children(text)
				where LocalFs.IsFile(p) && Py.Lower(new PPath(p).Name).EndsWith(".jar", StringComparison.Ordinal)
				select p).ToList();
			for (int num = 0; num < list3.Count; num++)
			{
				string text2 = list3[num];
				string name = new PPath(text2).Name;
				on_note?.Invoke("解析模组 " + name, num, list3.Count);
				string text3 = Utils.Sha1File(text2);
				object obj;
				try
				{
					obj = dm.FetchJson("https://api.modrinth.com/v2/version_file/{hash}".Replace("{hash}", text3), 15L);
				}
				catch (Exception)
				{
					obj = null;
				}
				if (obj is PyDict pyDict)
				{
					PyDict pyDict2 = null;
					foreach (PyDict item in Py.ListOr(Py.Or(pyDict["files"], new List<object>())))
					{
						if (Py.Truthy(item["primary"]) || pyDict2 == null)
						{
							pyDict2 = item;
						}
					}
					if (pyDict2 != null && Py.Truthy(pyDict2["url"]))
					{
						list.Add(PyDict.Of("path", "mods/" + name, "hashes", PyDict.Of("sha1", text3, "sha512", Py.Or(Py.DictOr(Py.Or(pyDict2["hashes"], new PyDict()))["sha512"], "")), "downloads", new List<object> { pyDict2["url"] }, "fileSize", Py.Int(Py.Or(pyDict2["size"], LocalFs.Size(text2)))));
						continue;
					}
				}
				list2.Add(new KeyValuePair<string, string>("mods/" + name, text2));
			}
		}
		string[] oVERRIDE_DIRS = OVERRIDE_DIRS;
		foreach (string text4 in oVERRIDE_DIRS)
		{
			string text5 = PPath.Join(inst.path, text4);
			if (!LocalFs.IsDir(text5))
			{
				continue;
			}
			foreach (string item2 in LocalFs.RglobFiles(text5))
			{
				string text6 = PPath.Join(text4, new PPath(item2).RelativeTo(text5).ToString());
				list2.Add(new KeyValuePair<string, string>(text6.Replace("\\", "/"), item2));
			}
		}
		PyDict pyDict4 = Py.DictOr(Py.Or(inst.Meta(), new PyDict()));
		PyDict pyDict5 = (pyDict4["modpack"] as PyDict) ?? new PyDict();
		object obj2 = Py.Or(pyDict5["name"], inst.name);
		object obj3 = Py.Or(pyDict5["version"], "1.0.0");
		object obj4 = Py.Or(pyDict5["mc_version"], pyDict4["mc_version"], "");
		object o = Py.Or(pyDict5["loader"], "");
		PyDict pyDict6 = (Py.Truthy(obj4) ? PyDict.Of("minecraft", obj4) : new PyDict());
		if (Py.Truthy(o))
		{
			pyDict6[Py.Lower(Py.Str(o))] = Py.Or(pyDict5["loader_version"], "");
		}
		PyDict pyDict7 = new PyDict();
		foreach (KeyValuePair<string, object> item3 in pyDict6)
		{
			if (Py.Truthy(item3.Value))
			{
				pyDict7[item3.Key] = item3.Value;
			}
		}
		PyDict obj5 = PyDict.Of("formatVersion", 1L, "game", "minecraft", "versionId", obj3, "name", obj2, "summary", "由 HelL 从实例 " + inst.name + " 导出", "files", list.Where((object f) => Py.Truthy(Py.DictOr(((PyDict)f)["hashes"])["sha1"])).ToList(), "dependencies", pyDict7);
		using (FileStream stream = new FileStream(PPath.Abs(dest), FileMode.Create, FileAccess.ReadWrite))
		{
			using ZipArchive zipArchive = new ZipArchive(stream, ZipArchiveMode.Create);
			using (Stream stream2 = zipArchive.CreateEntry("modrinth.index.json", CompressionLevel.Optimal).Open())
			{
				byte[] bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(PyJson.Dumps(obj5, 2, ensureAscii: false));
				stream2.Write(bytes, 0, bytes.Length);
			}
			foreach (KeyValuePair<string, string> item4 in list2)
			{
				ZipArchiveEntry zipArchiveEntry = zipArchive.CreateEntry("overrides/" + item4.Key, CompressionLevel.Optimal);
				DateTime dateTime = File.GetLastWriteTime(PPath.Abs(item4.Value));
				if (dateTime.Year < 1980)
				{
					dateTime = new DateTime(1980, 1, 1);
				}
				zipArchiveEntry.LastWriteTime = dateTime;
				using Stream destination = zipArchiveEntry.Open();
				using FileStream fileStream = File.OpenRead(PPath.Abs(item4.Value));
				fileStream.CopyTo(destination);
			}
		}
		on_note?.Invoke("导出完成", 1L, 1L);
		return dest;
	}
}
