using System;
using System.Collections.Generic;
using System.Linq;

namespace HelL.Core.Launcher;

public static class LoaderMeta
{
	public static readonly PyDict NEOFORGE_MC_MAP = PyDict.Of("1.20.1", "47.1", "1.20.2", "20.2", "1.20.3", "20.3", "1.20.4", "20.4", "1.20.5", "20.5", "1.20.6", "20.6", "1.21", "21.0", "1.21.1", "21.1");

	internal static object G(object o, string k)
	{
		if (o is PyDict pyDict)
		{
			return pyDict.Get(k);
		}
		throw new AttributeError("'" + Py.TypeName(o) + "' object has no attribute 'get'");
	}

	internal static IEnumerable<object> IterOr(object data)
	{
		if (!Py.Truthy(data))
		{
			return new List<object>();
		}
		if (data is PyDict pyDict)
		{
			return pyDict.Keys.Cast<object>().ToList();
		}
		if (data is string source)
		{
			return ((IEnumerable<char>)source).Select((Func<char, object>)((char c) => c.ToString())).ToList();
		}
		if (data is List<object> result)
		{
			return result;
		}
		throw new TypeError("'" + Py.TypeName(data) + "' object is not iterable");
	}

	public static List<object> ListLoaderVersions(DownloadManager dm, object mc_version, object loader)
	{
		dm = dm ?? new DownloadManager(2L);
		string mc = Py.Strip(Py.AsStr(Py.Or(mc_version, ""), "strip"));
		string text = Py.Lower(Py.Strip(Py.AsStr(Py.Or(loader, ""), "strip")));
		if (mc.Length != 0)
		{
			switch (text)
			{
			case "fabric":
				return _fabric(dm, mc);
			case "quilt":
				return _quilt(dm, mc);
			case "forge":
				return _forge(dm, mc);
			case "neoforge":
				return _neoforge(dm, mc);
			case "optifine":
			{
				List<object> list2 = new List<object>();
				{
					foreach (PyDict item in Optifine.ListBuilds(dm, mc))
					{
						list2.Add(PyDict.Of("id", Py.RStrip(Py.Str(item.Req("type")) + "_" + Py.Str(item.Req("patch")), "_"), "label", Py.Strip(Py.Str(item.Req("type")) + " " + Py.Str(item.Req("patch"))), "type", Py.Or(item.Get("type"), ""), "patch", Py.Or(item.Get("patch"), ""), "stable", true));
					}
					return list2;
				}
			}
			case "liteloader":
			{
				object obj = Py.Or(Liteloader.ListVersions(dm), new PyDict());
				if ((obj is PyDict pyDict) ? pyDict.ContainsKey(mc) : IterOr(obj).Any((object x) => Py.Eq(x, mc)))
				{
					List<object> list = new List<object>();
					list.Add(PyDict.Of("id", mc, "label", mc, "stable", true));
					return list;
				}
				return new List<object>();
			}
			default:
				return new List<object>();
			case "无":
			case "none":
			case "":
				break;
			}
		}
		return new List<object>();
	}

	private static List<object> _fabric(DownloadManager dm, string mc)
	{
		object data = dm.FetchJson("https://meta.fabricmc.net/v2/versions/loader/" + mc, 30L);
		List<object> list = new List<object>();
		foreach (object item in IterOr(data))
		{
			object obj = G(Py.Or(G(Py.Or(item, new PyDict()), "loader"), new PyDict()), "version");
			if (Py.Truthy(obj))
			{
				list.Add(PyDict.Of("id", obj, "label", obj, "stable", Py.Truthy(G(Py.Or(G(item, "loader"), new PyDict()), "stable"))));
			}
		}
		return list;
	}

	private static List<object> _quilt(DownloadManager dm, string mc)
	{
		object data = dm.FetchJson("https://meta.quiltmc.org/v3/versions/loader/" + mc, 30L);
		List<object> list = new List<object>();
		foreach (object item in IterOr(data))
		{
			object obj = G(Py.Or(G(Py.Or(item, new PyDict()), "loader"), new PyDict()), "version");
			if (Py.Truthy(obj))
			{
				list.Add(PyDict.Of("id", obj, "label", obj, "stable", true));
			}
		}
		return list;
	}

