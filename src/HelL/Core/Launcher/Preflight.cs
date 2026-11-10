using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using HelL.Core.Launcher.Ai;

namespace HelL.Core.Launcher;

public static class Preflight
{
	private struct MEMORYSTATUSEX
	{
		public uint dwLength;

		public uint dwMemoryLoad;

		public ulong ullTotalPhys;

		public ulong ullAvailPhys;

		public ulong ullTotalPageFile;

		public ulong ullAvailPageFile;

		public ulong ullTotalVirtual;

		public ulong ullAvailVirtual;

		public ulong ullAvailExtendedVirtual;
	}

	private const int _MAX_LIB_CHECK = 400;

	private const int _MAX_ASSET_SAMPLE = 80;

	private const int _MAX_MOD_JARS = 120;

	public static Func<string, long?> FreeBytesHook;

	private static PyDict _item(string level, string code, string title, string detail)
	{
		return PyDict.Of("level", level, "code", code, "title", title, "detail", detail);
	}

	private static long? FreeBytes(string path)
	{
		Func<string, long?> freeBytesHook = FreeBytesHook;
		if (freeBytesHook != null)
		{
			return freeBytesHook(path);
		}
		try
		{
			if (GetDiskFreeSpaceEx(Utils.AbsPath(path), out var avail, out var _, out var _))
			{
				return (long)avail;
			}
			return null;
		}
		catch (Exception)
		{
			return null;
		}
	}

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool GetDiskFreeSpaceEx(string dir, out ulong avail, out ulong total, out ulong free);

