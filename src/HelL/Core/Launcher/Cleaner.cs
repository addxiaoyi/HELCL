using System;
using System.Collections.Generic;
using System.IO;

namespace HelL.Core.Launcher;

public static class Cleaner
{
	private static string Low(string libs, string rel)
	{
		return Py.Lower(new PPath(PPath.Join(libs, rel)).Resolve().ToString());
	}

	private static HashSet<string> _lib_paths(Instance instance)
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
		string libs = instance.LibrariesDir();
		foreach (string item in instance.InstalledIds())
		{
			PyDict pyDict = Py.DictOr(instance.VersionJson(item));
			PyDict pyDict2;
			try
			{
				pyDict2 = Manifest.ResolveInherits(pyDict, (string pid) => instance.VersionJson(pid) as PyDict);
			}
			catch (Exception)
			{
				pyDict2 = pyDict;
			}
			foreach (object item2 in Py.ListOr(pyDict2["libraries"]))
			{
				if (!(item2 is PyDict pyDict3))
				{
					continue;
				}
				PyDict pyDict4 = Py.DictOr(Py.Or(pyDict3["downloads"], new PyDict()));
				object o = Py.DictOr(Py.Or(pyDict4["artifact"], new PyDict()))["path"];
				if (!Py.Truthy(o) && Py.Truthy(pyDict3["name"]) && !Py.Truthy(pyDict3["natives"]))
				{
					o = Utils.MavenArtifactPath((string)pyDict3["name"]);
				}
				if (Py.Truthy(o))
				{
					hashSet.Add(Low(libs, Py.Str(o)));
				}
				string text = (Py.Truthy(pyDict3["natives"]) ? Installer.SelectNativeClassifier(pyDict3) : null);
				if (Py.Truthy(text) && Py.Truthy(pyDict3["name"]))
				{
					hashSet.Add(Low(libs, Installer.NativesJarRelpath(pyDict3, text)));
				}
				foreach (object value in Py.DictOr(Py.Or(pyDict4["classifiers"], new PyDict())).Values)
				{
					if (value is PyDict pyDict5 && Py.Truthy(pyDict5["path"]))
					{
						hashSet.Add(Low(libs, Py.Str(pyDict5["path"])));
					}
				}
			}
		}
		return hashSet;
	}

	public static PyDict Preview()
	{
		List<string> list = new List<string>();
		List<string> list2 = new List<string>();
		List<string> list3 = new List<string>();
		HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
		List<string> list4 = new List<string>();
		foreach (object item2 in Instances.ListInstances())
		{
			Instance instance = new Instance(item2);
			hashSet.UnionWith(_lib_paths(instance));
			string item = new PPath(instance.LibrariesDir()).Resolve().ToString();
			if (!list4.Contains(item))
			{
				list4.Add(item);
			}
		}
		foreach (string item3 in list4)
		{
			if (!LocalFs.IsDir(item3))
			{
				continue;
			}
			foreach (string item4 in LocalFs.RglobFiles(item3))
			{
				if (Py.Lower(new PPath(item4).Suffix) == ".part")
				{
					list2.Add(item4);
				}
				else if (!hashSet.Contains(Py.Lower(new PPath(item4).Resolve().ToString())))
				{
					list.Add(item4);
				}
			}
		}
		string cache_dir = Config.CONFIG.cache_dir;
		if (LocalFs.IsDir(cache_dir))
		{
			foreach (string item5 in LocalFs.RglobFiles(cache_dir))
			{
				if (new PPath(item5).Name.EndsWith(".part", StringComparison.Ordinal))
				{
					list2.Add(item5);
				}
			}
			foreach (string item6 in LocalFs.Children(cache_dir))
			{
				string name = new PPath(item6).Name;
				if (name.StartsWith("HelL-", StringComparison.Ordinal) && name.EndsWith(".bin", StringComparison.Ordinal))
				{
					list3.Add(item6);
				}
			}
		}
		List<object> reference;
		long num = Stat(list, out reference);
		List<object> reference2;
		long num2 = Stat(list2, out reference2);
		List<object> reference3;
		long num3 = Stat(list3, out reference3);
		return PyDict.Of("unused_libraries", reference, "parts", reference2, "cache", reference3, "bytes", num + num2 + num3, "count", (long)(reference.Count + reference2.Count + reference3.Count));
		static long Stat(List<string> paths, out List<object> reference4)
		{
			long num4 = 0L;
			reference4 = new List<object>();
			foreach (string path in paths)
			{
				long length;
				try
				{
					length = new FileInfo(PPath.Abs(path)).Length;
				}
				catch (Exception)
				{
					continue;
				}
				num4 += length;
				reference4.Add(PyDict.Of("path", path, "bytes", length));
			}
			return num4;
		}
	}

	public static PyDict Apply(object kinds = null)
	{
		List<string> list = new List<string>();
		IEnumerable<object> enumerable2;
		if (!Py.Truthy(kinds))
		{
			IEnumerable<object> enumerable = new List<object> { "parts", "cache", "unused_libraries" };
			enumerable2 = enumerable;
		}
		else
		{
			IEnumerable<object> enumerable = Py.ListCtor(kinds);
			enumerable2 = enumerable;
		}
		foreach (object item2 in enumerable2)
		{
			string item = Py.Str(item2);
			if (!list.Contains(item))
			{
				list.Add(item);
			}
		}
		PyDict pyDict = Preview();
		long num = 0L;
		long num2 = 0L;
		foreach (string item3 in list)
		{
			if (item3 != "unused_libraries" && item3 != "parts" && item3 != "cache")
			{
				continue;
			}
			foreach (PyDict item4 in (List<object>)pyDict[item3])
			{
				string text = PPath.Abs(Py.Str(item4["path"]));
				try
				{
					long length = new FileInfo(text).Length;
					if (!File.Exists(text))
					{
						throw new IOException();
					}
					File.Delete(text);
					num++;
					num2 += length;
				}
				catch (Exception)
				{
				}
			}
		}
		return PyDict.Of("removed", num, "bytes", num2);
	}
}