	private static List<object> _forge(DownloadManager dm, string mc)
	{
		List<string> list;
		try
		{
			list = Installer.BmclForgeArtifacts(dm.FetchJson("https://bmclapi2.bangbang93.com/forge/minecraft/" + mc, 30L), mc).Cast<string>().ToList();
		}
		catch (Exception ex) when (!(ex is TaskCancelled))
		{
			list = new List<string>();
		}
		if (list.Count == 0)
		{
			(string, bool)[] array = new(string, bool)[2]
			{
				("https://bmclapi2.bangbang93.com/maven/net/minecraftforge/forge/maven-metadata.xml", true),
				("https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml", false)
			};
			for (int i = 0; i < array.Length; i++)
			{
				var (url, expand) = array[i];
				string xml;
				try
				{
					xml = dm.FetchText(url, 40L, expand);
				}
				catch (Exception ex2) when (!(ex2 is TaskCancelled))
				{
					continue;
				}
				list = (from v in Installer.ParseMavenVersions(xml)
					where v == mc || v.StartsWith(mc + "-", StringComparison.Ordinal)
					select v).ToList();
				if (list.Count > 0)
				{
					break;
				}
			}
		}
		list = (from x in list.Select((string v, int item) => (v: v, i: item)).OrderBy(((string v, int i) x) => ((string, int))x, Comparer<(string, int)>.Create(delegate((string v, int i) a, (string v, int i) b)
			{
				int num = Installer.ForgeSortCompare(b.v, a.v, mc);
				return (num != 0) ? num : a.i.CompareTo(b.i);
			}))
			select x.v).ToList();
		return ((IEnumerable<string>)list).Select((Func<string, object>)((string v) => PyDict.Of("id", v, "label", v, "stable", !Py.Lower(v).Contains("-pre")))).ToList();
	}

	public static string NeoforgePrefix(string mc_version)
	{
		string text = Py.Strip(mc_version ?? "");
		if (text.Length == 0)
		{
			return "";
		}
		object o = NEOFORGE_MC_MAP.Get(text);
		if (Py.Truthy(o))
		{
			return Py.Str(o);
		}
		string calver = Installer.NeoforgeCalverPrefix(text);
		if (calver != null)
		{
			return calver;
		}
		long[] array = Installer._mc_tuple(text);
		if ((array[0] != 1) ? (array[0] > 1) : ((array[1] != 20) ? (array[1] > 20) : (array[2] >= 2)))
		{
			return string.Join(".", text.Split('.').Skip(1));
		}
		return "";
	}

	public static List<string> FilterNeoforgeVersions(IEnumerable<object> versions, string mc_version)
	{
		List<string> list = (from v in versions ?? new List<object>()
			select Py.Strip(Py.Str(v)) into v
			where v.Length > 0
			select v).ToList();
		string mc = Py.Strip(mc_version ?? "");
		if (mc.Length == 0 || list.Count == 0)
		{
			return new List<string>();
		}
		string prefix = NeoforgePrefix(mc);
		List<string> list2 = list.Where((string v) => prefix.Length > 0 && v.StartsWith(prefix + ".", StringComparison.Ordinal)).ToList();
		if (list2.Count == 0)
		{
			list2 = list.Where((string v) => v.StartsWith(mc + "-", StringComparison.Ordinal)).ToList();
		}
		List<string> list3 = list2.Skip(Math.Max(0, list2.Count - 80)).ToList();
		list3.Reverse();
		return list3;
	}

	private static List<object> _neoforge(DownloadManager dm, string mc)
	{
		List<string> source;
		try
		{
			source = Installer.ParseMavenVersions(dm.FetchText("https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml", 30L, expand: false));
		}
		catch (Exception ex) when (!(ex is TaskCancelled))
		{
			source = new List<string>();
		}
		return ((IEnumerable<string>)FilterNeoforgeVersions(source.Cast<object>(), mc)).Select((Func<string, object>)((string v) => PyDict.Of("id", v, "label", v, "stable", !Py.Lower(v).Contains("beta")))).ToList();
	}
}
