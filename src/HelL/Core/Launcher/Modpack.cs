using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;

namespace HelL.Core.Launcher;

public static class Modpack
{
	public const string MODRINTH_API = "https://api.modrinth.com/v2";

	public const string CURSEFORGE_DOWNLOAD = "https://www.curseforge.com/api/v1/mods/{project_id}/files/{file_id}/download";

	private static readonly Regex HttpRe = new Regex("^https?://");

	private static readonly string[] _PLAIN_MC_STRONG = new string[4] { "mods", "config", "versions", "saves" };

	private static readonly Dictionary<string, string> HtmlHeaders = new Dictionary<string, string> { { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36" } };

	private static string BadZipText(Exception e)
	{
		if (!(e is InvalidDataException))
		{
			return Py.ExcStr(e);
		}
		return "File is not a zip file";
	}

	private static void _emit(Action<string, long, long> on_progress, object msg0)
	{
		string text = Py.Strip(Py.S(msg0));
		if (text.Length != 0)
		{
			Utils.log.Info("%s", text);
			on_progress?.Invoke(text, 0L, 1L);
		}
	}

	public static bool PackRetryWorthwhile(object err)
	{
		string msg = ((err is Exception e) ? Py.ExcStr(e) : Py.Str(err));
		return !new string[8] { "Forge 没有支持 Minecraft", "NeoForge 没有支持 Minecraft", "无法获取 Forge 版本列表", "Forge 处理器失败", "Forge 处理器运行失败", "缺少原版客户端", "已取消", "用户取消" }.Any((string s) => msg.Contains(s));
	}

	public static List<object> CfManifestLoaders(PyDict mf)
	{
		return (Py.Or(((mf["minecraft"] as PyDict) ?? new PyDict())["modLoaders"], mf["modLoaders"], new List<object>()) as List<object>) ?? new List<object>();
	}

	private static string MkdTemp(string prefix)
	{
		string text = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N").Substring(0, 8));
		Directory.CreateDirectory(text);
		return text;
	}

	private static int CmpParts(string a, string b)
	{
		string[] array = a.Split('/', '\\');
		string[] array2 = b.Split('/', '\\');
		for (int i = 0; i < Math.Min(array.Length, array2.Length); i++)
		{
			int num = string.CompareOrdinal(array[i], array2[i]);
			if (num != 0)
			{
				return num;
			}
		}
		return array.Length.CompareTo(array2.Length);
	}

	private static List<string> GlobLevel(string root, int level)
	{
		List<string> list = new List<string> { root };
		for (int i = 0; i < level; i++)
		{
			list = list.SelectMany((string d) => LocalFs.Children(d).Where(LocalFs.IsDir)).ToList();
		}
		return list;
	}

	private static List<string> SortedPaths(IEnumerable<string> xs)
	{
		List<string> list = xs.ToList();
		list.Sort(CmpParts);
		return list;
	}

	public static List<string> _copy_embedded_versions(string tmpdir, Instance instance, bool plain = false)
	{
		List<string> list = new List<string>();
		string text = instance.VersionsDir();
		string[] array = (plain ? new string[3] { "overrides", "client-overrides", "" } : new string[2] { "overrides", "client-overrides" });
		foreach (string text2 in array)
		{
			string text3 = ((text2.Length > 0) ? PPath.Join(tmpdir, text2, "versions") : PPath.Join(tmpdir, "versions"));
			if (!LocalFs.IsDir(text3))
			{
				continue;
			}
			foreach (string item in LocalFs.Children(text3))
			{
				if (LocalFs.IsDir(item))
				{
					string name = new PPath(item).Name;
					if (LocalFs.IsFile(PPath.Join(item, name + ".json")))
					{
						LocalFs.CopyTree(item, PPath.Join(text, name), dirsExistOk: true);
						list.Add(name);
					}
				}
			}
		}
		return list;
	}

	public static string _nested_marker_root(string tmpdir, string marker, int depth = 3)
	{
		for (int i = 1; i <= depth; i++)
		{
			List<string> list = SortedPaths((from d in GlobLevel(tmpdir, i)
				select PPath.Join(d, marker)).Where(LocalFs.IsFile));
			if (list.Count > 0)
			{
				return new PPath(list[0]).Parent.ToString();
			}
		}
		return null;
	}

	private static bool _looks_like_mc_dir(string root)
	{
		HashSet<string> hashSet;
		try
		{
			hashSet = new HashSet<string>(from p in Directory.EnumerateFileSystemEntries(PPath.Abs(root))
				select Py.Lower(Path.GetFileName(p)));
		}
		catch (Exception)
		{
			return false;
		}
		if (hashSet.Contains("manifest.json") || hashSet.Contains("modrinth.index.json"))
		{
			return false;
		}
		return _PLAIN_MC_STRONG.Any(hashSet.Contains);
	}

	private static string _plain_pack_root(string tmpdir)
	{
		if (_looks_like_mc_dir(tmpdir))
		{
			return tmpdir;
		}
		int[] array = new int[2] { 1, 2 };
		foreach (int level in array)
		{
			foreach (string item in SortedPaths(GlobLevel(tmpdir, level)))
			{
				if (LocalFs.IsDir(item) && _looks_like_mc_dir(item))
				{
					return item;
				}
			}
		}
		return null;
	}

	private static PyDict _plain_pack_version(string root)
	{
		string text = PPath.Join(root, "versions");
		if (!LocalFs.IsDir(text))
		{
			return null;
		}
		PyDict pyDict = null;
		foreach (string item in LocalFs.SortedChildren(text))
		{
			if (!LocalFs.IsDir(item))
			{
				continue;
			}
			string name = new PPath(item).Name;
			string text2 = PPath.Join(item, name + ".json");
			if (LocalFs.IsFile(text2))
			{
				PyDict pyDict2 = Py.DictOr(Py.Or(Utils.ReadJson(text2), new PyDict()));
				string text3 = Py.Str(Py.Or(pyDict2["id"], name));
				string text4 = string.Join(" ", from l in Py.ListOr(Py.Or(pyDict2["libraries"], new List<object>()))
					select Py.Str(Py.Or(Py.DictOr(Py.Or(l, new PyDict()))["name"], "")));
				string text5 = Py.Str(Py.Or(pyDict2["inheritsFrom"], ""));
				object obj = ((text5.Length > 0) ? text5 : null);
				if (text3.Contains("-forge") || text4.Contains("minecraftforge"))
				{
					Match match = Regex.Match(text3, "-forge-?(.+)$");
					return PyDict.Of("mc", obj, "loader", "forge", "loader_version", match.Success ? match.Groups[1].Value : "");
				}
				if (text3.Contains("neoforge") || text4.Contains("neoforge"))
				{
					Match match2 = Regex.Match(text3, "-neoforge-?(.+)$");
					return PyDict.Of("mc", obj, "loader", "neoforge", "loader_version", match2.Success ? match2.Groups[1].Value : "");
				}
				if (text3.StartsWith("fabric-loader-", StringComparison.Ordinal))
				{
					Match match3 = Regex.Match(text3, "^fabric-loader-([^-]+)-(.+)$");
					return PyDict.Of("mc", match3.Success ? match3.Groups[2].Value : obj, "loader", "fabric-loader", "loader_version", match3.Success ? match3.Groups[1].Value : "");
				}
				if (text3.StartsWith("quilt-loader-", StringComparison.Ordinal))
				{
					Match match4 = Regex.Match(text3, "^quilt-loader-([^-]+)-(.+)$");
					return PyDict.Of("mc", match4.Success ? match4.Groups[2].Value : obj, "loader", "quilt-loader", "loader_version", match4.Success ? match4.Groups[1].Value : "");
				}
				if (pyDict == null && Manifest.LooksLikeMinecraftVersion(text3))
				{
					pyDict = PyDict.Of("mc", text3, "loader", null, "loader_version", "");
				}
			}
		}
		return pyDict;
	}

	private static List<object> _prepare_target_version(Instance instance, object version_id, object version_name, bool isolate, Action<string, long, long> on_progress)
	{
		string text = Py.Str(Py.Or(version_id, ""));
		string text2 = Py.Strip(Py.S(version_name));
		if (text.Length > 0 && text2.Length > 0 && text2 != text)
		{
			string new_id = VersionOps.UniqueId(instance, text2);
			text = VersionOps.RenameVersion(instance, text, new_id);
			_emit(on_progress, "版本命名为 " + text);
		}
		if (text.Length == 0 || !isolate)
		{
			return new List<object> { text, instance.path };
		}
		VersionSettings.SetIsolation(instance, text, "all");
		string text3 = VersionSettings.GameDir(instance, text);
		_emit(on_progress, "版本 " + text + " 设为完全独立，整合包内容写进 " + text3);
		return new List<object> { text, text3 };
	}

	private static string _resolve_pack_minecraft(DownloadManager dm, object declared, Action<string, long, long> on_progress)
	{
		if (!Py.Truthy(declared))
		{
			return null;
		}
		string text = Py.Str(declared);
		if (Manifest.LooksLikeMinecraftVersion(declared))
		{
			string text2 = Manifest.ResolvePlayableVersion(dm, declared);
			if (Py.Truthy(text2) && text2 != text)
			{
				on_progress?.Invoke("Minecraft " + text + " 不在官方列表，改用 " + text2, 0L, 1L);
			}
			if (!Py.Truthy(text2))
			{
				return text;
			}
			return text2;
		}
		string text3 = Manifest.ResolvePlayableVersion(dm, declared);
		if (Py.Truthy(text3))
		{
			on_progress?.Invoke("声明版本 " + text + " 不是 Minecraft 版本，改用 " + text3, 0L, 1L);
			return text3;
		}
		return null;
	}

	public static bool _has_mrpack_index(string path)
	{
		if (LocalFs.IsDir(path))
		{
			if (!LocalFs.IsFile(PPath.Join(path, "modrinth.index.json")))
			{
				return _nested_marker_root(path, "modrinth.index.json") != null;
			}
			return true;
		}
		try
		{
			using ZipArchive zipArchive = Utils.OpenZip(path);
			return zipArchive.Entries.Select((ZipArchiveEntry e) => Utils.ZipEntryName(e)).Any((string n) => n.Split('/').Last() == "modrinth.index.json" && n.Split('/').Length <= 4);
		}
		catch (Exception)
		{
			return false;
		}
	}

	private static string _fetch_mrpack(DownloadManager dm, object source)
	{
		string text = Py.Str(source);
		if (HttpRe.IsMatch(text))
		{
			string text2 = Path.Combine(Path.GetTempPath(), "tipcl_mrpack_" + Math.Abs((long)text.GetHashCode()) + ".mrpack");
			dm.Download(text, text2, null, null, force: false, 900L);
			return text2;
		}
		if (!LocalFs.IsFile(text))
		{
			throw new ModpackError("找不到整合包文件: " + text);
		}
		return PPath.Norm(text);
	}

	private static void _copy_tree_over(string src, string dest)
	{
		Walk(src);
		void Walk(string d)
		{
			foreach (string item in LocalFs.Children(d))
			{
				string text = new PPath(item).RelativeTo(src).ToString();
				string text2 = PPath.Join(dest, text);
				if (LocalFs.IsDir(item))
				{
					Directory.CreateDirectory(PPath.Abs(text2));
				}
				else if (LocalFs.IsFile(item))
				{
					Directory.CreateDirectory(PPath.Abs(new PPath(text2).Parent.ToString()));
					LocalFs.Copy2(item, text2);
				}
			}
			foreach (string item2 in LocalFs.Children(d))
			{
				if (LocalFs.IsDir(item2) && !SingleRoot.IsSymlink(item2))
				{
					Walk(item2);
				}
			}
		}
	}

	private static void _copy_mc_tree(string root, string dest)
	{
		HashSet<string> hashSet = new HashSet<string> { "versions", "logs", "crash-reports" };
		foreach (string item in LocalFs.Children(root))
		{
			string name = new PPath(item).Name;
			if (hashSet.Contains(name))
			{
				continue;
			}
			string dst = PPath.Join(dest, name);
			try
			{
				if (LocalFs.IsDir(item))
				{
					LocalFs.CopyTree(item, dst, dirsExistOk: true);
				}
				else if (LocalFs.IsFile(item))
				{
					LocalFs.Copy2(item, dst);
				}
			}
			catch (IOException)
			{
			}
			catch (UnauthorizedAccessException)
			{
			}
		}
	}

	private static void EnsureInstance(Instance instance)
	{
		if (LocalFs.IsDir(instance.path))
		{
			instance.EnsureStandardDirs();
		}
		else
		{
			instance.Create();
		}
	}

	private static void CleanupTemp(bool unpacked, string tmpdir, bool downloaded, string packPath)
	{
		if (!unpacked && tmpdir != null)
		{
			try
			{
				Directory.Delete(tmpdir, recursive: true);
			}
			catch (Exception)
			{
			}
		}
		if (downloaded && packPath != null)
		{
			try
			{
				File.Delete(packPath);
			}
			catch (Exception)
			{
			}
		}
	}

	public static PyDict InstallMrpack(DownloadManager dm, object source, Instance instance, Action<string, long, long> on_progress = null, Func<bool> cancel = null, bool force = false, string java = null, bool isolate = false, object version_name = null)
	{
		string text = Py.Str(source);
		bool flag = HttpRe.IsMatch(text);
		bool flag2 = !flag && LocalFs.IsDir(text);
		_emit(on_progress, (flag ? "下载" : "读取") + "整合包: " + text);
		string text2 = null;
		string text3;
		if (flag2)
		{
			text3 = (text2 = PPath.Norm(text));
		}
		else
		{
			text3 = _fetch_mrpack(dm, source);
			_emit(on_progress, "解压整合包 " + new PPath(text3).Name);
			text2 = MkdTemp("tipcl_mrpack_");
		}
		try
		{
			if (!flag2)
			{
				try
				{
					Utils.SafeExtractZip(text3, text2);
				}
				catch (Exception ex) when (ex is InvalidDataException || ex is ValueError)
				{
					throw new ModpackError("不是有效的 mrpack 文件: " + BadZipText(ex));
				}
			}
			string text4 = text2;
			string text5 = PPath.Join(text2, "modrinth.index.json");
			if (!LocalFs.IsFile(text5))
			{
				string text6 = _nested_marker_root(text2, "modrinth.index.json");
				if (text6 != null)
				{
					text4 = text6;
					text5 = PPath.Join(text6, "modrinth.index.json");
					_emit(on_progress, "modrinth.index.json 位于子目录 " + new PPath(text6).Name + "/，按该层作为包根安装");
				}
			}
			if (!LocalFs.IsFile(text5))
			{
				if (LocalFs.IsFile(PPath.Join(text4, "manifest.json")))
				{
					throw new ModpackError("这是 CurseForge 整合包（含 manifest.json），请按 zip 安装，不是 mrpack");
				}
				throw new ModpackError("整合包缺少 modrinth.index.json。常见原因：下载到的是模组 jar，而不是 .mrpack 整合包。");
			}
			PyDict pyDict = Py.DictOr(Py.Or(Utils.ReadJson(text5), new PyDict()));
			if (Py.Cmp(pyDict.ContainsKey("formatVersion") ? pyDict["formatVersion"] : ((object)1L), 1L) > 0)
			{
				_emit(on_progress, "mrpack 格式版本 " + Py.Str(pyDict["formatVersion"]) + " 较新，可能不完全兼容");
			}
			string stem = new PPath(text3).Stem;
			object o = Py.Or(pyDict["name"], stem);
			object o2 = Py.Or(pyDict["versionId"], "?");
			PyDict pyDict2 = Py.DictOr(Py.Or(pyDict["dependencies"], new PyDict()));
			List<object> list = Py.ListOr(Py.Or(pyDict["files"], new List<object>()));
			_emit(on_progress, "解析整合包 " + Py.Str(o) + " 版本 " + Py.Str(o2));
			_emit(on_progress, "声明依赖: " + Py.Str(pyDict2));
			_emit(on_progress, "索引文件 " + list.Count + " 个");
			EnsureInstance(instance);
			_emit(on_progress, "安装到实例 " + instance.name + " (" + instance.path + ")");
			Installer installer = new Installer(instance, dm, on_progress ?? dm.on_progress, cancel);
			List<string> list2 = _copy_embedded_versions(text4, instance);
			if (list2.Count > 0)
			{
				_emit(on_progress, "发现整合包自带版本: " + string.Join(", ", list2));
			}
			string text8;
			if (Py.Or(pyDict2["minecraft"], pyDict["versionId"]) is string text7 && list2.Contains(text7))
			{
				text8 = text7;
				_emit(on_progress, "使用整合包自带 Minecraft " + text8);
			}
			else
			{
				text8 = _resolve_pack_minecraft(dm, pyDict2["minecraft"], on_progress);
				if (!Py.Truthy(text8))
				{
					text8 = _resolve_pack_minecraft(dm, pyDict["versionId"], on_progress);
				}
			}
			if (!Py.Truthy(text8))
			{
				throw new ModpackError("整合包没有声明可用的 Minecraft 版本");
			}
			_emit(on_progress, "安装 Minecraft " + text8);
			installer.InstallVersion(text8, force, java);
			string text9 = null;
			string text10 = null;
			string[] array = new string[4] { "fabric-loader", "quilt-loader", "forge", "neoforge" };
			foreach (string text11 in array)
			{
				if (Py.Truthy(pyDict2[text11]))
				{
					text9 = text11;
					break;
				}
			}
			if (text9 != null)
			{
				object obj = pyDict2[text9];
				_emit(on_progress, "安装加载器 " + text9 + " " + Py.Str(obj) + " (Minecraft " + text8 + ")");
				text10 = InstallLoader(installer, text9, obj, text8, force);
				_emit(on_progress, "加载器安装完成: " + text10);
			}
			else
			{
				_emit(on_progress, "整合包未声明 Forge/Fabric/Quilt/NeoForge，仅安装原版");
			}
			List<object> list3 = _prepare_target_version(instance, Py.Or(text10, text8), version_name, isolate, on_progress);
			string text12 = (string)list3[0];
			string text13 = (string)list3[1];
			List<DownloadTask> list4 = new List<DownloadTask>();
			List<KeyValuePair<string, string>> list5 = new List<KeyValuePair<string, string>>();
			string text14 = new PPath(text13).Resolve().ToString();
			foreach (PyDict item in Py.ListOr(pyDict.ContainsKey("files") ? pyDict["files"] : new List<object>()))
			{
				object a = Py.DictOr(Py.Or(item["env"], new PyDict()))["client"];
				if (Py.Eq(a, "unsupported") || Py.Eq(a, "server"))
				{
					continue;
				}
				object o3 = item["path"];
				List<object> list6 = Py.ListOr(Py.Or(item["downloads"], new List<object>())).Where(Py.Truthy).ToList();
				if (Py.Truthy(o3) && list6.Count != 0)
				{
					string text15 = new PPath(PPath.Join(text13, Py.Str(o3))).Resolve().ToString();
					if (!text15.StartsWith(text14 + Path.DirectorySeparatorChar, StringComparison.Ordinal))
					{
						throw new ModpackError("整合包文件路径非法: " + Py.Str(o3));
					}
					PyDict pyDict4 = Py.DictOr(Py.Or(item["hashes"], new PyDict()));
					object obj2 = item["size"];
					list4.Add(new DownloadTask(Mods.ModrinthDownloadUrls(list6).Cast<object>().ToList(), text15, pyDict4["sha1"] as string, (obj2 == null) ? ((long?)null) : new long?(Py.Int(obj2)), pyDict4["sha512"] as string));
					if (Py.Truthy(pyDict4["sha512"]))
					{
						list5.Add(new KeyValuePair<string, string>(text15, Py.Str(pyDict4["sha512"])));
					}
				}
			}
			if (list4.Count > 0)
			{
				_emit(on_progress, "开始下载整合包文件（" + list4.Count + " 个，镜像失败会改走官方源）");
				try
				{
					dm.DownloadAll(list4, "下载整合包文件");
				}
				catch (DownloadError e)
				{
					throw new ModpackError(Py.ExcStr(e));
				}
				foreach (KeyValuePair<string, string> item2 in list5)
				{
					if (!LocalFs.IsFile(item2.Key) || Utils.Sha512File(item2.Key) != Py.Lower(item2.Value))
					{
						throw new ModpackError("文件校验失败 (sha512): " + item2.Key);
					}
				}
				_emit(on_progress, "整合包文件下载完成");
			}
			else
			{
				_emit(on_progress, "索引中没有客户端文件需要下载");
			}
			array = new string[2] { "overrides", "client-overrides" };
			foreach (string text16 in array)
			{
				string text17 = PPath.Join(text4, text16);
				if (LocalFs.IsDir(text17))
				{
					_emit(on_progress, "复制 " + text16);
					_copy_tree_over(text17, text13);
					break;
				}
			}
			PyDict pyDict5 = PyDict.Of("name", pyDict.ContainsKey("name") ? pyDict["name"] : stem, "version", pyDict["versionId"], "mc_version", text8, "version_id", text12, "loader", (text9 != null) ? (text9 + "-" + Py.Str(pyDict2[text9])) : null, "source", "modrinth", "instance", instance.name);
			instance.SetMeta("modpack", pyDict5);
			instance.SetMeta("mc_version", text12);
			_emit(on_progress, "整合包 " + Py.Str(pyDict5["name"]) + " 安装完成 -> 实例 " + instance.name);
			return pyDict5;
		}
		finally
		{
			CleanupTemp(flag2, text2, flag, text3);
		}
	}

	public static PyDict InstallCfZip(DownloadManager dm, object source, Instance instance, Action<string, long, long> on_progress = null, Func<bool> cancel = null, bool force = false, string java = null, bool isolate = false, object version_name = null)
	{
		bool downloaded = false;
		bool flag = false;
		string text = Py.Str(source);
		string text2;
		if (HttpRe.IsMatch(text))
		{
			text2 = Path.Combine(Path.GetTempPath(), "tipcl_cfpack_" + Math.Abs((long)text.GetHashCode()) + ".zip");
			dm.Download(text, text2, null, null, force: false, 900L);
			downloaded = true;
		}
		else
		{
			text2 = PPath.Norm(text);
			if (LocalFs.IsDir(text2))
			{
				flag = true;
			}
			else if (!LocalFs.IsFile(text2))
			{
				throw new ModpackError("找不到整合包文件: " + text);
			}
		}
		string text3 = (flag ? text2 : MkdTemp("tipcl_cfpack_"));
		try
		{
			if (!flag)
			{
				try
				{
					Utils.SafeExtractZip(text2, text3);
				}
				catch (Exception ex) when (ex is InvalidDataException || ex is ValueError)
				{
					throw new ModpackError("不是有效的整合包 zip: " + BadZipText(ex));
				}
			}
			string text4 = PPath.Join(text3, "manifest.json");
			string text5 = text3;
			if (!LocalFs.IsFile(text4))
			{
				string text6 = _nested_marker_root(text3, "manifest.json");
				if (text6 != null)
				{
					text5 = text6;
					text4 = PPath.Join(text6, "manifest.json");
					_emit(on_progress, "manifest.json 位于子目录 " + new PPath(text6).Name + "/，按该层作为包根安装");
				}
			}
			if (!LocalFs.IsFile(text4))
			{
				return _install_plain_zip(dm, text3, instance, text2, on_progress, cancel, force, java, isolate, version_name);
			}
			PyDict pyDict = Py.DictOr(Py.Or(Utils.ReadJson(text4), new PyDict()));
			if (!Py.Eq(pyDict["manifestType"], "minecraftModpack"))
			{
				throw new ModpackError("manifest.json 不是 minecraftModpack 类型");
			}
			string stem = new PPath(text2).Stem;
			_emit(on_progress, "解析 CurseForge 整合包 " + Py.Str(Py.Or(pyDict["name"], stem)) + " 版本 " + Py.Str(pyDict.ContainsKey("version") ? pyDict["version"] : "?"));
			EnsureInstance(instance);
			_emit(on_progress, "安装到实例 " + instance.name + " (" + instance.path + ")");
			Installer installer = new Installer(instance, dm, on_progress ?? dm.on_progress, cancel);
			List<string> list = _copy_embedded_versions(text5, instance);
			if (list.Count > 0)
			{
				_emit(on_progress, "发现整合包自带版本: " + string.Join(", ", list));
			}
			object obj = Py.DictOr(Py.Or(pyDict["minecraft"], new PyDict()))["version"];
			string text7 = ((obj is string text8 && list.Contains(text8)) ? text8 : _resolve_pack_minecraft(dm, obj, on_progress));
			if (!Py.Truthy(text7))
			{
				throw new ModpackError("整合包没有声明可用的 Minecraft 版本");
			}
			_emit(on_progress, "安装 Minecraft " + text7);
			installer.InstallVersion(text7, force, java);
			List<object> list2 = CfManifestLoaders(pyDict);
			PyDict pyDict2 = list2.OfType<PyDict>().FirstOrDefault((PyDict l) => Py.Truthy(l["primary"])) ?? ((list2.Count > 0) ? (list2[0] as PyDict) : null);
			if (pyDict2 == null || !Py.Truthy(pyDict2["id"]))
			{
				throw new ModpackError("整合包没有声明 Forge/Fabric 加载器（manifest.json 的 minecraft.modLoaders）。只装原版的话，游戏不会加载 mods 文件夹。");
			}
			string text9 = Py.Str(pyDict2.ContainsKey("id") ? pyDict2["id"] : "");
			Match match = Regex.Match(text9, "^([a-zA-Z]+)-(.+)$", RegexOptions.Singleline);
			if (!match.Success)
			{
				throw new ModpackError("无法解析加载器 id: " + text9);
			}
			string value = match.Groups[1].Value;
			string value2 = match.Groups[2].Value;
			value = ((value == "fabric") ? "fabric-loader" : ((value == "quilt") ? "quilt-loader" : value));
			_emit(on_progress, "安装加载器 " + value + " " + value2 + " (Minecraft " + text7 + ")");
			string text10 = InstallLoader(installer, value, value2, text7, force);
			_emit(on_progress, "加载器安装完成: " + text10);
			List<object> list3 = _prepare_target_version(instance, Py.Or(text10, text7), version_name, isolate, on_progress);
			string text11 = (string)list3[0];
			string text12 = (string)list3[1];
			List<PyDict> list4 = (from f in Py.ListOr(Py.Or(pyDict["files"], new List<object>())).OfType<PyDict>()
				where Py.Truthy(f["projectID"]) && Py.Truthy(f["fileID"])
				select f).ToList();
			Dictionary<long, PyDict> dictionary = new Dictionary<long, PyDict>();
			if (list4.Count > 0)
			{
				try
				{
					dictionary = Mods.CfFilesByIds(dm, list4.Select((PyDict f) => f["fileID"]).ToList());
				}
				catch (Exception e)
				{
					Utils.log.Warning("批量查询整合包 Mod 元数据失败，将仅用 CDN 规则: %s", Py.ExcStr(e));
				}
			}
			List<DownloadTask> list5 = new List<DownloadTask>();
			foreach (PyDict item in list4)
			{
				object obj2 = item["projectID"];
				object obj3 = item["fileID"];
				PyDict value3;
				PyDict pyDict3 = ((obj3 != null && dictionary.TryGetValue(Py.Int(obj3), out value3)) ? value3 : new PyDict());
				object obj4 = pyDict3["fileName"];
				object download_url = pyDict3["downloadUrl"];
				string text13 = (Py.Truthy(obj4) ? Py.Str(obj4) : ("mod-" + Py.Str(obj2) + "-" + Py.Str(obj3) + ".jar"));
				string dest = PPath.Join(text12, "mods", text13);
				string sha = null;
				foreach (PyDict item2 in Py.ListOr(Py.Or(pyDict3["hashes"], new List<object>())).OfType<PyDict>())
				{
					if (Py.Eq(item2["algo"], 1L) && Py.Truthy(item2["value"]))
					{
						sha = Py.Str(item2["value"]);
						break;
					}
				}
				object obj5 = pyDict3["fileLength"];
				List<object> url = Mods.CfModDownloadUrls(obj2, obj3, obj4, download_url);
				list5.Add(new DownloadTask(url, dest, sha, (obj5 == null) ? ((long?)null) : new long?(Py.Int(obj5))));
			}
			if (list5.Count > 0)
			{
				_emit(on_progress, "开始下载整合包 Mod（" + list5.Count + " 个）");
				dm.DownloadAll(list5, "下载整合包 Mod");
				_emit(on_progress, "整合包 Mod 下载完成");
			}
			object o = pyDict["overrides"];
			if (Py.Truthy(o))
			{
				string text14 = PPath.Join(text5, Py.Str(o));
				if (LocalFs.IsDir(text14))
				{
					_copy_tree_over(text14, text12);
				}
			}
			PyDict pyDict4 = PyDict.Of("name", pyDict.ContainsKey("name") ? pyDict["name"] : stem, "version", pyDict.ContainsKey("version") ? pyDict["version"] : "?", "mc_version", text7, "version_id", text11, "loader", text9, "source", "curseforge", "instance", instance.name);
			instance.SetMeta("modpack", pyDict4);
			instance.SetMeta("mc_version", text11);
			_emit(on_progress, "整合包 " + Py.Str(pyDict4["name"]) + " 安装完成 -> 实例 " + instance.name);
			return pyDict4;
		}
		finally
		{
			CleanupTemp(flag, text3, downloaded, text2);
		}
	}

	private static PyDict _install_plain_zip(DownloadManager dm, string tmpdir, Instance instance, string packPath, Action<string, long, long> on_progress, Func<bool> cancel, bool force, string java, bool isolate, object version_name)
	{
		string text = _plain_pack_root(tmpdir);
		if (text == null)
		{
			List<string> list = (from p in LocalFs.Children(tmpdir)
				select new PPath(p).Name).ToList();
			list.Sort(string.CompareOrdinal);
			string text2 = string.Join(", ", list.Take(10));
			throw new ModpackError("整合包缺少 manifest.json，也不是 .minecraft 目录结构。\nzip 顶层内容: " + ((text2.Length > 0) ? text2 : "(空)") + "\n支持三种格式：CurseForge 导出的 zip（内含 manifest.json）、Modrinth 的 .mrpack、直接压缩的 .minecraft 目录（含 mods / versions 等）。");
		}
		string text3 = new PPath(text).RelativeTo(tmpdir).ToString();
		string text4 = ((text3 != ".") ? ("（位于 " + text3 + " 子目录）") : "");
		_emit(on_progress, "识别为 .minecraft 目录压缩包" + text4 + "，开始安装");
		EnsureInstance(instance);
		_emit(on_progress, "安装到实例 " + instance.name + " (" + instance.path + ")");
		Installer installer = new Installer(instance, dm, on_progress ?? dm.on_progress, cancel);
		PyDict obj = _plain_pack_version(text) ?? new PyDict();
		string text5 = Py.Str(Py.Or(obj["mc"], ""));
		string text6 = obj["loader"] as string;
		string text7 = Py.Str(Py.Or(obj["loader_version"], ""));
		List<string> list2 = _copy_embedded_versions(text, instance, plain: true);
		if (list2.Count > 0)
		{
			_emit(on_progress, "发现自带版本: " + string.Join(", ", list2));
		}
		string a = null;
		if (text5.Length == 0)
		{
			_emit(on_progress, "包里没有可识别的版本信息，未安装游戏版本，稍后请在版本页选择");
		}
		else
		{
			string text8 = _resolve_pack_minecraft(dm, text5, on_progress) ?? text5;
			if (text8.Length == 0)
			{
				text8 = text5;
			}
			_emit(on_progress, "安装 Minecraft " + text8);
			installer.InstallVersion(text8, force, java);
			if (Py.Truthy(text6))
			{
				_emit(on_progress, "安装加载器 " + text6 + " " + text7 + " (Minecraft " + text8 + ")");
				try
				{
					a = InstallLoader(installer, text6, text7, text8, force);
				}
				catch (InstallError e)
				{
					a = null;
					_emit(on_progress, "加载器安装失败（" + Py.ExcStr(e) + "），已仅装原版；mods 可能无法加载");
				}
			}
			else
			{
				_emit(on_progress, "未识别到 Forge/Fabric 加载器，按原版安装（mods 不会被加载）");
			}
		}
		List<object> list3 = _prepare_target_version(instance, Py.Or(a, text5), version_name, isolate, on_progress);
		string text9 = (string)list3[0];
		string dest = (string)list3[1];
		_copy_mc_tree(text, dest);
		PyDict pyDict = PyDict.Of("name", new PPath(packPath).Stem, "version", "?", "mc_version", (text5.Length > 0) ? text5 : null, "version_id", text9, "loader", Py.Truthy(text6) ? (text6 + "-" + text7) : "vanilla", "source", "plain-zip", "instance", instance.name);
		instance.SetMeta("modpack", pyDict);
		instance.SetMeta("mc_version", Py.Or(text9, ""));
		_emit(on_progress, "整合包 " + Py.Str(pyDict["name"]) + " 安装完成 -> 实例 " + instance.name);
		return pyDict;
	}

	public static string InstallLoader(Installer installer, object loader0, object version0, string mc_version, bool force = false)
	{
		string text = Py.Lower(Py.S(loader0));
		string text2 = ((version0 == null) ? null : Py.Str(version0));
		switch (text)
		{
		case "fabric-loader":
			try
			{
				return installer.InstallFabric(mc_version, text2, force);
			}
			catch (InstallError e5)
			{
				installer._note("Fabric Loader " + Py.Str(version0) + " 不可用: " + Py.ExcStr(e5) + "，尝试最新稳定版", 0L, 1L);
				try
				{
					return installer.InstallFabric(mc_version, null, force);
				}
				catch (InstallError e6)
				{
					throw new InstallError(Py.ExcStr(e5) + "；回退最新版也失败: " + Py.ExcStr(e6));
				}
			}
		case "quilt-loader":
			try
			{
				return installer.InstallQuilt(mc_version, text2, force);
			}
			catch (InstallError e3)
			{
				installer._note("Quilt Loader " + Py.Str(version0) + " 不可用: " + Py.ExcStr(e3) + "，尝试最新版", 0L, 1L);
				try
				{
					return installer.InstallQuilt(mc_version, null, force);
				}
				catch (InstallError e4)
				{
					throw new InstallError(Py.ExcStr(e3) + "；回退最新版也失败: " + Py.ExcStr(e4));
				}
			}
		case "forge":
			return installer.InstallForge(mc_version, text2, force);
		case "neoforge":
			try
			{
				return installer.InstallNeoforge(mc_version, text2, force);
			}
			catch (InstallError e)
			{
				if (!Py.Truthy(version0))
				{
					throw;
				}
				installer._note("NeoForge " + Py.Str(version0) + " 安装失败: " + Py.ExcStr(e) + "，尝试该 MC 最新 NeoForge", 0L, 1L);
				try
				{
					return installer.InstallNeoforge(mc_version, null, force);
				}
				catch (InstallError e2)
				{
					throw new InstallError(Py.ExcStr(e) + "；回退最新版也失败: " + Py.ExcStr(e2));
				}
			}
		default:
			throw new ModpackError("不支持的加载器: " + text);
		}
	}

	private static object Gt(object o, string k)
	{
		return Mods.Gt(o, k);
	}

	private static List<object> Iter(object o)
	{
		return Mods.Iter(o);
	}

	private static string ErrS(object e)
	{
		if (!(e is Exception e2))
		{
			return Py.Str(e);
		}
		return Py.ExcStr(e2);
	}

	public static List<object> SearchCfModpacks(DownloadManager dm, object query, long limit = 25L, string api_key = null, object game_version = null, List<object> categories = null)
	{
		List<object> list = new List<object>();
		foreach (object item in categories ?? new List<object>())
		{
			List<object> list2 = CatalogFiles.CfCategoryTokens(item);
			IEnumerable<object> collection;
			if (list2.Count <= 0)
			{
				IEnumerable<object> enumerable = Py.L(Py.Lower(Py.Str(item)));
				collection = enumerable;
			}
			else
			{
				IEnumerable<object> enumerable = list2;
				collection = enumerable;
			}
			list.AddRange(collection);
		}
		List<object> list3 = Mods.SearchCurseforge(dm, query, limit, api_key, 4471L, null, game_version, (list.Count > 0) ? list : null);
		foreach (PyDict item2 in list3)
		{
			item2["description"] = item2.Pop("summary", "");
			item2.Pop("cf_categories");
		}
		return list3;
	}

	private static bool _cf_pack_is_server(object file_obj)
	{
		if (!(file_obj is PyDict pyDict))
		{
			return false;
		}
		object obj = pyDict["isServerPack"];
		bool flag = false;
		int num;
		if (obj is bool)
		{
			flag = (bool)obj;
			num = 1;
		}
		else
		{
			num = 0;
		}
		if (((uint)num & (flag ? 1u : 0u)) != 0)
		{
			return true;
		}
		string text = Py.Lower(Py.S(pyDict["fileName"]));
		if (text.Contains("client"))
		{
			return false;
		}
		return text.Contains("server");
	}

	private static object _pick_cf_pack_file(List<object> files, object main_file_id = null)
	{
		if (files == null || files.Count == 0)
		{
			return null;
		}
		List<object> list = files.Where((object f) => Py.Lower(Py.S(Gt(f, "fileName"))).EndsWith(".zip", StringComparison.Ordinal)).ToList();
		List<object> list2 = ((list.Count > 0) ? list : new List<object>(files)).Where((object f) => !_cf_pack_is_server(f)).ToList();
		if (list2.Count == 0)
		{
			list2 = ((list.Count > 0) ? list : new List<object>(files));
		}
		if (main_file_id != null)
		{
			long? num = null;
			try
			{
				num = Py.Int(main_file_id);
			}
			catch (PyException)
			{
				num = null;
			}
			if (num.HasValue)
			{
				foreach (object item in list2)
				{
					try
					{
						if (Py.Int(Gt(item, "id")) == num)
						{
							return item;
						}
					}
					catch (PyException ex2) when (ex2 is TypeError || ex2 is ValueError)
					{
					}
				}
			}
		}
		List<object> list3 = list2.Where((object f) => Py.Eq(Gt(f, "releaseType"), 1L)).ToList();
		return ((list3.Count > 0) ? list3 : list2)[0];
	}

	public static PyDict ResolveCfModpackFile(DownloadManager dm, object addon_id, string api_key = null, object cf_slug = null)
	{
		object obj = null;
		object obj2 = null;
		object obj3 = "curseforge:" + Py.Str(addon_id);
		if (Py.Truthy(cf_slug))
		{
			try
			{
				object o = Mods.CfBySlug(dm, cf_slug, 4471L, api_key);
				if (Py.Truthy(o))
				{
					addon_id = Py.Or(Gt(o, "id"), addon_id);
					obj3 = Py.Or(Gt(o, "name"), obj3);
					obj2 = Gt(o, "mainFileId");
				}
			}
			catch (Exception ex)
			{
				obj = ex;
				Utils.log.Warning("slug 解析失败(%s): %s", Py.Str(cf_slug), Py.ExcStr(ex));
			}
		}
		try
		{
			PyDict pyDict = Mods.CfDetail(dm, addon_id, api_key);
			obj3 = Py.Or(pyDict["name"], obj3);
			if (!Py.Truthy(obj2))
			{
				obj2 = pyDict["mainFileId"];
			}
			if (!Py.Truthy(cf_slug))
			{
				cf_slug = pyDict["slug"];
			}
		}
		catch (Exception ex2)
		{
			obj = ex2;
			Utils.log.Warning("cf_detail 失败: %s", Py.ExcStr(ex2));
		}
		try
		{
			object o2 = _pick_cf_pack_file(Mods.CfFiles(dm, addon_id, api_key, null, null, 100L), obj2);
			if (Py.Truthy(o2) && Py.Truthy(Gt(o2, "id")))
			{
				return PyDict.Of("addon_id", addon_id, "file_id", Gt(o2, "id"), "fileName", Gt(o2, "fileName"), "downloadUrl", Gt(o2, "downloadUrl"), "hashes", Gt(o2, "hashes"), "fileLength", Gt(o2, "fileLength"), "name", obj3, "slug", cf_slug);
			}
			obj = (Py.Truthy(obj) ? obj : ("项目 " + Py.Str(addon_id) + " 文件列表无可选文件"));
		}
		catch (Exception ex3)
		{
			obj = ex3;
			Utils.log.Warning("cf_files 失败: %s", Py.ExcStr(ex3));
		}
		if (Py.Truthy(cf_slug))
		{
			try
			{
				object[] array = _cf_latest_from_html(dm, Py.Str(cf_slug));
				return PyDict.Of("addon_id", addon_id, "file_id", array[0], "fileName", array[1], "downloadUrl", array[2], "name", Py.Or(_cf_name_from_html(dm, Py.Str(cf_slug)), obj3), "slug", cf_slug);
			}
			catch (Exception ex4)
			{
				obj = ex4;
				Utils.log.Warning("HTML 抓取失败(%s): %s", Py.Str(cf_slug), Py.ExcStr(ex4));
			}
		}
		string text = ((obj != null && Py.Truthy(ErrS(obj))) ? ErrS(obj) : "无可用文件");
		throw new ModpackError("获取 CurseForge 整合包文件失败: " + text + "（slug=" + Py.Str(cf_slug) + ", addon_id=" + Py.Str(addon_id) + "）");
	}

	public static PyDict InstallCfModpack(DownloadManager dm, object addon_id, Instance instance, string api_key = null, Action<string, long, long> on_progress = null, Func<bool> cancel = null, object cf_slug = null, object file_id = null, bool isolate = false, object version_name = null)
	{
		PyDict pyDict;
		if (Py.Truthy(file_id))
		{
			object obj = Mods.CfFiles(dm, addon_id, api_key, null, null, 50L).FirstOrDefault((object x) => Py.Str(Gt(x, "id")) == Py.Str(file_id));
			if (obj == null)
			{
				throw new ModpackError("找不到整合包文件 " + Py.Str(file_id));
			}
			pyDict = PyDict.Of("addon_id", addon_id, "file_id", Gt(obj, "id"), "fileName", Gt(obj, "fileName"), "downloadUrl", Gt(obj, "downloadUrl"), "hashes", Gt(obj, "hashes"), "fileLength", Gt(obj, "fileLength"), "name", Py.Or(cf_slug, "curseforge:" + Py.Str(addon_id)));
		}
		else
		{
			pyDict = ResolveCfModpackFile(dm, addon_id, api_key, cf_slug);
		}
		addon_id = pyDict.Req("addon_id");
		file_id = pyDict.Req("file_id");
		object obj2 = pyDict["fileName"];
		object obj3 = pyDict["downloadUrl"];
		string text = Py.Str(Py.Or(pyDict["name"], "curseforge:" + Py.Str(addon_id)));
		string text2 = Path.Combine(Path.GetTempPath(), "tipcl_cfpack_" + Py.Str(addon_id) + "_" + Py.Str(file_id) + ".zip");
		List<object> list = new List<object>();
		if (Py.Truthy(obj3))
		{
			list.Add(obj3);
		}
		if (Py.Truthy(obj2))
		{
			list.AddRange(Mods.CfModDownloadUrls(addon_id, file_id, obj2));
		}
		list.AddRange(Mods.CfModDownloadUrls(addon_id, file_id));
		on_progress?.Invoke("下载整合包 " + text, 0L, 1L);
		CatalogDownload.CurseForge(dm, list, text2, pyDict);
		try
		{
			return InstallCfZip(dm, text2, instance, on_progress, cancel, force: false, null, isolate, version_name);
		}
		finally
		{
			try
			{
				File.Delete(text2);
			}
			catch (Exception)
			{
			}
		}
	}

	private static object[] _cf_latest_from_html(DownloadManager dm, string slug, string class_path = "modpacks")
	{
		string text = "https://www.curseforge.com/minecraft/" + class_path + "/" + slug + "/files";
		Dictionary<string, string> headers = new Dictionary<string, string>(HtmlHeaders) { { "Referer", "https://www.curseforge.com/" } };
		string input = dm.FetchText(text, 60L, expand: true, headers);
		MatchCollection matchCollection = Regex.Matches(input, "/minecraft/" + class_path + "/" + Regex.Escape(slug) + "/files/(\\d+)");
		if (matchCollection.Count == 0)
		{
			throw new ModpackError("网页未找到文件链接: " + text);
		}
		long num = Py.Int(matchCollection[0].Groups[1].Value);
		string text2 = null;
		Match match = Regex.Match(input, "files/" + num + "/[^\"'<> ]+\\.zip");
		if (match.Success)
		{
			text2 = match.Value.Substring(match.Value.LastIndexOf('/') + 1);
		}
		if (string.IsNullOrEmpty(text2))
		{
			match = Regex.Match(input, "data-filename\\s*=\\s*[\"']([^\"']+\\.zip)");
			if (match.Success)
			{
				text2 = match.Groups[1].Value;
			}
		}
		if (string.IsNullOrEmpty(text2))
		{
			text2 = "modpack-" + num + ".zip";
		}
		return new object[3] { num, text2, null };
	}

	private static object _cf_name_from_html(DownloadManager dm, string slug, string class_path = "modpacks")
	{
		string url = "https://www.curseforge.com/minecraft/" + class_path + "/" + slug;
		try
		{
			Match match = Regex.Match(dm.FetchText(url, 60L, expand: true, new Dictionary<string, string>(HtmlHeaders)), "<title>\\s*(.+?)\\s*-\\s*Modpacks\\s*-\\s*CurseForge\\s*</title>", RegexOptions.IgnoreCase);
			if (match.Success)
			{
				return Py.Strip(match.Groups[1].Value);
			}
		}
		catch (Exception)
		{
		}
		return null;
	}

	public static List<object> SearchModpacksChinese(DownloadManager dm, string query, long limit = 25L, string api_key = null, object game_version = null, List<object> categories = null)
	{
		string text = Py.Strip(query);
		if (text.Length == 0)
		{
			return new List<object>();
		}
		List<object> list = (categories ?? new List<object>()).Where((object c) => CatalogFiles.TypeKey(c).Length > 0).Select<object, object>((Func<object, object>)((object c) => CatalogFiles.TypeKey(c))).ToList();
		List<object> list2 = new List<object>();
		object[] array = Catalog.LookupModpackAlias(text);
		object obj = array[0];
		object obj2 = array[1];
		object b = array[2];
		if (Py.Truthy(obj))
		{
			try
			{
				object obj3 = dm.FetchJson("https://api.modrinth.com/v2/project/" + Py.Str(obj), 60L);
				if (_match_filters_mr(obj3, game_version, list))
				{
					List<object> list3 = Iter(Py.Or(Gt(obj3, "game_versions"), new List<object>()));
					string text2 = Py.Str(Mods.SliceObj(Py.Or(Gt(obj3, "description"), ""), 120));
					if (list3.Count > 0)
					{
						text2 = "MC " + string.Join(", ", list3.Take(3).Select(Py.Str)) + " · " + text2;
					}
					list2.Add(PyDict.Of("source", "modrinth", "slug", Mods.Gd(obj3, "slug", obj), "title", Py.Or(Gt(obj3, "title"), b, obj), "author", CatalogPresentation.ModrinthAuthors(dm, Gt(obj3, "id") ?? obj), "downloads", Mods.Gd(obj3, "downloads", 0L), "description", text2, "matched_alias", true, "icon_url", Gt(obj3, "icon_url"), "gallery", Gt(obj3, "gallery"), "original_description", Gt(obj3, "description")));
				}
			}
			catch (Exception e)
			{
				Utils.log.Warning("整合包别名命中后 Modrinth 查询失败: %s", Py.ExcStr(e));
			}
		}
		if (Py.Truthy(obj2))
		{
			try
			{
				PyDict pyDict = Mods.CfDetail(dm, obj2, api_key);
				if (_match_filters_cf(pyDict, game_version, list))
				{
					List<object> list4 = new List<object>();
					foreach (object item in Iter(Py.Or(pyDict["latestFilesIndexes"], new List<object>())))
					{
						object obj4 = Gt(Py.Or(item, new PyDict()), "gameVersion");
						if (Py.Truthy(obj4) && !Py.In(obj4, list4))
						{
							list4.Add(obj4);
						}
					}
					string text3 = Py.Str(Mods.SliceObj(Py.Or(pyDict["summary"], ""), 120));
					if (list4.Count > 0)
					{
						text3 = "MC " + string.Join(", ", list4.Take(3).Select(Py.Str)) + " · " + text3;
					}
					string text4 = string.Join(", ", from a in Iter(Py.Or(pyDict["authors"], new List<object>()))
						select Py.Str(Mods.Gd(a, "name", "")));
                    var row = PyDict.Of("source", "curseforge", "id", pyDict["id"], "slug", pyDict["slug"], "title", Py.Or(pyDict["name"], b, Py.Str(obj2)), "author", (text4.Length > 0) ? text4 : "?", "downloads", Py.Or(pyDict["downloadCount"], 0L), "description", text3, "original_description", pyDict.Get("summary"), "matched_alias", true);
                    row.Update(CatalogPresentation.Artwork(pyDict));
                    list2.Add(row);
				}
			}
			catch (Exception e2)
			{
				Utils.log.Warning("整合包别名命中后 CurseForge 查询失败: %s", Py.ExcStr(e2));
			}
		}
		if (list2.Count > 0)
		{
			return Py.Slice(list2, null, (int)limit).ToList();
		}
		try
		{
			list2.AddRange(ModrinthSearch(dm, text, limit, game_version, list));
		}
		catch (Exception e3)
		{
			Utils.log.Warning("中文搜索回退 Modrinth 整合包失败: %s", Py.ExcStr(e3));
		}
		try
		{
			list2.AddRange(SearchCfModpacks(dm, text, limit, api_key, game_version, list));
		}
		catch (Exception e4)
		{
			Utils.log.Warning("中文搜索回退 CurseForge 整合包失败: %s", Py.ExcStr(e4));
		}
		return Py.Slice(list2, null, (int)limit).ToList();
	}

	private static bool _match_filters_mr(object project, object game_version, List<object> cat_keys)
	{
		if (Py.Truthy(game_version))
		{
			List<object> list = ((IEnumerable<object>)Iter(Py.Or(Gt(project, "game_versions"), new List<object>()))).Select((Func<object, object>)((object v) => Py.Str(v))).ToList();
			if (list.Count > 0 && !Py.In(game_version, list))
			{
				return false;
			}
		}
		if (cat_keys != null && cat_keys.Count > 0)
		{
			HashSet<string> other = new HashSet<string>(from c in Iter(Py.Or(Gt(project, "categories"), new List<object>()))
				select Py.Lower(Py.Str(c)));
			HashSet<string> hashSet = new HashSet<string>();
			foreach (object cat_key in cat_keys)
			{
				if (CatalogFiles.TYPE_FACETS.TryGetValue(Py.Str(cat_key), out string[] value))
				{
					string[] array = value;
					foreach (string item in array)
					{
						hashSet.Add(item);
					}
				}
			}
			if (hashSet.Count > 0 && !hashSet.Overlaps(other))
			{
				return false;
			}
		}
		return true;
	}

	private static bool _match_filters_cf(PyDict mod, object game_version, List<object> cat_keys)
	{
		if (Py.Truthy(game_version))
		{
			List<object> list = (from i in Iter(Py.Or(mod["latestFilesIndexes"], new List<object>()))
				select Py.S(Gt(Py.Or(i, new PyDict()), "gameVersion")) into g
				where g.Length > 0
				select g).Select((Func<string, object>)((string g) => g)).ToList();
			if (list.Count > 0 && !Py.In(game_version, list))
			{
				return false;
			}
		}
		if (cat_keys != null && cat_keys.Count > 0)
		{
			string names = string.Join(" ", from c in Iter(Py.Or(mod["categories"], new List<object>()))
				where c is PyDict
				select Py.Lower(Py.S(((PyDict)c)["name"])));
			List<string> list2 = new List<string>();
			foreach (object cat_key in cat_keys)
			{
				if (CatalogFiles.CF_TYPE_TOKENS.TryGetValue(Py.Str(cat_key), out string[] value))
				{
					list2.AddRange(value);
				}
			}
			if (list2.Count > 0 && !list2.Any((string t) => names.Contains(t)))
			{
				return false;
			}
		}
		return true;
	}

	public static List<object> ModrinthSearch(DownloadManager dm, object query, long limit = 25L, object game_version = null, List<object> categories = null)
	{
		string text = Mods._mr_facets("modpack", game_version, categories);
		string text2 = Py.Strip(Py.S(query));
		PyDict pyDict = PyDict.Of("query", text2, "facets", text, "limit", limit, "index", (text2.Length > 0) ? "relevance" : "downloads");
		if (categories != null && categories.Count > 0)
		{
			List<object> list = CatalogFiles.CategoryFacets((categories.Count == 1) ? categories[0] : "");
			if (categories.Count == 1 && list.Count == 0)
			{
				pyDict["facets"] = Mods._mr_facets("modpack", game_version);
			}
		}
		object obj = null;
		object o = null;
		bool flag = false;
		foreach (string item in Source.ModrinthApiBases())
		{
			try
			{
				o = dm.FetchJson(Mods.WithParams(item + "/search", pyDict), Py.L(3, 8), expand: false);
				flag = true;
			}
			catch (Exception ex)
			{
				obj = ex;
				Utils.log.Warning("Modrinth 搜索端点不可用 %s: %s", item, Py.ExcStr(ex));
				continue;
			}
			break;
		}
		if (!flag)
		{
			throw new ModpackError("搜索 Modrinth 整合包失败: " + ((obj == null) ? "None" : ErrS(obj)));
		}
		List<object> list2 = new List<object>();
		foreach (object item2 in Iter(Mods.Gd(o, "hits", new List<object>())))
		{
			object obj2 = Gt(item2, "slug");
			list2.Add(PyDict.Of("slug", obj2, "title", Mods.Gd(item2, "title", obj2), "description", Mods.SliceObj(Py.Or(Gt(item2, "description"), ""), 120), "author", Mods.Gd(item2, "author", "?"), "downloads", Mods.Gd(item2, "downloads", 0L), "game_versions", Py.Or(Gt(item2, "versions"), new List<object>()), "icon_url", Gt(item2, "icon_url"), "featured_gallery", Gt(item2, "featured_gallery"), "gallery", Gt(item2, "gallery"), "original_description", Gt(item2, "description")));
		}
		return list2;
	}

	public static object ModrinthProject(DownloadManager dm, object slug)
	{
		object obj = null;
		foreach (string item in Source.ModrinthApiBases())
		{
			try
			{
				return dm.FetchJson(item + "/project/" + Py.Str(slug), 12L, expand: false);
			}
			catch (Exception ex)
			{
				obj = ex;
				Utils.log.Warning("Modrinth 项目端点不可用 %s: %s", item, Py.ExcStr(ex));
			}
		}
		throw new ModpackError("找不到 Modrinth 项目 " + Py.Str(slug) + ": " + ((obj == null) ? "None" : ErrS(obj)));
	}

	public static List<object> ModrinthVersions(DownloadManager dm, object slug)
	{
		object o;
		try
		{
			o = dm.FetchJson("https://api.modrinth.com/v2/project/" + Py.Str(slug) + "/version", 60L);
		}
		catch (Exception e)
		{
			throw new ModpackError("获取整合包版本失败: " + Py.ExcStr(e));
		}
		List<object> list = new List<object>();
		foreach (object item in Iter(o))
		{
			List<object> list2 = new List<object>();
			foreach (object item2 in Iter(Mods.Gd(item, "files", new List<object>())))
			{
				object o2 = Py.Or(Gt(item2, "hashes"), new PyDict());
				list2.Add(PyDict.Of("url", Gt(item2, "url"), "filename", Gt(item2, "filename"), "primary", Mods.Gd(item2, "primary", false), "size", Mods.Gd(item2, "size", 0L), "sha1", Gt(o2, "sha1"), "sha512", Gt(o2, "sha512")));
			}
			list.Add(PyDict.Of("id", Gt(item, "id"), "name", Gt(item, "name"), "version_number", Gt(item, "version_number"), "version_type", Py.Or(Gt(item, "version_type"), "release"), "game_versions", Mods.Gd(item, "game_versions", new List<object>()), "loaders", Mods.Gd(item, "loaders", new List<object>()), "files", list2));
		}
		return list;
	}

	private static int RankType(object v)
	{
		object a = Py.Or(Gt(v, "version_type"), "release");
		if (!Py.Eq(a, "release"))
		{
			if (!Py.Eq(a, "beta"))
			{
				return 2;
			}
			return 1;
		}
		return 0;
	}

	private static object[] _pick_mrpack_file(List<object> versions)
	{
		List<object[]> list = new List<object[]>();
		foreach (object item in versions ?? new List<object>())
		{
			List<object> list2 = (from f in Iter(Py.Or(Gt(item, "files"), new List<object>()))
				where Py.Lower(Py.S(Gt(f, "filename"))).EndsWith(".mrpack", StringComparison.Ordinal) && Py.Truthy(Gt(f, "url"))
				select f).ToList();
			if (list2.Count != 0)
			{
				List<object> list3 = list2.Where((object f) => !Py.Lower(Py.S(Gt(f, "filename"))).Contains("server")).ToList();
				List<object> list4 = ((list3.Count > 0) ? list3 : list2);
				List<object> list5 = list4.Where((object f) => Py.Truthy(Gt(f, "primary"))).ToList();
				list.Add(new object[3]
				{
					RankType(item),
					(list5.Count > 0) ? list5 : list4,
					item
				});
			}
		}
		if (list.Count == 0)
		{
			return new object[2];
		}
		list = Py.Sorted(list, (object[] r) => r[0]);
		return new object[2]
		{
			((List<object>)list[0][1])[0],
			list[0][2]
		};
	}

	private static List<object[]> _mrpack_candidates(List<object> versions, int limit = 5)
	{
		List<object[]> list = new List<object[]>();
		HashSet<string> hashSet = new HashSet<string>();
		foreach (object item in versions ?? new List<object>())
		{
			object obj = _pick_mrpack_file(Py.L(item))[0];
			if (obj != null)
			{
				object o = Gt(obj, "url");
				if (Py.Truthy(o) && !hashSet.Contains(Py.Str(o)))
				{
					hashSet.Add(Py.Str(o));
					list.Add(new object[3]
					{
						RankType(item),
						obj,
						item
					});
				}
			}
		}
		list = Py.Sorted(list, (object[] x) => x[0]);
		return (from x in list.Take(limit)
			select new object[2]
			{
				x[1],
				x[2]
			}).ToList();
	}

	public static PyDict InstallMrpackBySlug(DownloadManager dm, object slug, Instance instance, Action<string, long, long> on_progress = null, Func<bool> cancel = null, bool force = false, string java = null, object version_id = null, bool isolate = false, object version_name = null)
	{
		object o = ModrinthProject(dm, slug);
		string text = Py.Str(Py.Or(Gt(o, "title"), slug));
		string text2 = Py.S(Py.Or(Gt(o, "project_type"), ""));
		if (text2 != "modpack")
		{
			throw new ModpackError("「" + text + "」(" + Py.Str(slug) + ") 是 " + ((text2.Length > 0) ? text2 : "未知类型") + "，不是整合包。请到「模组」页安装，或换一个真正的整合包（机械动力可搜黄铜协奏曲 CBC）。");
		}
		List<object> list = ModrinthVersions(dm, slug);
		if (Py.Truthy(version_id))
		{
			List<object> list2 = list.Where((object v) => Py.Str(Gt(v, "id")) == Py.Str(version_id)).ToList();
			if (list2.Count > 0)
			{
				list = list2;
			}
		}
		if (list.Count == 0)
		{
			throw new ModpackError("整合包 " + Py.Str(slug) + " 没有可下载的版本");
		}
		List<object[]> list3 = _mrpack_candidates(list, Py.Truthy(version_id) ? 1 : 5);
		if (list3.Count == 0)
		{
			List<object> list4 = (from f in Iter(Py.Or(Gt(list[0], "files"), new List<object>()))
				select Gt(f, "filename")).ToList();
			throw new ModpackError("「" + text + "」没有 .mrpack 文件（最新版本文件: " + ((list4.Count > 0) ? Py.Str(list4) : "无") + "）。它可能不是 Modrinth 整合包。");
		}
		Exception ex = null;
		_emit(on_progress, "整合包「" + text + "」共 " + list.Count + " 个版本，将依次尝试 " + list3.Count + " 个");
		for (int num = 0; num < list3.Count; num++)
		{
			if (cancel != null && cancel())
			{
				throw new ModpackError("已取消");
			}
			object o2 = list3[num][0];
			string text3 = Py.S(Py.Or(Gt(list3[num][1], "version_number"), Gt(o2, "filename"), ""));
			string text4 = Py.S(Py.Or(Gt(o2, "filename"), ""));
			_emit(on_progress, "[" + (num + 1) + "/" + list3.Count + "] 尝试 " + text3 + " (" + text4 + ")");
			try
			{
				return InstallMrpack(dm, Gt(o2, "url"), instance, on_progress, cancel, force, java, isolate, version_name);
			}
			catch (Exception ex2) when (ex2 is ModpackError || ex2 is InstallError || ex2 is VersionNotFound)
			{
				ex = ex2;
				_emit(on_progress, text3 + " 安装失败: " + Py.ExcStr(ex2));
				if (!PackRetryWorthwhile(ex2))
				{
					throw new ModpackError("「" + text + "」" + text3 + " 无法安装: " + Py.ExcStr(ex2));
				}
				if (num == list3.Count - 1)
				{
					break;
				}
				_emit(on_progress, "该版本装不上，尝试下一个整合包版本…");
				continue;
			}
		}
		throw new ModpackError("「" + text + "」多个版本均无法安装: " + ((ex == null) ? "None" : Py.ExcStr(ex)));
	}
}