	[DllImport("kernel32.dll")]
	private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX st);

	public static PyDict CheckLaunch(Instance inst, object version0, long memory_mb = 0L, string java_exe = "")
	{
		List<object> list = new List<object>();
		string text = Py.Strip(Py.S(version0));
		string path = inst.path;
		if (!new PPath(path).IsDir())
		{
			list.Add(_item("error", "no_instance", "实例目录不存在", PPath.Norm(path)));
			return PyDict.Of("ok", false, "items", list);
		}
		try
		{
			string path2 = Utils.AbsPath(PPath.Join(path, ".tipcl_write_probe"));
			File.WriteAllText(path2, "ok");
			if (File.Exists(path2))
			{
				File.Delete(path2);
			}
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
		{
			list.Add(_item("error", "not_writable", "实例目录不可写", ex.Message));
		}
		PyDict pyDict = null;
		if (text.Length == 0)
		{
			list.Add(_item("error", "no_version", "未选择版本", "请先到「下载 → 原版游戏」安装版本"));
		}
		else
		{
			PyDict pyDict2 = inst.VersionJson(text) as PyDict;
			if (!Py.Truthy(pyDict2))
			{
				list.Add(_item("error", "no_version_json", "版本未安装", "找不到 " + text + " 的版本 JSON"));
			}
			else
			{
				try
				{
					pyDict = Manifest.ResolveInherits(pyDict2, (string pid) => inst.VersionJson(pid) as PyDict);
				}
				catch (Exception)
				{
					pyDict = pyDict2;
				}
				string client = McLauncher.ClientJarPath(inst, text, pyDict2, pyDict);
				bool ownClient = string.Equals(Utils.AbsPath(client), Utils.AbsPath(PPath.Join(inst.VersionsDir(), text, text + ".jar")), StringComparison.OrdinalIgnoreCase);
				var clientInfo = ((ownClient ? pyDict2 : pyDict).Get("downloads") as PyDict)?.Get("client") as PyDict;
				if (!new PPath(client).IsFile()) list.Add(_item("error", "no_client_jar", "游戏主文件缺失，请修复此版本。", client));
				else if (clientInfo != null && !Utils.FileMatches(client, clientInfo.Get("sha1"), clientInfo.Get("size")))
					list.Add(_item("error", "client_corrupt", "游戏主文件校验失败，请修复此版本。", client));
			}
		}
		long? num = FreeBytes(path);
		if (num.HasValue)
		{
			long num2 = num.Value / 1048576;
			if (num2 < 512)
			{
				list.Add(_item("error", "disk_low", "磁盘空间不足", "实例所在盘仅剩约 " + num2 + " MB，至少需要 512 MB"));
			}
			else if (num2 < 2048)
			{
				list.Add(_item("warn", "disk_warn", "磁盘空间偏低", "实例所在盘剩余约 " + num2 + " MB，建议清理后再装大整合包"));
			}
		}
		string text3 = _resolve_mods_dir(inst, text);
		if (new PPath(text3).IsDir())
		{
			List<string> source = Children(text3);
			List<string> list2 = (from p in source
				where Directory.Exists(Utils.AbsPath(p)) && !new PPath(p).Name.StartsWith(".")
				select new PPath(p).Name).ToList();
			if (list2.Count > 0)
			{
				list.Add(_item("warn", "mod_unzipped", "Mods 被解压成了文件夹", "直接放整个 .jar/.zip 即可。请删掉这些文件夹：\n - " + string.Join("\n - ", list2.Take(12))));
			}
			List<string> list3 = source.Where((string p) => Py.Lower(new PPath(p).Suffix) == ".jar").ToList();
			string vl = Py.Lower(text);
			bool flag = new string[6] { "forge", "fabric", "quilt", "neoforge", "optifine", "liteloader" }.Any((string t) => vl.Contains(t));
			if (list3.Count > 0 && text.Length > 0 && !flag)
			{
				list.Add(_item("warn", "vanilla_mods", "原版版本不会加载模组", "mods 里有 " + list3.Count + " 个 jar，但当前版本名像原版。请安装 Fabric/Forge 等加载器。"));
			}
		}
		if (Py.Truthy(pyDict) && text.Length > 0)
		{
			_check_libraries(inst, pyDict, list);
			_check_assets(inst, pyDict, list);
			_check_natives(inst, text, pyDict, list);
		}
		if (new PPath(text3).IsDir() && text.Length > 0)
		{
			_check_mod_conflicts(text3, list);
		}
		if (pyDict != null && text.Length > 0)
			list.Add(LaunchJava.Inspect(pyDict, LaunchJava.Preference(inst, text, java_exe)));
		long num4 = memory_mb;
		if (num4 > 0 && Utils.IS_WINDOWS)
		{
			try
			{
				MEMORYSTATUSEX st = new MEMORYSTATUSEX
				{
					dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX))
				};
				if (GlobalMemoryStatusEx(ref st))
				{
					long num5 = (long)(st.ullAvailPhys / 1048576);
					if (num4 + 1024 > num5)
					{
						list.Add(_item("warn", "memory_high", "分配内存接近可用物理内存", "游戏 " + num4 + " MB，系统当前可用约 " + num5 + " MB，可能触发交换卡顿"));
					}
				}
			}
			catch (Exception)
			{
			}
		}
		bool flag2 = !list.Any((object it) => Py.Eq(((PyDict)it)["level"], "error"));
		if (list.Count == 0)
		{
			list.Add(_item("ok", "ready", "预检通过", "未发现阻塞问题"));
		}
		return PyDict.Of("ok", flag2, "items", list);
	}

	private static List<string> Children(string dir)
	{
		try
		{
			List<string> list = Directory.EnumerateFileSystemEntries(Utils.AbsPath(dir)).Select(Path.GetFileName).ToList();
			list.Sort(string.CompareOrdinal);
			return list.Select((string n) => PPath.Join(dir, n)).ToList();
		}
		catch (Exception)
		{
			return new List<string>();
		}
	}

	private static string _resolve_mods_dir(Instance inst, string version)
	{
		if (version.Length > 0)
		{
			try
			{
				return PPath.Norm(VersionSettings.ModsDir(inst, version));
			}
			catch (Exception)
			{
			}
		}
		return PPath.Join(inst.path, "mods");
	}

	private static void _check_libraries(Instance inst, PyDict resolved, List<object> items)
	{
		string text = inst.LibrariesDir();
		List<string> list = new List<string>();
		List<string> list2 = new List<string>();
		int num = 0;
		foreach (PyDict item in Installer.Libs(resolved))
		{
			if (num >= 400)
			{
				break;
			}
			object obj = item.Get("clientreq");
			if ((obj is bool && !(bool)obj) || !Utils.CheckRules(item.Get("rules")))
			{
				continue;
			}
			object o = item.Get("name");
			if (!Py.Truthy(o))
			{
				continue;
			}
			PyDict pyDict = Py.DictOr(Py.Or(item.Get("downloads"), new PyDict()));
			PyDict pyDict2 = Py.DictOr(Py.Or(pyDict.Get("artifact"), new PyDict()));
			if (pyDict2.Count > 0 || (pyDict.Count == 0 && !Py.Truthy(item.Get("natives"))))
			{
				string text2 = (Py.Truthy(pyDict2.Get("path")) ? Py.Str(pyDict2["path"]) : Utils.MavenArtifactPath(Py.Str(o)));
				string text3 = PPath.Join(text, text2);
				object obj2 = pyDict2.Get("sha1");
				object obj3 = pyDict2.Get("size");
				num++;
				if (!new PPath(text3).IsFile())
				{
					list.Add(new PPath(text2).Name);
				}
				else if ((Py.Truthy(obj2) || obj3 != null) && !Utils.FileMatches(text3, obj2, obj3))
				{
					list2.Add(new PPath(text2).Name);
				}
			}
			string text4 = Installer.SelectNativeClassifier(item);
			if (string.IsNullOrEmpty(text4) || num >= 400)
			{
				continue;
			}
			PyDict pyDict3 = Py.DictOr(Py.Or(pyDict.Get("classifiers"), new PyDict()));
			PyDict pyDict4 = Py.DictOr(Py.Or(pyDict3.Get(text4), new PyDict()));
			if (pyDict4.Count == 0)
			{
				foreach (KeyValuePair<string, object> item2 in pyDict3)
				{
					if (Installer.SubstNativeKey(item2.Key) == text4)
					{
						pyDict4 = Py.DictOr(Py.Or(item2.Value, new PyDict()));
						break;
					}
				}
			}
			string text5 = (Py.Truthy(pyDict4.Get("path")) ? Py.Str(pyDict4["path"]) : Utils.MavenArtifactPath(Py.Str(o) + ":" + text4));
			string text6 = PPath.Join(text, text5);
			num++;
			if (!new PPath(text6).IsFile())
			{
				list.Add(new PPath(text5).Name);
			}
			else if ((Py.Truthy(pyDict4.Get("sha1")) || pyDict4.Get("size") != null) && !Utils.FileMatches(text6, pyDict4.Get("sha1"), pyDict4.Get("size")))
			{
				list2.Add(new PPath(text5).Name);
			}
		}
		if (list.Count > 0)
		{
			string text7 = string.Join("\n - ", list.Take(10));
			string text8 = ((list.Count > 10) ? ("\n…共缺 " + list.Count + " 个") : "");
			string level = ((list.Count >= 3) ? "error" : "warn");
			items.Add(_item(level, "libs_missing", "依赖库缺失", "缺少以下库文件，请到版本页点「修复」：\n - " + text7 + text8));
		}
		if (list2.Count > 0)
		{
			string text9 = string.Join("\n - ", list2.Take(8));
			items.Add(_item("error", "libs_hash", "依赖库校验不一致", "以下库 sha1/大小与清单不符，可能损坏：\n - " + text9 + "\n建议修复该版本。"));
		}
	}

	private static void _check_assets(Instance inst, PyDict resolved, List<object> items)
	{
		PyDict pyDict = Py.DictOr(Py.Or(resolved.Get("assetIndex"), new PyDict()));
		if (pyDict.Count == 0)
		{
			return;
		}
		string text = inst.AssetsDir();
		string text2 = Py.Str(Py.Or(pyDict.Get("id"), ""));
		string text3 = PPath.Join(text, "indexes", text2 + ".json");
		if (!new PPath(text3).IsFile())
		{
			items.Add(_item("error", "assets_index_missing", "资源索引缺失", "找不到 assets/indexes/" + text2 + ".json，请修复该版本。"));
			return;
		}
		if (Py.Truthy(pyDict.Get("sha1")) && !Utils.FileMatches(text3, pyDict.Get("sha1"), pyDict.Get("size")))
		{
			items.Add(_item("warn", "assets_index_hash", "资源索引校验失败", text2 + ".json 与清单不符，建议修复该版本。"));
		}
		PyDict pyDict2 = Py.DictOr(Py.Or(Py.DictOr(Py.Or(Utils.ReadJson(text3), new PyDict())).Get("objects"), new PyDict()));
		if (pyDict2.Count == 0)
		{
			items.Add(_item("warn", "assets_index_empty", "资源索引为空", text2 + ".json 没有 objects，游戏可能缺材质/音效。"));
			return;
		}
		List<string> list = pyDict2.Keys.ToList();
		list.Sort(string.CompareOrdinal);
		List<string> list2 = list.Take(80).ToList();
		int num = 0;
		foreach (string item in list2)
		{
			string text4 = Py.Strip(Py.Str(Py.Or(Py.DictOr(Py.Or(pyDict2.Get(item), new PyDict())).Get("hash"), "")));
			if (text4.Length >= 2 && !new PPath(PPath.Join(text, "objects", text4.Substring(0, 2), text4)).IsFile())
			{
				num++;
			}
		}
		if (num != 0)
		{
			double num2 = (double)num / (double)Math.Max(1, list2.Count);
			long num3 = Py.RoundInt(num2 * (double)pyDict2.Count);
			string text5 = "抽样 " + list2.Count + " 个资源缺 " + num + " 个（约估全量缺 " + num3 + "/" + pyDict2.Count + "）。";
			if (num2 >= 0.25 || num >= 8)
			{
				items.Add(_item("error", "assets_missing", "游戏资源大量缺失", text5 + "请修复该版本。"));
			}
			else
			{
				items.Add(_item("warn", "assets_partial", "部分游戏资源缺失", text5 + "可先启动，异常再修复。"));
			}
		}
	}

	private static void _check_natives(Instance inst, string version, PyDict resolved, List<object> items)
	{
		bool flag = false;
		foreach (PyDict item in Installer.Libs(resolved))
		{
			if (Utils.CheckRules(item.Get("rules")) && !string.IsNullOrEmpty(Installer.SelectNativeClassifier(item)))
			{
				flag = true;
				break;
			}
		}
		if (flag)
		{
			string text;
			try
			{
				text = Installer.NativesDir(inst, version, resolved);
			}
			catch (Exception)
			{
				return;
			}
			if (!Installer.NativesPresent(text))
			{
				items.Add(_item("warn", "natives_missing", "本地库（natives）可能未解压", text + " 为空或不完整。启动时会尝试再解压；若仍黑屏请修复该版本。"));
			}
		}
	}

	private static HashSet<string> _global_mod_ids()
	{
		HashSet<string> hashSet = new HashSet<string>();
		string text = GlobalMods.Root();
		if (!new PPath(text).IsDir())
		{
			return hashSet;
		}
		List<string> source;
		try
		{
			source = (from p in Children(text)
				where File.Exists(Utils.AbsPath(p)) && Py.Lower(new PPath(p).Name).EndsWith(".jar")
				select p).ToList();
		}
		catch (Exception)
		{
			return hashSet;
		}
		foreach (string item in source.Take(120))
		{
			string text2;
			try
			{
				text2 = Py.Lower(Py.Str(Py.Or(Conflict.InspectJar(item).Get("id"), "")));
			}
			catch (Exception)
			{
				continue;
			}
			if (text2.Length > 0)
			{
				hashSet.Add(text2);
			}
		}
		return hashSet;
	}

	private static List<string> Dedup(IEnumerable<string> xs)
	{
		HashSet<string> hashSet = new HashSet<string>();
		return xs.Where(hashSet.Add).ToList();
	}

	private static void _check_mod_conflicts(string modsPath, List<object> items)
	{
		List<string> list = new List<string>();
		foreach (string item in Children(modsPath))
		{
			string text = Py.Lower(new PPath(item).Name);
			if (File.Exists(Utils.AbsPath(item)) && (text.EndsWith(".jar") || text.EndsWith(".jar.disabled")))
			{
				list.Add(item);
			}
			if (list.Count >= 120)
			{
				break;
			}
		}
		if (list.Count == 0)
		{
			return;
		}
		List<string> list2 = list.Where((string p) => !Py.Lower(new PPath(p).Name).EndsWith(".disabled")).Take(120).ToList();
		List<PyDict> list3 = new List<PyDict>();
		foreach (string item2 in list2)
		{
			try
			{
				list3.Add(Conflict.InspectJar(item2));
			}
			catch (Exception)
			{
			}
		}
		PyDict pyDict = new PyDict();
		foreach (PyDict item3 in list3)
		{
			string text2 = Py.Lower(Py.Str(Py.Or(item3.Get("id"), "")));
			if (text2.Length != 0)
			{
				if (!pyDict.ContainsKey(text2))
				{
					pyDict[text2] = new List<object>();
				}
				((List<object>)pyDict[text2]).Add(item3);
			}
		}
		List<string> list4 = new List<string>();
		foreach (KeyValuePair<string, object> item4 in pyDict)
		{
			List<object> list5 = (List<object>)item4.Value;
			if (list5.Count > 1)
			{
				list4.Add(item4.Key + " ×" + list5.Count + "（" + string.Join(", ", from g in list5.Take(3)
					select Py.Str(Py.Or(((PyDict)g).Get("file"), "?"))) + "）");
			}
		}
		if (list4.Count > 0)
		{
			items.Add(_item("error", "mod_duplicate", "重复安装同一模组", "同 id 装了多份会导致启动失败：\n - " + string.Join("\n - ", list4.Take(8))));
		}
		HashSet<string> hashSet = new HashSet<string>(pyDict.Keys);
		bool flag = hashSet.Contains("fabric-api") || hashSet.Contains("fabricapi");
		if (!flag)
		{
			HashSet<string> hashSet2 = _global_mod_ids();
			flag = hashSet2.Contains("fabric-api") || hashSet2.Contains("fabricapi");
		}
		List<string> list6 = new List<string>();
		List<string> list7 = new List<string>();
		HashSet<string> hashSet3 = new HashSet<string> { "minecraft", "java", "forge", "neoforge", "fabricloader", "fabric-loader", "quilt_loader", "quilt-loader" };
		foreach (PyDict item5 in list3)
		{
			foreach (PyDict item6 in Py.ListOr(Py.Or(item5.Get("breaks"), new List<object>())).Concat(Py.ListOr(Py.Or(item5.Get("conflicts"), new List<object>()))))
			{
				string text3 = Py.Lower(Py.Str(Py.Or(item6.Get("id"), "")));
				if (text3.Length > 0 && hashSet.Contains(text3))
				{
					list6.Add(Py.Str(item5.Get("id")) + " ↔ " + text3);
				}
			}
			foreach (PyDict item7 in Py.ListOr(Py.Or(item5.Get("depends"), new List<object>())))
			{
				string text4 = Py.Lower(Py.Str(Py.Or(item7.Get("id"), "")));
				if (text4.Length == 0 || hashSet3.Contains(text4))
				{
					continue;
				}
				string text5 = Py.Str(Py.Or(item5.Get("name"), item5.Get("file")));
				switch (text4)
				{
				case "fabric-api":
				case "fabricapi":
				case "fabric":
					if (!flag)
					{
						list7.Add(text5 + " 需要 Fabric API");
					}
					break;
				default:
					if (!hashSet.Contains(text4))
					{
						list7.Add(text5 + " 缺少 " + text4);
					}
					break;
				}
			}
		}
		if (list6.Count > 0)
		{
			items.Add(_item("error", "mod_breaks", "模组互相冲突", "元数据声明不兼容：\n - " + string.Join("\n - ", list6.Take(8))));
		}
		List<string> fab = list7.Where((string x) => x.Contains("Fabric API")).ToList();
		List<string> list8 = list7.Where((string x) => !fab.Contains(x)).ToList();
		if (fab.Count > 0)
		{
			items.Add(_item("error", "mod_missing_fabric_api", "缺少 Fabric API", string.Join("\n - ", Dedup(fab).Take(6))));
		}
		if (list8.Count > 0)
		{
			items.Add(_item("warn", "mod_missing_dep", "可能缺少模组依赖", string.Join("\n - ", Dedup(list8).Take(8)) + "\n（若实际由其它 jar 提供可忽略）"));
		}
	}
}
