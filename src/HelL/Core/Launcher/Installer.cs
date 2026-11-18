using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace HelL.Core.Launcher;

public class Installer
{
	public static readonly string[] FORGE_MAVEN_MIRRORS = new string[2] { "https://maven.minecraftforge.net/net/minecraftforge/forge", "https://bmclapi2.bangbang93.com/maven/net/minecraftforge/forge" };

	public const string MOJANG_LIBRARIES = "https://libraries.minecraft.net/";

	public static readonly PyDict NEOFORGE_MC_MAP = PyDict.Of("1.20.1", "47.1", "1.20.2", "20.2", "1.20.3", "20.3", "1.20.4", "20.4", "1.20.5", "20.5", "1.20.6", "20.6", "1.21", "21.0", "1.21.1", "21.1");

	public Instance instance;

	public DownloadManager dm;

	public Action<string, long, long> on_progress;

	public Func<bool> cancel;

	private string _java_cache;

	public bool skip_assets;

	private static readonly Regex _PROC_RE = new Regex("\\{([A-Za-z_]+)\\}", RegexOptions.CultureInvariant);

	private static readonly Regex _PROC_LEFT_RE = new Regex("\\{[A-Za-z_$][^{}]*\\}", RegexOptions.CultureInvariant);

	private static readonly Regex _FORGE_ARG_RE = new Regex("\\{([A-Za-z0-9_]+)\\}", RegexOptions.CultureInvariant);

	public const string FABRIC_META = "https://meta.fabricmc.net/v2";

	public const string QUILT_META = "https://meta.quiltmc.org/v3";

	public const string FORGE_MAVEN = "https://maven.minecraftforge.net/net/minecraftforge/forge";

	public const string BMCLAPI = "https://bmclapi2.bangbang93.com";

	public const string NEOFORGE_MAVEN = "https://maven.neoforged.net/releases/net/neoforged/neoforge";

	public Installer(Instance instance, DownloadManager dm = null, Action<string, long, long> on_progress = null, Func<bool> cancel = null)
	{
		this.instance = instance;
		this.dm = dm ?? new DownloadManager(null, on_progress, cancel);
		this.on_progress = on_progress;
		this.cancel = cancel ?? ((Func<bool>)(() => false));
	}

	public static string MatchForgeArtifact(IList<string> candidates, string mc_version, string forge_version)
	{
		if (string.IsNullOrEmpty(forge_version))
		{
			return null;
		}
		if (candidates.Contains(forge_version))
		{
			return forge_version;
		}
		string text = mc_version + "-" + forge_version;
		string text2 = text + "-" + mc_version;
		if (candidates.Contains(text2))
		{
			return text2;
		}
		if (candidates.Contains(text))
		{
			return text;
		}
		List<string> list = candidates.Where((string v) => v.Split('-').Contains<string>(forge_version)).ToList();
		if (list.Count == 0)
		{
			list = candidates.Where((string v) => v.Contains(forge_version)).ToList();
		}
		if (list.Count == 0)
		{
			return null;
		}
		List<string> list2 = list.Where((string v) => v.StartsWith(mc_version + "-", StringComparison.Ordinal)).ToList();
		return Py.Sorted((list2.Count > 0) ? list2 : list, (string v) => Py.L((long)v.Length, v)).Last();
	}

	public static List<string> ForgeArtifactGuesses(string mc_version, string forge_version)
	{
		string text = Py.Strip(mc_version ?? "");
		string text2 = Py.Strip(forge_version ?? "");
		if (Py.Lower(text2).StartsWith("forge-"))
		{
			text2 = text2.Substring(6);
		}
		List<string> o = new List<string>();
		if (text.Length == 0 || text2.Length == 0)
		{
			return o;
		}
		if (text2 == text || text2.StartsWith(text + "-", StringComparison.Ordinal))
		{
			add(text2);
			return o;
		}
		add(text + "-" + text2);
		add(text + "-" + text2 + "-" + text);
		return o;
		void add(string item)
		{
			if (!string.IsNullOrEmpty(item) && !o.Contains(item))
			{
				o.Add(item);
			}
		}
	}

	public static bool IsForgeInstallerJar(string path)
	{
		PPath pPath = new PPath(path);
		if (!pPath.IsFile() || new FileInfo(pPath.Abs()).Length < 1024)
		{
			return false;
		}
		try
		{
			using ZipArchive zipArchive = Utils.OpenZip(path);
			HashSet<string> hashSet = new HashSet<string>(zipArchive.Entries.Select(Utils.ZipEntryName));
			return hashSet.Contains("install_profile.json") || hashSet.Contains("version.json");
		}
		catch (InvalidDataException)
		{
			return false;
		}
	}

	private static string _library_base_url(PyDict lib)
	{
		string text = Py.RStrip(Py.Str(Py.Or(lib.Get("url"), "https://libraries.minecraft.net/")), "/") + "/";
		if (text.StartsWith("http://files.minecraftforge.net/maven"))
		{
			return "https://maven.minecraftforge.net/";
		}
		return text;
	}

	private static bool IsSha1(object h)
	{
		if (h is string { Length: 40 } text)
		{
			return text.All((char c) => "0123456789abcdefABCDEF".IndexOf(c) >= 0);
		}
		return false;
	}

	private static string _lib_sha1(PyDict lib)
	{
		if (IsSha1(lib.Get("sha1")))
		{
			return (string)lib["sha1"];
		}
		PyDict pyDict = Py.DictOr(Py.Or(Py.DictOr(Py.Or(lib.Get("downloads"), new PyDict())).Get("artifact"), new PyDict()));
		if (IsSha1(pyDict.Get("sha1")))
		{
			return (string)pyDict["sha1"];
		}
		foreach (object item in (Py.Or(lib.Get("checksums"), new List<object>()) as IEnumerable) ?? new List<object>())
		{
			if (IsSha1(item))
			{
				return (string)item;
			}
		}
		return null;
	}

	private static long? _lib_size(PyDict lib)
	{
		PyDict pyDict = Py.DictOr(Py.Or(Py.DictOr(Py.Or(lib.Get("downloads"), new PyDict())).Get("artifact"), new PyDict()));
		object[] array = new object[2]
		{
			lib.Get("size"),
			pyDict.Get("size")
		};
		foreach (object obj in array)
		{
			if (obj != null)
			{
				try
				{
					return Py.Int(obj);
				}
				catch (TypeError)
				{
				}
				catch (ValueError)
				{
				}
			}
		}
		return null;
	}

	public static string NativeArchToken()
	{
		if (!(Utils.ARCH == "x86"))
		{
			return "64";
		}
		return "32";
	}

	public static string _maven_relpath(string spec)
	{
		string text = Py.Strip(spec ?? "");
		if (text.StartsWith("[") && text.EndsWith("]"))
		{
			text = text.Substring(1, text.Length - 2);
		}
		string suffix = "jar";
		if (text.Contains("@"))
		{
			List<string> list = Py.RSplit(text, "@", 1);
			text = list[0];
			suffix = list[1];
		}
		return Utils.MavenArtifactPath(text, suffix);
	}

	public static string SubstNativeKey(string key)
	{
		if (string.IsNullOrEmpty(key))
		{
			return key;
		}
		return key.Replace("${arch}", NativeArchToken());
	}

	public static string SelectNativeClassifier(PyDict lib)
	{
		PyDict pyDict = Py.DictOr(Py.Or(Py.DictOr(Py.Or(lib.Get("downloads"), new PyDict())).Get("classifiers"), new PyDict()));
		object obj = Py.DictOr(Py.Or(lib.Get("natives"), new PyDict())).Get(Utils.OS_NAME);
		string text = ((obj == null) ? null : SubstNativeKey(Py.AsStr(obj, "replace")));
		string[] aliases = ((Utils.OS_NAME == "osx") ? new string[2] { "osx", "macos" } : ((Utils.OS_NAME == "windows") ? new string[1] { "windows" } : ((!(Utils.OS_NAME == "linux")) ? new string[0] : new string[1] { "linux" })));
		string[] archSuffixes = ((Utils.ARCH == "x64") ? new string[4] { "64", "x64", "x86_64", "amd64" } : ((Utils.ARCH == "x86") ? new string[4] { "32", "x86", "i386", "i686" } : ((!(Utils.ARCH == "arm64")) ? new string[0] : new string[2] { "arm64", "aarch64" })));
		if (pyDict.Count == 0)
		{
			if (string.IsNullOrEmpty(text) || rank(text) == 0)
			{
				return null;
			}
			return text;
		}
		List<string> list = pyDict.Keys.Select(SubstNativeKey).ToList();
		if (text != null && list.Contains(text) && rank(text) != 0)
		{
			return text;
		}
		List<string> list2 = list.Where((string k) => rank(k) != 0).ToList();
		if (list2.Count == 0)
		{
			return null;
		}
		string text2 = list2[0];
		foreach (string item in list2)
		{
			if (rank(item) > rank(text2))
			{
				text2 = item;
			}
		}
		return text2;
		int rank(string key)
		{
			if (key == null)
			{
				return 0;
			}
			string[] array = aliases;
			foreach (string text3 in array)
			{
				string text4 = "natives-" + text3;
				if (key == text4)
				{
					if (!(Utils.ARCH == "x86") && !(Utils.ARCH == "x64"))
					{
						return 0;
					}
					return 1;
				}
				if (key.StartsWith(text4 + "-"))
				{
					if (!archSuffixes.Contains<string>(key.Substring(text4.Length + 1)))
					{
						return 0;
					}
					return 2;
				}
			}
			return 0;
		}
	}

	public static string NativesJarRelpath(PyDict lib, string nkey)
	{
		PyDict pyDict = Py.DictOr(Py.Or(Py.DictOr(Py.Or(lib.Get("downloads"), new PyDict())).Get("classifiers"), new PyDict()));
		object o = pyDict.Get(nkey);
		if (!Py.Truthy(o))
		{
			foreach (KeyValuePair<string, object> item in pyDict)
			{
				if (SubstNativeKey(item.Key) == nkey)
				{
					o = item.Value;
					break;
				}
			}
		}
		PyDict pyDict2 = (Py.Truthy(o) ? Py.DictOr(o) : new PyDict());
		string text = Py.Str(Py.Or(lib.Get("name"), ""));
		if (!Py.Truthy(pyDict2.Get("path")))
		{
			return Utils.MavenArtifactPath(text + ":" + nkey);
		}
		return Py.Str(pyDict2["path"]);
	}

	public static bool NativesPresent(string natives_dir)
	{
		if (!new PPath(natives_dir).IsDir())
		{
			return false;
		}
		foreach (string item in Directory.EnumerateFileSystemEntries(Utils.AbsPath(natives_dir)))
		{
			if (File.Exists(item))
			{
				string text = Path.GetFileName(item).ToLowerInvariant();
				if (text.StartsWith("lwjgl") || text.StartsWith("liblwjgl"))
				{
					return true;
				}
			}
		}
		return false;
	}

	public static string NativesDir(Instance inst, string version_id, PyDict version_json = null)
	{
		if (Py.Truthy(version_json) && Manifest.IsLegacyVersion(version_json))
		{
			return PPath.Join(inst.path, "bin", "natives");
		}
		return PPath.Join(inst.VersionsDir(), version_id, version_id + "-natives");
	}

	internal static IEnumerable<PyDict> Libs(PyDict resolved)
	{
		foreach (object item in (Py.Or(resolved.Get("libraries"), new List<object>()) as IEnumerable) ?? new List<object>())
		{
			yield return (item as PyDict) ?? new PyDict();
		}
	}

	internal static List<string> StrList(object o)
	{
		List<string> list = new List<string>();
		foreach (object item in (Py.Or(o, new List<object>()) as IEnumerable) ?? new List<object>())
		{
			list.Add(Py.Str(item));
		}
		return list;
	}

	public static string ExtractNatives(Instance instance, PyDict resolved, string version_id)
	{
		string text = NativesDir(instance, version_id, resolved);
		Utils.EnsureDir(text);
		string text2 = instance.LibrariesDir();
		int num = 0;
		foreach (PyDict item in Libs(resolved))
		{
			if ((Py.Eq(item.Get("clientreq"), false) && item.Get("clientreq") is bool) || !Utils.CheckRules(item.Get("rules")))
			{
				continue;
			}
			string text3 = SelectNativeClassifier(item);
			if (string.IsNullOrEmpty(text3))
			{
				continue;
			}
			string text4 = PPath.Join(text2, NativesJarRelpath(item, text3));
			if (!new PPath(text4).IsFile())
			{
				Utils.log.Warning("缺少 natives jar: %s", text4);
				continue;
			}
			List<string> exclude = StrList(Py.DictOr(Py.Or(item.Get("extract"), new PyDict())).Get("exclude"));
			try
			{
				DownloadManager.ExtractJarNatives(text4, text, exclude);
				num++;
			}
			catch (Exception e)
			{
				Utils.log.Warning("解压 natives 失败 %s: %s", text4, Py.ExcStr(e));
			}
		}
		if (num > 0 && NativesPresent(text))
		{
			return text;
		}
		object obj = Py.Or(resolved.Get("inheritsFrom"), resolved.Get("jar"));
		if (Py.Truthy(obj) && !Py.Eq(obj, version_id))
		{
			PyDict pyDict = instance.VersionJson(Py.Str(obj)) as PyDict;
			if (Py.Truthy(pyDict))
			{
				string text5 = NativesDir(instance, Py.Str(obj), pyDict);
				if (NativesPresent(text5) && !new PPath(text5).Resolve().Equals(new PPath(text).Resolve()))
				{
					foreach (string item2 in Directory.EnumerateFiles(Utils.AbsPath(text5)))
					{
						CopyFile2(item2, Path.Combine(Utils.AbsPath(text), Path.GetFileName(item2)));
					}
					Utils.log.Info("已从父版本 %s 复制 natives 到 %s", obj, text);
				}
			}
		}
		return text;
	}

	internal static void CopyFile2(string src, string dst)
	{
		File.Copy(Utils.AbsPath(src), Utils.AbsPath(dst), overwrite: true);
		try
		{
			File.SetLastWriteTimeUtc(Utils.AbsPath(dst), File.GetLastWriteTimeUtc(Utils.AbsPath(src)));
		}
		catch (Exception)
		{
		}
	}

	internal static void CopyTree(string src, string dst)
	{
		string text = Utils.AbsPath(src);
		string text2 = Utils.AbsPath(dst);
		Directory.CreateDirectory(text2);
		string[] directories = Directory.GetDirectories(text, "*", SearchOption.AllDirectories);
		foreach (string text3 in directories)
		{
			Directory.CreateDirectory(Path.Combine(text2, text3.Substring(text.Length).TrimStart(Path.DirectorySeparatorChar)));
		}
		directories = Directory.GetFiles(text, "*", SearchOption.AllDirectories);
		foreach (string text4 in directories)
		{
			CopyFile2(text4, Path.Combine(text2, text4.Substring(text.Length).TrimStart(Path.DirectorySeparatorChar)));
		}
	}

	private static byte[] ZipRead(string jar, string name)
	{
		using (ZipArchive zipArchive = Utils.OpenZip(jar))
		{
			foreach (ZipArchiveEntry entry in zipArchive.Entries)
			{
				if (Utils.ZipEntryName(entry) != name)
				{
					continue;
				}
				using Stream stream = entry.Open();
				using MemoryStream memoryStream = new MemoryStream();
				stream.CopyTo(memoryStream);
				return memoryStream.ToArray();
			}
		}
		throw new KeyError("\"There is no item named " + Py.ReprStr(name) + " in the archive\"");
	}

	private static string MkdTemp(string prefix)
	{
		string tempPath = Path.GetTempPath();
		int num = 0;
		string text;
		while (true)
		{
			text = Path.Combine(tempPath, prefix + Guid.NewGuid().ToString("N").Substring(0, 8));
			if (!Directory.Exists(text) && !File.Exists(text))
			{
				break;
			}
			num++;
		}
		Directory.CreateDirectory(text);
		return text;
	}

	private static string AsciiReplace(string s)
	{
		StringBuilder stringBuilder = new StringBuilder(s.Length);
		foreach (char c in s)
		{
			stringBuilder.Append((c < '\u0080') ? c : '?');
		}
		return Regex.Replace(stringBuilder.ToString(), "\\?{2}", (Match m) => m.Value);
	}

	private static string AsciiReplacePy(string s)
	{
		StringBuilder stringBuilder = new StringBuilder(s.Length);
		for (int i = 0; i < s.Length; i++)
		{
			char c = s[i];
			if (c < '\u0080')
			{
				stringBuilder.Append(c);
				continue;
			}
			stringBuilder.Append('?');
			if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
			{
				i++;
			}
		}
		return stringBuilder.ToString();
	}

	public void _note(object msg0, long done = 0L, long total = 1L)
	{
		string text = Py.Strip(Py.S(msg0));
		if (text.Length != 0)
		{
			Utils.log.Info("%s", text);
			on_progress?.Invoke(text, done, total);
		}
	}

	private void _proc_line(string line, long done = 0L, long total = 1L, string prefix = "Forge 处理器")
	{
		string text = Py.Strip(AsciiReplacePy(line ?? ""));
		if (text.Length != 0 && !text.StartsWith("Patching ") && !text.StartsWith("Processing "))
		{
			on_progress?.Invoke(prefix + ": " + Py.Slice(text, null, 240), done, total);
		}
	}

	public void InstallVersion(string version_id, bool force = false, string java = null)
	{
		Instance instance = this.instance;
		instance.EnsureStandardDirs();
		PyDict vjson;
		try
		{
			vjson = Manifest.GetVersionJson(dm, version_id, force);
		}
		catch (VersionNotFound)
		{
			PyDict pyDict = instance.VersionJson(version_id) as PyDict;
			if (!Py.Truthy(pyDict))
			{
				string text = Manifest.ResolvePlayableVersion(dm, version_id);
				if (!string.IsNullOrEmpty(text) && text != version_id)
				{
					on_progress?.Invoke("版本 " + version_id + " 不存在，自动改用 " + text, 0L, 1L);
					Utils.log.Info("版本 %s 不存在，自动改用 %s", version_id, text);
					InstallVersion(text, force, java);
					return;
				}
				throw;
			}
			vjson = pyDict;
		}
		_install_json(version_id, vjson, force, java);
	}

	public void _install_json(string version_id, PyDict vjson, bool force = false, string java = null)
	{
		Instance instance = this.instance;
		string text = PPath.Join(instance.VersionsDir(), version_id);
		Utils.EnsureDir(text);
		PyDict pyDict = new PyDict();
		foreach (KeyValuePair<string, object> item in vjson)
		{
			if (item.Key != "__tipcl_cached_at")
			{
				pyDict[item.Key] = item.Value;
			}
		}
		Utils.WriteJson(PPath.Join(text, version_id + ".json"), pyDict);
		if (Py.Truthy(vjson.Get("inheritsFrom")))
		{
			string text2 = Py.Str(vjson["inheritsFrom"]);
			if (!instance.HasVersion(text2))
			{
				on_progress?.Invoke("安装依赖版本 " + text2, 0L, 1L);
				PyDict vjson2 = _load_parent_json(text2);
				_install_json(text2, vjson2, force, java);
			}
		}
		PyDict pyDict2 = Manifest.ResolveInherits(vjson, _load_parent_json);
		FileMirrors.RegisterVersion(vjson);
		object o = Py.DictOr(Py.Or(Py.DictOr(Py.Or(vjson.Get("downloads"), new PyDict())).Get("client"), new PyDict())).Get("url");
		object o2 = Py.Or(vjson.Get("jar"), Py.Truthy(o) ? null : vjson.Get("inheritsFrom"));
		string text3 = (Py.Truthy(o2) ? PPath.Join(instance.VersionsDir(), Py.Str(o2), Py.Str(o2) + ".jar") : null);
		PyDict pyDict3 = Py.DictOr(Py.Or(pyDict2.Get("downloads"), new PyDict())).Get("client") as PyDict;
		string dest = PPath.Join(text, version_id + ".jar");
		if (text3 != null && new PPath(text3).IsFile() && !force && (pyDict3 == null || Utils.FileMatches(text3, pyDict3.Get("sha1"), pyDict3.Get("size"))))
		{
			on_progress?.Invoke("复用客户端 jar " + Py.Str(o2), 1L, 1L);
		}
		else
		{
			if (!Py.Truthy(pyDict3) || !Py.Truthy(pyDict3.Get("url")))
			{
				throw new InstallError("版本 " + version_id + " 缺少客户端 jar 下载信息");
			}
			on_progress?.Invoke("下载客户端 jar " + version_id, 0L, 1L);
			dm.Download(pyDict3["url"], text3 ?? dest, pyDict3.Get("sha1") as string, (pyDict3.Get("size") == null) ? ((long?)null) : new long?(Py.Int(pyDict3["size"])), force);
		}
		_install_libraries(pyDict2, version_id, force);
		if (Py.Truthy(vjson.Get("processors")))
		{
			string vanilla_jar = _vanilla_jar_for(vjson, pyDict2);
			_run_processors(vjson, pyDict2, text, vanilla_jar, force, java);
		}
		if (skip_assets)
		{
			_note("已跳过资源文件校验", 0L, 1L);
		}
		else
		{
			_install_assets(pyDict2, force);
		}
		_install_logging(pyDict2);
		instance.SetMeta("mc_version", version_id);
		Utils.log.Info("版本 %s 安装完成 -> %s", version_id, text);
	}

	public PyDict _load_parent_json(string parent_id)
	{
		PyDict pyDict = instance.VersionJson(parent_id) as PyDict;
		if (Py.Truthy(pyDict))
		{
			FileMirrors.RegisterVersion(pyDict);
			return pyDict;
		}
		try
		{
			return Manifest.GetVersionJson(dm, parent_id);
		}
		catch (VersionNotFound)
		{
			return null;
		}
	}

	private string _vanilla_jar_for(PyDict vjson, PyDict resolved)
	{
		object o = vjson.Get("inheritsFrom");
		if (Py.Truthy(o))
		{
			string text = PPath.Join(instance.VersionsDir(), Py.Str(o), Py.Str(o) + ".jar");
			if (new PPath(text).IsFile())
			{
				return text;
			}
		}
		string text2 = Py.Str(resolved.Get("id"));
		return PPath.Join(instance.VersionsDir(), text2, text2 + ".jar");
	}

	public void _install_libraries(PyDict resolved, string version_id, bool force = false)
	{
		Instance obj = instance;
		string text = obj.LibrariesDir();
		string text2 = NativesDir(obj, version_id, resolved);
		Utils.EnsureDir(text2);
		List<DownloadTask> list = new List<DownloadTask>();
		List<KeyValuePair<string, PyDict>> list2 = new List<KeyValuePair<string, PyDict>>();
		foreach (PyDict item in Libs(resolved))
		{
			object obj2 = item.Get("clientreq");
			if ((obj2 is bool && !(bool)obj2) || !Utils.CheckRules(item.Get("rules")))
			{
				continue;
			}
			object o = item.Get("name");
			if (!Py.Truthy(o))
			{
				continue;
			}
			string text3 = Py.Str(o);
			PyDict pyDict = Py.DictOr(Py.Or(item.Get("downloads"), new PyDict()));
			PyDict pyDict2 = pyDict.Get("artifact") as PyDict;
			if (Py.Truthy(pyDict2) && Py.Truthy(pyDict2.Get("url")))
			{
				string text4 = (Py.Truthy(pyDict2.Get("path")) ? Py.Str(pyDict2["path"]) : Utils.MavenArtifactPath(text3));
				string text5 = PPath.Join(text, text4);
				if (force || !Utils.FileMatches(text5, pyDict2.Get("sha1"), pyDict2.Get("size")))
				{
					list.Add(new DownloadTask(pyDict2["url"], text5, pyDict2.Get("sha1") as string, (pyDict2.Get("size") == null) ? ((long?)null) : new long?(Py.Int(pyDict2["size"]))));
				}
			}
			else if (pyDict.Count == 0 && !Py.Truthy(item.Get("natives")))
			{
				string text6 = Utils.MavenArtifactPath(text3);
				string text7 = PPath.Join(text, text6);
				string sha = _lib_sha1(item);
				long? size = _lib_size(item);
				if (force || !Utils.FileMatches(text7, sha, size.HasValue ? ((object)size.Value) : null))
				{
					list.Add(new DownloadTask(_library_base_url(item) + text6, text7, sha, size));
				}
			}
			else if (!Py.Truthy(pyDict.Get("classifiers")))
			{
				Utils.log.Warning("库 %s 没有可下载的 artifact，跳过", text3);
			}
			string text8 = SelectNativeClassifier(item);
			if (string.IsNullOrEmpty(text8))
			{
				continue;
			}
			PyDict pyDict3 = Py.DictOr(Py.Or(pyDict.Get("classifiers"), new PyDict()));
			PyDict pyDict4 = Py.DictOr(Py.Or(pyDict3.Get(text8), new PyDict()));
			if (pyDict4.Count == 0)
			{
				foreach (KeyValuePair<string, object> item2 in pyDict3)
				{
					if (SubstNativeKey(item2.Key) == text8)
					{
						pyDict4 = Py.DictOr(Py.Or(item2.Value, new PyDict()));
						break;
					}
				}
			}
			string text9 = (Py.Truthy(pyDict4.Get("path")) ? Py.Str(pyDict4["path"]) : Utils.MavenArtifactPath(text3 + ":" + text8));
			string text10 = PPath.Join(text, text9);
			object url = (Py.Truthy(pyDict4.Get("url")) ? pyDict4["url"] : (_library_base_url(item) + text9.Replace("\\", "/")));
			if (force || !Utils.FileMatches(text10, pyDict4.Get("sha1"), pyDict4.Get("size")))
			{
				list.Add(new DownloadTask(url, text10, pyDict4.Get("sha1") as string, (pyDict4.Get("size") == null) ? ((long?)null) : new long?(Py.Int(pyDict4["size"]))));
			}
			list2.Add(new KeyValuePair<string, PyDict>(text10, Py.DictOr(Py.Or(item.Get("extract"), new PyDict()))));
		}
		if (list.Count > 0)
		{
			dm.DownloadAll(list, "下载依赖库");
		}
		if (list2.Count <= 0)
		{
			return;
		}
		on_progress?.Invoke("解压 natives", 0L, list2.Count);
		for (int i = 0; i < list2.Count; i++)
		{
			if (cancel())
			{
				throw new InstallError("用户取消");
			}
			string key = list2[i].Key;
			PyDict value = list2[i].Value;
			try
			{
				DownloadManager.ExtractJarNatives(key, text2, StrList(value.Get("exclude")));
			}
			catch (Exception e)
			{
				throw new InstallError("解压 natives 失败 " + new PPath(key).Name + ": " + Py.ExcStr(e));
			}
			on_progress?.Invoke("解压 natives", i + 1, list2.Count);
		}
	}

	private void _run_processors(PyDict child_json, PyDict resolved, string vdir, string vanilla_jar, bool force = false, string java = null)
	{
		if (!Py.Truthy(child_json.Get("processors")))
		{
			return;
		}
		string item = java ?? _ensure_any_java();
		string libsDir = instance.LibrariesDir();
		List<PyDict> libs = Libs(resolved).ToList();
		List<object> list = (List<object>)child_json["processors"];
		int total = list.Count;
		for (int i = 0; i < list.Count; i++)
		{
			PyDict pyDict = Py.DictOr(list[i]);
			if (!StrList(pyDict.Get("sides", Py.L("client"))).Contains("client"))
			{
				continue;
			}
			PyDict data = PyDict.Of("SIDE", "client", "MINECRAFT_JAR", vanilla_jar, "ROOT", vdir, "LIBRARY_DIR", libsDir);
			PyDict pyDict2 = new PyDict();
			foreach (KeyValuePair<string, object> item2 in Py.DictOr(Py.Or(pyDict.Get("outputs"), new PyDict())))
			{
				pyDict2[sub(item2.Key)] = item2.Value;
			}
			if (pyDict2.Count > 0 && !force && pyDict2.All((KeyValuePair<string, object> kv) => Utils.FileMatches(kv.Key, Py.Or(kv.Value, null))))
			{
				continue;
			}
			string text = libPath(Py.Str(pyDict.Req("jar")));
			List<string> list2 = new List<string> { text };
			foreach (string item3 in StrList(pyDict.Get("classpath")))
			{
				list2.Add(libPath(item3));
			}
			string text2 = _jar_main_class(text);
			if (string.IsNullOrEmpty(text2))
			{
				throw new InstallError("处理器 jar 没有 Main-Class: " + Py.Str(pyDict["jar"]));
			}
			List<string> list3 = new List<string>();
			foreach (string item4 in StrList(pyDict.Get("args")))
			{
				string text3 = sub(item4);
				if (_PROC_LEFT_RE.IsMatch(text3))
				{
					Utils.log.Warning("处理器参数含未知占位符，跳过该参数: %s", text3);
				}
				else
				{
					list3.Add(text3);
				}
			}
			on_progress?.Invoke("运行 Forge 处理器 " + (i + 1) + "/" + total + ": " + text2, i, total);
			List<string> procLines = new List<string>();
			int ii = i;
			List<string> list4 = new List<string>();
			list4.Add(item);
			list4.Add("-cp");
			list4.Add(string.Join(Path.PathSeparator.ToString(), list2));
			list4.Add(text2);
			list4.AddRange(list3);
			int num = Utils.RunProcess(list4, vdir, delegate(string line)
			{
				procLines.Add(line);
				_proc_line(line, ii + 1, total);
			});
			if (num != 0)
			{
				string text4 = string.Join(" | ", procLines.Skip(Math.Max(0, procLines.Count - 6)).Select(AsciiReplacePy));
				throw new InstallError("Forge 处理器运行失败 (退出码 " + num + "): " + Py.Str(pyDict["jar"]) + ((text4.Length > 0) ? (" · " + text4) : ""));
			}
			List<object> list5 = pyDict2.Where((KeyValuePair<string, object> kv) => !Utils.FileMatches(kv.Key, Py.Or(kv.Value, null))).Select<KeyValuePair<string, object>, object>((Func<KeyValuePair<string, object>, object>)((KeyValuePair<string, object> kv) => kv.Key)).ToList();
			if (list5.Count > 0)
			{
				Utils.log.Warning("处理器未生成预期输出: %s", Py.Repr(list5));
			}
			string sub(string s)
			{
				return _PROC_RE.Replace(s, (Match m) => data.ContainsKey(m.Groups[1].Value) ? Py.Str(data[m.Groups[1].Value]) : m.Value);
			}
		}
		PyDict findLib(string mavenName)
		{
			return libs.FirstOrDefault((PyDict l) => Py.Eq(l.Get("name"), mavenName));
		}
		string libPath(string mavenName)
		{
			PyDict pyDict3 = Py.DictOr(Py.Or(Py.DictOr(Py.Or((findLib(mavenName) ?? throw new InstallError("找不到处理器依赖库: " + mavenName)).Get("downloads"), new PyDict())).Get("artifact"), new PyDict()));
			string text5 = (Py.Truthy(pyDict3.Get("path")) ? Py.Str(pyDict3["path"]) : Utils.MavenArtifactPath(mavenName));
			string text6 = PPath.Join(libsDir, text5);
			if (!new PPath(text6).IsFile())
			{
				throw new InstallError("处理器依赖库未下载: " + text5);
			}
			return text6;
		}
	}

	public static string _jar_main_class(string jar_path)
	{
		try
		{
			foreach (string item in Py.SplitLines(Encoding.UTF8.GetString(ZipRead(jar_path, "META-INF/MANIFEST.MF"))))
			{
				if (Py.Lower(item).StartsWith("main-class:"))
				{
					return Py.Strip(Py.Split(item, ":", 1)[1]);
				}
			}
		}
		catch (Exception e)
		{
			Utils.log.Warning("读取 %s 的 MANIFEST 失败: %s", jar_path, Py.ExcStr(e));
		}
		return null;
	}

	private string _ensure_any_java()
	{
		if (!string.IsNullOrEmpty(_java_cache))
		{
			return _java_cache;
		}
		using (List<object>.Enumerator enumerator = Java.ListInstalledJavas().GetEnumerator())
		{
			if (enumerator.MoveNext())
			{
				PyDict pyDict = (PyDict)enumerator.Current;
				_java_cache = Py.Str(pyDict["exe"]);
				return _java_cache;
			}
		}
		using (List<object>.Enumerator enumerator2 = Java.FindSystemJavas().GetEnumerator())
		{
			if (enumerator2.MoveNext())
			{
				PyDict pyDict2 = (PyDict)enumerator2.Current;
				_java_cache = Py.Str(pyDict2["exe"]);
				return _java_cache;
			}
		}
		_java_cache = Java.InstallAdoptium(dm, 8L, on_progress);
		return _java_cache;
	}

	public void _install_assets(PyDict resolved, bool force = false)
	{
		Instance instance = this.instance;
		PyDict pyDict = resolved.Get("assetIndex") as PyDict;
		if (!Py.Truthy(pyDict) || !Py.Truthy(pyDict.Get("url")))
		{
			return;
		}
		string text = instance.AssetsDir();
		string text2 = Py.Str(pyDict.Req("id"));
		string text3 = PPath.Join(text, "indexes", text2 + ".json");
		bool flag = force || !Utils.FileMatches(text3, pyDict.Get("sha1"), pyDict.Get("size"));
		if (!Py.Truthy(pyDict.Get("sha1")) && !Py.Truthy(pyDict.Get("size")))
		{
			flag = force || !new PPath(text3).IsFile();
		}
		long? size = ((pyDict.Get("size") == null) ? ((long?)null) : new long?(Py.Int(pyDict["size"])));
		if (flag)
		{
			on_progress?.Invoke("下载资源索引 " + text2, 0L, 1L);
			dm.Download(pyDict["url"], text3, pyDict.Get("sha1") as string, size, force: true);
		}
		PyDict pyDict2 = Py.DictOr(Py.Or(Utils.ReadJson(text3), new PyDict()));
		PyDict pyDict3 = Py.DictOr(Py.Or(pyDict2.Get("objects"), new PyDict()));
		if (pyDict3.Count == 0 && (Py.Truthy(pyDict.Get("sha1")) || Py.Truthy(pyDict.Get("url"))))
		{
			on_progress?.Invoke("资源索引无效，重新下载 " + text2, 0L, 1L);
			dm.Download(pyDict["url"], text3, pyDict.Get("sha1") as string, size, force: true);
			pyDict2 = Py.DictOr(Py.Or(Utils.ReadJson(text3), new PyDict()));
			pyDict3 = Py.DictOr(Py.Or(pyDict2.Get("objects"), new PyDict()));
		}
		if (pyDict3.Count == 0)
		{
			throw new InstallError("资源索引为空或损坏: " + Py.Str(pyDict.Get("id")));
		}
		List<DownloadTask> list = new List<DownloadTask>();
		foreach (KeyValuePair<string, object> item in pyDict3)
		{
			PyDict pyDict4 = Py.DictOr(item.Value);
			object o = pyDict4.Get("hash");
			if (Py.Truthy(o))
			{
				string text4 = Py.Str(o);
				string text5 = PPath.Join(text, "objects", text4.Substring(0, Math.Min(2, text4.Length)), text4);
				if (!Utils.FileMatches(text5, text4, pyDict4.Get("size")))
				{
					list.Add(new DownloadTask("https://resources.download.minecraft.net/" + text4.Substring(0, Math.Min(2, text4.Length)) + "/" + text4, text5, text4, (pyDict4.Get("size") == null) ? ((long?)null) : new long?(Py.Int(pyDict4["size"]))));
				}
			}
		}
		if (list.Count > 0)
		{
			dm.DownloadAll(list, "下载资源文件 " + text2);
		}
		if (Py.Truthy(pyDict2.Get("virtual")))
		{
			_copy_objects(pyDict3, text, PPath.Join(text, "virtual", text2));
		}
		if (Py.Truthy(pyDict2.Get("map_to_resources")))
		{
			_copy_objects(pyDict3, text, PPath.Join(instance.path, "resources"));
		}
	}

	private static void _copy_objects(PyDict objects, string assets_dir, string b)
	{
		b = Utils.EnsureDir(b);
		int num = 0;
		foreach (KeyValuePair<string, object> @object in objects)
		{
			object o = Py.DictOr(@object.Value).Get("hash");
			if (!Py.Truthy(o))
			{
				continue;
			}
			string text = Py.Str(o);
			string text2 = PPath.Join(assets_dir, "objects", text.Substring(0, Math.Min(2, text.Length)), text);
			if (new PPath(text2).IsFile())
			{
				string text3 = PPath.Join(b, @object.Key);
				if (!new PPath(text3).IsFile() || !(Utils.Sha1File(text3) == text))
				{
					Directory.CreateDirectory(Path.GetDirectoryName(Utils.AbsPath(text3)));
					CopyFile2(text2, text3);
					num++;
				}
			}
		}
		if (num > 0)
		{
			Utils.log.Info("复制资源 %s 个 -> %s", num, b);
		}
	}

	private void _install_logging(PyDict resolved)
	{
		PyDict pyDict = Py.DictOr(Py.Or(resolved.Get("logging"), new PyDict())).Get("client") as PyDict;
		if (!Py.Truthy(pyDict))
		{
			return;
		}
		PyDict pyDict2 = Py.DictOr(Py.Or(pyDict.Get("file"), new PyDict()));
		if (!Py.Truthy(pyDict2.Get("url")))
		{
			return;
		}
		string dest = PPath.Join(instance.AssetsDir(), "log_configs", Py.Str(Py.Or(pyDict2.Get("id"), "client")));
		try
		{
			dm.Download(pyDict2["url"], dest, pyDict2.Get("sha1") as string, (pyDict2.Get("size") == null) ? ((long?)null) : new long?(Py.Int(pyDict2["size"])));
		}
		catch (Exception e)
		{
			Utils.log.Warning("下载日志配置失败: %s", Py.ExcStr(e));
		}
	}

	private static PyDict LoaderOf(object d)
	{
		return (PyDict)((PyDict)d).Req("loader");
	}

	public string InstallFabric(string mc_version, string loader_version = null, bool force = false)
	{
		return InstallKnot("Fabric", "https://meta.fabricmc.net/v2", mc_version, loader_version, force, preferStable: true);
	}

	public string InstallQuilt(string mc_version, string loader_version = null, bool force = false)
	{
		return InstallKnot("Quilt", "https://meta.quiltmc.org/v3", mc_version, loader_version, force, preferStable: false);
	}

	private string InstallKnot(string label, string meta, string mc_version, string loader_version, bool force, bool preferStable)
	{
		string url = meta + "/versions/loader/" + mc_version;
		object obj = dm.FetchJson(url);
		if (!Py.Truthy(obj))
		{
			throw new InstallError(label + " 不支持 Minecraft " + mc_version);
		}
		List<object> list = (List<object>)obj;
		object obj2;
		if (string.IsNullOrEmpty(loader_version))
		{
			obj2 = ((!preferStable) ? list[0] : (list.FirstOrDefault((object d) => Py.Truthy(LoaderOf(d).Get("stable"))) ?? list[0]));
		}
		else
		{
			obj2 = list.FirstOrDefault((object d) => Py.Eq(LoaderOf(d).Req("version"), loader_version));
			if (obj2 == null)
			{
				throw new InstallError("找不到 " + label + " Loader " + loader_version + " (MC " + mc_version + ")");
			}
		}
		string text = Py.Str(LoaderOf(obj2).Req("version"));
		on_progress?.Invoke("下载 " + label + " Loader " + text + " 版本 JSON", 0L, 1L);
		PyDict pyDict = (PyDict)dm.FetchJson(meta + "/versions/loader/" + mc_version + "/" + text + "/profile/json");
		string text2 = Py.Str(pyDict.Req("id"));
		_install_json(text2, pyDict, force);
		return text2;
	}

	public string InstallForge(string mc_version, string forge_version = null, bool force = false)
	{
		mc_version = Py.Strip(mc_version ?? "");
		forge_version = Py.Strip(forge_version ?? "");
		if (forge_version.Length == 0)
		{
			forge_version = null;
		}
		if (mc_version.Length == 0)
		{
			throw new InstallError("安装 Forge 时缺少 Minecraft 版本");
		}
		string text = PPath.Join(Utils.ROOT, "cache");
		Utils.EnsureDir(text);
		List<string> list = ((forge_version != null) ? ForgeArtifactGuesses(mc_version, forge_version) : new List<string>());
		string text2 = null;
		string text3 = null;
		Exception ex = null;
		string text4 = _find_installed_forge(mc_version, forge_version);
		if (text4 != null && !force)
		{
			_note("实例已安装 " + text4 + "，跳过 Forge 安装", 0L, 1L);
			return text4;
		}
		if (list.Count > 0)
		{
			_note("按整合包声明直装 Forge " + forge_version + " (Minecraft " + mc_version + ")，不查残缺版本列表", 0L, 1L);
			_note("尝试构件: " + string.Join(", ", list), 0L, 1L);
		}
		foreach (string item in list)
		{
			string text5 = PPath.Join(text, "forge-" + item + "-installer.jar");
			if (!force && IsForgeInstallerJar(text5))
			{
				_note("使用缓存的 Forge 安装器 " + item + " (" + new PPath(text5).Name + ")", 0L, 1L);
				text2 = item;
				text3 = text5;
			}
			else
			{
				try
				{
					_note("下载 Forge 安装器 " + item, 0L, 1L);
					_download_forge_installer(item, mc_version, text5, force);
					text2 = item;
					text3 = text5;
				}
				catch (Exception ex2) when (ex2 is DownloadError || ex2 is InstallError)
				{
					ex = ex2;
					_note("Forge 安装器 " + item + " 不可用: " + Py.ExcStr(ex2), 0L, 1L);
					continue;
				}
			}
			break;
		}
		List<string> pool;
		if (text3 == null)
		{
			List<string> list2 = _list_forge_artifacts(mc_version);
			List<string> list3 = list2.Where((string v) => !Py.Lower(v).Contains("-pre")).ToList();
			pool = ((list3.Count > 0) ? list3 : list2);
			if (pool.Count == 0)
			{
				if (ex != null)
				{
					throw new InstallError("无法安装 Forge " + forge_version + " (Minecraft " + mc_version + "): " + Py.ExcStr(ex));
				}
				if (list.Count > 0)
				{
					throw new InstallError("无法下载 Forge " + forge_version + " 安装器（Minecraft " + mc_version + "，已尝试 " + string.Join(", ", list) + "）");
				}
				throw new InstallError("Forge 没有支持 Minecraft " + mc_version + " 的版本");
			}
			if (forge_version != null)
			{
				text2 = MatchForgeArtifact(pool, mc_version, forge_version);
				if (text2 == null)
				{
					string text6 = latestOf();
					_note("列表中没有 Forge " + forge_version + "，改用最新版 " + text6, 0L, 1L);
					text2 = text6;
				}
				else
				{
					_note("版本列表命中 Forge " + text2, 0L, 1L);
				}
			}
			else
			{
				text2 = latestOf();
				_note("使用 Minecraft " + mc_version + " 最新 Forge " + text2, 0L, 1L);
			}
			text3 = PPath.Join(text, "forge-" + text2 + "-installer.jar");
			if (force || !IsForgeInstallerJar(text3))
			{
				_note("下载 Forge 安装器 " + text2, 0L, 1L);
				_download_forge_installer(text2, mc_version, text3, force);
			}
			else
			{
				_note("使用缓存的 Forge 安装器 " + text2, 0L, 1L);
			}
		}
		return _run_forge_installer(text3, mc_version, text2, force);
		string latestOf()
		{
			string text7 = pool[0];
			foreach (string item2 in pool)
			{
				if (Py.Cmp(ForgeSortKey(item2, mc_version), ForgeSortKey(text7, mc_version)) > 0)
				{
					text7 = item2;
				}
			}
			return text7;
		}
	}

	private string _find_installed_forge(string mc_version, string forge_version)
	{
		string text = instance.VersionsDir();
		if (!new PPath(text).IsDir())
		{
			return null;
		}
		string text2 = Py.Strip(forge_version ?? "");
		if (Py.Lower(text2).StartsWith("forge-"))
		{
			text2 = text2.Substring(6);
		}
		List<string> list = new List<string>();
		if (text2.Length > 0)
		{
			if (text2.StartsWith(mc_version + "-"))
			{
				list.Add(mc_version + "-forge-" + text2.Substring(mc_version.Length + 1));
			}
			list.Add(mc_version + "-forge-" + text2);
		}
		foreach (string item in list)
		{
			if (!string.IsNullOrEmpty(item) && instance.HasVersion(item))
			{
				return item;
			}
		}
		if (text2.Length == 0)
		{
			return null;
		}
		string value = (text2.StartsWith(mc_version + "-") ? text2.Substring(mc_version.Length + 1) : text2);
		foreach (string item2 in Directory.EnumerateFileSystemEntries(Utils.AbsPath(text)))
		{
			string fileName = Path.GetFileName(item2);
			if (Directory.Exists(item2) && Py.Lower(fileName).Contains("forge") && fileName.Contains(mc_version) && fileName.Contains(value) && instance.HasVersion(fileName))
			{
				return fileName;
			}
		}
		return null;
	}

	private void _download_forge_installer(string full, string mc_version, string dest, bool force = false)
	{
		if (new PPath(dest).IsFile() && !IsForgeInstallerJar(dest))
		{
			try
			{
				File.Delete(Utils.AbsPath(dest));
			}
			catch (FileNotFoundException)
			{
			}
			force = true;
		}
		List<string> list = _forge_installer_urls(full, mc_version);
		DownloadManager downloadManager = dm;
		string url = list[0];
		bool force2 = force;
		object urls = list;
		downloadManager.Download(url, dest, null, null, force2, null, null, null, urls);
		if (!IsForgeInstallerJar(dest))
		{
			try
			{
				File.Delete(Utils.AbsPath(dest));
			}
			catch (Exception)
			{
			}
			throw new InstallError("下载的 Forge 安装器无效: " + full);
		}
	}

	private List<string> _forge_installer_urls(string full, string mc_version)
	{
		List<string> list = FORGE_MAVEN_MIRRORS.Select((string b) => b + "/" + full + "/forge-" + full + "-installer.jar").ToList();
		List<string> list2 = SplitForgeArtifact(full, mc_version);
		string text = list2[0];
		string text2 = list2[1];
		string text3 = list2[2];
		if (!string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(text2))
		{
			string text4 = "mcversion=" + text + "&version=" + text2 + "&category=installer&format=jar";
			if (!string.IsNullOrEmpty(text3))
			{
				text4 = text4 + "&branch=" + text3;
			}
			list.Add("https://bmclapi2.bangbang93.com/forge/download?" + text4);
		}
		return list;
	}

	private List<string> _list_forge_artifacts(string mc_version)
	{
		List<string> found = new List<string>();
		HashSet<string> seen = new HashSet<string>();
		_note("查询 Forge 版本列表 (Minecraft " + mc_version + ")", 0L, 1L);
		try
		{
			object data = dm.FetchJson("https://bmclapi2.bangbang93.com/forge/minecraft/" + mc_version, 60L);
			addMany(BmclForgeArtifacts(data, mc_version), "BMCLAPI");
		}
		catch (Exception e)
		{
			_note("BMCLAPI Forge 列表失败: " + Py.ExcStr(e), 0L, 1L);
		}
		if (found.Count > 0)
		{
			found = sortFound();
			_note("可用 Forge 共 " + found.Count + " 个，最新 " + found[found.Count - 1], 0L, 1L);
			return found;
		}
		(string, bool, string)[] array = new(string, bool, string)[2]
		{
			("https://bmclapi2.bangbang93.com/maven/net/minecraftforge/forge/maven-metadata.xml", true, "BMCLAPI maven-metadata"),
			("https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml", false, "Forge 官方 maven-metadata")
		};
		for (int i = 0; i < array.Length; i++)
		{
			var (url, expand, text) = array[i];
			string xml;
			try
			{
				xml = dm.FetchText(url, 90L, expand);
			}
			catch (Exception e2)
			{
				_note(text + " 失败: " + Py.ExcStr(e2), 0L, 1L);
				continue;
			}
			List<string> list = ParseMavenVersions(xml);
			_note(text + ": 解析到 " + list.Count + " 条版本", 0L, 1L);
			if (addMany(list.Cast<object>(), text) > 0)
			{
				break;
			}
		}
		found = sortFound();
		if (found.Count > 0)
		{
			_note("可用 Forge 共 " + found.Count + " 个，最新 " + found[found.Count - 1], 0L, 1L);
		}
		return found;
		int addMany(IEnumerable<object> items, string source)
		{
			int num = 0;
			foreach (object item in items ?? new List<object>())
			{
				string text2 = Py.Strip(Py.S(item));
				if (text2.Length != 0 && !seen.Contains(text2) && (!(text2 != mc_version) || text2.StartsWith(mc_version + "-")))
				{
					seen.Add(text2);
					found.Add(text2);
					num++;
				}
			}
			if (num > 0)
			{
				_note(source + ": 匹配到 " + num + " 个 Forge 版本", 0L, 1L);
			}
			return num;
		}
		List<string> sortFound()
		{
			return Py.Sorted(found, (string v) => ForgeSortKey(v, mc_version));
		}
	}

	/// <summary>Forge / NeoForge 安装处理器需要的最低 Java：26.x 起 25，1.20.5 起 21，其余 17。</summary>
	public static long InstallerJavaMajor(string mc_version)
	{
		long[] t = _mc_tuple(mc_version);
		if (t[0] >= 26)
		{
			return 25L;
		}
		if (!TupleLess(t, 1L, 20L, 5L))
		{
			return 21L;
		}
		return 17L;
	}

	/// <summary>NeoForge 版本前缀：26.x 起是完整三段（26.3 → 26.3.0），1.20.2+ 是去掉开头的 1。</summary>
	public static string NeoforgeCalverPrefix(string mc_version)
	{
		long[] t = _mc_tuple(mc_version);
		if (t[0] < 26)
		{
			return null;
		}
		return t[0] + "." + t[1] + "." + t[2];
	}

	private static bool TupleLess(long[] a, params long[] b)
	{
		for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
		{
			if (a[i] != b[i])
			{
				return a[i] < b[i];
			}
		}
		return a.Length < b.Length;
	}

	private string _run_forge_installer(string installer_jar, string mc_version, string full, bool force = false)
	{
		PyDict pyDict = _read_forge_install_profile(installer_jar);
		if (Py.Truthy(pyDict) && Py.Truthy(pyDict.Get("versionInfo")) && !Py.Truthy(pyDict.Get("processors")))
		{
			_note("旧版 Forge 安装流程 " + full, 0L, 1L);
			return _install_forge_legacy(installer_jar, pyDict, mc_version, force);
		}
		if (Py.Truthy(pyDict) && (Py.Truthy(pyDict.Get("processors")) || Py.Truthy(pyDict.Get("json"))))
		{
			_note("离线安装 Forge " + full + "（不调用官方安装器）", 0L, 1L);
			return _install_forge_modern(installer_jar, pyDict, mc_version, force);
		}
		string java_exe = Java.JavaForInstaller(TupleLess(_mc_tuple(mc_version), 1L, 17L) ? "forge-legacy" : "forge", dm, on_progress, InstallerJavaMajor(mc_version));
		_note("回退官方 Forge 安装器 " + full, 0L, 1L);
		return _install_forge_cli(installer_jar, mc_version, full, java_exe);
	}

	private PyDict _read_forge_install_profile(string installer_jar)
	{
		try
		{
			return PyJson.Loads(Utils.DecodeUtf8Strict(ZipRead(installer_jar, "install_profile.json"))) as PyDict;
		}
		catch (Exception e)
		{
			Utils.log.Warning("读取 Forge install_profile.json 失败: %s", Py.ExcStr(e));
			return null;
		}
	}

	private string _install_forge_modern(string installer_jar, PyDict profile, string mc_version, bool force = false)
	{
		Instance instance = this.instance;
		string name = Py.LStrip(Py.Str(Py.Or(profile.Get("json"), "/version.json")), "/");
		PyDict pyDict = (PyDict)PyJson.Loads(Utils.DecodeUtf8Strict(ZipRead(installer_jar, name)));
		object o = Py.Or(pyDict.Get("id"), profile.Get("version"));
		if (!Py.Truthy(o))
		{
			throw new InstallError("Forge 安装器缺少 version id");
		}
		string text = Py.Str(o);
		string text2 = Py.Str(Py.Or(profile.Get("minecraft"), pyDict.Get("inheritsFrom"), mc_version));
		_note("Forge 版本 id=" + text + "，依赖原版 " + text2, 0L, 1L);
		if (!instance.HasVersion(text2))
		{
			_note("安装 Minecraft " + text2, 0L, 1L);
			InstallVersion(text2, force);
		}
		string text3 = PPath.Join(instance.VersionsDir(), text2, text2 + ".jar");
		if (!new PPath(text3).IsFile())
		{
			throw new InstallError("缺少原版客户端 jar: " + text3);
		}
		List<object> list = new List<object>();
		list.AddRange(Py.ListOr(Py.Or(profile.Get("libraries"), new List<object>())));
		list.AddRange(Py.ListOr(Py.Or(pyDict.Get("libraries"), new List<object>())));
		_note("下载 Forge 依赖库（" + list.Count + " 个条目）", 0L, 1L);
		_install_libraries(PyDict.Of("libraries", list, "id", text), text, force);
		string text4 = MkdTemp("tipcl_fgdata_");
		try
		{
			PyDict data = _forge_processor_data(installer_jar, profile, text3, text4);
			_prefetch_mojmaps(data, text2);
			string java_exe = Java.JavaForInstaller("forge", dm, on_progress, InstallerJavaMajor(mc_version));
			List<object> list2 = (from p in Py.ListOr(Py.Or(profile.Get("processors"), new List<object>()))
				where StrList(Py.Or(Py.DictOr(p).Get("sides"), Py.L("client"))).Contains("client")
				select p).ToList();
			_note("开始运行 " + list2.Count + " 个 Forge 客户端处理器", 0L, 1L);
			_run_install_profile_processors(profile, data, java_exe, force);
		}
		finally
		{
			try
			{
				Directory.Delete(text4, recursive: true);
			}
			catch (Exception)
			{
			}
		}
		string text5 = PPath.Join(instance.VersionsDir(), text);
		Utils.EnsureDir(text5);
		Utils.WriteJson(PPath.Join(text5, text + ".json"), pyDict);
		instance.SetMeta("mc_version", text);
		_note("Forge/NeoForge " + text + " 安装完成", 0L, 1L);
		return text;
	}

	private PyDict _forge_processor_data(string installer_jar, PyDict profile, string vanilla_jar, string tmp_data)
	{
		string text = instance.LibrariesDir();
		PyDict pyDict = PyDict.Of("SIDE", "client", "MINECRAFT_JAR", new PPath(vanilla_jar).Resolve().ToString(), "ROOT", new PPath(instance.path).Resolve().ToString(), "INSTALLER", new PPath(installer_jar).Resolve().ToString(), "LIBRARY_DIR", new PPath(text).Resolve().ToString());
		foreach (KeyValuePair<string, object> item in Py.DictOr(Py.Or(profile.Get("data"), new PyDict())))
		{
			object raw = ((item.Value is PyDict pyDict2) ? pyDict2.Get("client") : item.Value);
			pyDict[item.Key] = _resolve_forge_data_value(raw, text, installer_jar, tmp_data);
		}
		return pyDict;
	}

	private string _resolve_forge_data_value(object raw, string libs_dir, string installer_jar, string tmp_data)
	{
		if (raw == null)
		{
			return "";
		}
		string text = Py.Str(raw);
		if (text.Length >= 2 && text[0] == '\'' && text[text.Length - 1] == '\'')
		{
			return text.Substring(1, text.Length - 2);
		}
		if (text.StartsWith("[") && text.EndsWith("]"))
		{
			string text2 = PPath.Join(libs_dir, _maven_relpath(text));
			Directory.CreateDirectory(Path.GetDirectoryName(Utils.AbsPath(text2)));
			return new PPath(text2).Resolve().ToString();
		}
		if (text.StartsWith("/"))
		{
			string text3 = Py.LStrip(text, "/");
			string text4 = Path.Combine(tmp_data, new PPath(text3).Name);
			File.WriteAllBytes(text4, ZipRead(installer_jar, text3));
			return new PPath(text4).Resolve().ToString();
		}
		return text;
	}

	private void _prefetch_mojmaps(PyDict data, string vanilla)
	{
		object o = data.Get("MOJMAPS");
		if (!Py.Truthy(o))
		{
			return;
		}
		PyDict pyDict = instance.VersionJson(vanilla) as PyDict;
		if (!Py.Truthy(pyDict))
		{
			try
			{
				pyDict = Manifest.GetVersionJson(dm, vanilla);
			}
			catch (Exception)
			{
				return;
			}
		}
		PyDict pyDict2 = Py.DictOr(Py.Or(Py.DictOr(Py.Or(pyDict.Get("downloads"), new PyDict())).Get("client_mappings"), new PyDict()));
		if (Py.Truthy(pyDict2.Get("url")))
		{
			string text = Py.Str(o);
			Directory.CreateDirectory(Path.GetDirectoryName(Utils.AbsPath(text)));
			on_progress?.Invoke("下载 Mojang mappings", 0L, 1L);
			dm.Download(pyDict2["url"], text, pyDict2.Get("sha1") as string, (pyDict2.Get("size") == null) ? ((long?)null) : new long?(Py.Int(pyDict2["size"])));
		}
	}

	private string _sub_forge_arg(object raw, PyDict data)
	{
		string text = _FORGE_ARG_RE.Replace(Py.Str(raw), (Match m) => data.ContainsKey(m.Groups[1].Value) ? Py.Str(data[m.Groups[1].Value]) : m.Value);
		if (text.StartsWith("[") && text.EndsWith("]"))
		{
			string text2 = PPath.Join(instance.LibrariesDir(), _maven_relpath(text));
			Directory.CreateDirectory(Path.GetDirectoryName(Utils.AbsPath(text2)));
			return new PPath(text2).Resolve().ToString();
		}
		return text;
	}

	private string _library_file(string maven_name)
	{
		string text = PPath.Join(instance.LibrariesDir(), _maven_relpath(maven_name));
		if (!new PPath(text).IsFile())
		{
			throw new InstallError("处理器依赖库未下载: " + text);
		}
		return text;
	}

	private void _run_install_profile_processors(PyDict profile, PyDict data, string java_exe, bool force = false)
	{
		List<PyDict> list = (from p in Py.ListOr(Py.Or(profile.Get("processors"), new List<object>())).Select(Py.DictOr)
			where StrList(Py.Or(p.Get("sides"), Py.L("client"))).Contains("client")
			select p).ToList();
		int total = list.Count;
		for (int num = 0; num < total; num++)
		{
			PyDict pyDict = list[num];
			string text = null;
			List<object> list2 = Py.ListOr(Py.Or(pyDict.Get("args"), new List<object>()));
			int num2 = list2.FindIndex((object a) => Py.Eq(a, "--task"));
			if (num2 >= 0)
			{
				text = ((num2 + 1 < list2.Count) ? (list2[num2 + 1] as string) : null);
			}
			if (text == "DOWNLOAD_MOJMAPS" && new PPath(Py.Str(Py.Or(data.Get("MOJMAPS"), ""))).IsFile())
			{
				_note("跳过处理器 " + (num + 1) + "/" + total + "（mappings 已就绪）", 0L, 1L);
				continue;
			}
			PyDict pyDict2 = new PyDict();
			foreach (KeyValuePair<string, object> item in Py.DictOr(Py.Or(pyDict.Get("outputs"), new PyDict())))
			{
				pyDict2[_sub_forge_arg(item.Key, data)] = _sub_forge_arg(item.Value, data);
			}
			if (pyDict2.Count > 0 && !force && pyDict2.All((KeyValuePair<string, object> kv) => Utils.FileMatches(kv.Key, Py.Or(kv.Value, null))))
			{
				_note("跳过处理器 " + (num + 1) + "/" + total + "（输出已存在）", 0L, 1L);
				continue;
			}
			string text2 = _library_file(Py.Str(pyDict.Req("jar")));
			List<string> list3 = new List<string> { text2 };
			foreach (string item2 in StrList(pyDict.Get("classpath")))
			{
				list3.Add(_library_file(item2));
			}
			string text3 = _jar_main_class(text2);
			if (string.IsNullOrEmpty(text3))
			{
				throw new InstallError("处理器 jar 没有 Main-Class: " + Py.Str(pyDict["jar"]));
			}
			List<string> list4 = list2.Select((object a) => _sub_forge_arg(a, data)).ToList();
			List<object> list5 = list4.Where((string a) => _FORGE_ARG_RE.IsMatch(a)).Cast<object>().ToList();
			if (list5.Count > 0)
			{
				throw new InstallError("处理器参数含未替换占位符: " + Py.Repr(list5));
			}
			_note("运行 Forge 处理器 " + (num + 1) + "/" + total + ": " + text3, num, total);
			List<string> lines = new List<string>();
			int ii = num;
			List<string> list6 = new List<string>();
			list6.Add(java_exe);
			list6.Add("-cp");
			list6.Add(string.Join(Path.PathSeparator.ToString(), list3));
			list6.Add(text3);
			list6.AddRange(list4);
			int num3 = Utils.RunProcess(list6, instance.path, delegate(string line)
			{
				lines.Add(line);
				_proc_line(line, ii + 1, total);
			}, 1800.0);
			if (num3 != 0)
			{
				string text4 = string.Join(" | ", lines.Skip(Math.Max(0, lines.Count - 6)).Select(AsciiReplacePy));
				if (pyDict2.Count <= 0 || !pyDict2.All((KeyValuePair<string, object> kv) => new PPath(kv.Key).IsFile()))
				{
					throw new InstallError("Forge 处理器失败 (退出码 " + num3 + "): " + Py.Str(pyDict["jar"]) + ((text4.Length > 0) ? (" · " + text4) : ""));
				}
				Utils.log.Warning("处理器退出码 %s，但输出已生成，继续: %s", num3, pyDict["jar"]);
			}
			else
			{
				List<object> list7 = pyDict2.Where((KeyValuePair<string, object> kv) => !Utils.FileMatches(kv.Key, Py.Or(kv.Value, null))).Select<KeyValuePair<string, object>, object>((Func<KeyValuePair<string, object>, object>)((KeyValuePair<string, object> kv) => kv.Key)).ToList();
				if (list7.Count > 0)
				{
					throw new InstallError("处理器未生成预期输出: " + Py.Repr(list7));
				}
			}
		}
	}

	private string _install_forge_cli(string installer_jar, string mc_version, string full, string java_exe, string label = "Forge")
	{
		string text = MkdTemp("tipclfg");
		try
		{
			string text2 = PPath.Join(instance.VersionsDir(), mc_version);
			string dst = Path.Combine(text, "versions", mc_version);
			if (new PPath(text2).IsDir())
			{
				CopyTree(text2, dst);
			}
			on_progress?.Invoke("运行 " + label + " 安装器 " + full, 0L, 1L);
			List<string> lines = new List<string>();
			int num = Utils.RunProcess(new List<string> { java_exe, "-jar", installer_jar, "--installClient", text }, text, delegate(string line)
			{
				lines.Add(line);
				_proc_line(line, 0L, 1L, label);
			}, 1800.0);
			if (num != 0)
			{
				string text3 = string.Join(" | ", lines.Skip(Math.Max(0, lines.Count - 8)).Select(AsciiReplacePy));
				throw new InstallError(label + " 安装器退出码 " + num + ((text3.Length > 0) ? (": " + text3) : ""));
			}
			string path = Path.Combine(text, "versions");
			if (Directory.Exists(path))
			{
				string[] directories = Directory.GetDirectories(path);
				foreach (string text4 in directories)
				{
					CopyTree(text4, PPath.Join(instance.VersionsDir(), Path.GetFileName(text4)));
				}
			}
			string text5 = Path.Combine(text, "libraries");
			if (Directory.Exists(text5))
			{
				string text6 = instance.LibrariesDir();
				string[] directories = Directory.GetFiles(text5, "*", SearchOption.AllDirectories);
				foreach (string text7 in directories)
				{
					string text8 = text7.Substring(text5.Length).TrimStart(Path.DirectorySeparatorChar);
					string text9 = PPath.Join(text6, text8);
					Directory.CreateDirectory(Path.GetDirectoryName(Utils.AbsPath(text9)));
					if (!new PPath(text9).IsFile())
					{
						CopyFile2(text7, text9);
					}
				}
			}
			_merge_libraries_to_shared();
			List<string> list = (from i in instance.InstalledIds().Select(Py.Str)
				where Py.Lower(i).Contains("forge") || Py.Lower(i).Contains("neoforge")
				select i).ToList();
			string text10 = ((list.Count > 0) ? list[list.Count - 1] : (mc_version + "-forge-" + full));
			instance.SetMeta("mc_version", text10);
			return text10;
		}
		finally
		{
			try
			{
				Directory.Delete(text, recursive: true);
			}
			catch (Exception)
			{
			}
		}
	}

	private string _install_forge_legacy(string installer_jar, PyDict profile, string mc_version, bool force = false)
	{
		PyDict pyDict = Py.DictOr(Py.Or(profile.Get("install"), new PyDict()));
		PyDict pyDict2 = Py.DictOr(Py.Or(profile.Get("versionInfo"), new PyDict()));
		object o = Py.Or(pyDict2.Get("id"), pyDict.Get("target"));
		if (!Py.Truthy(o))
		{
			throw new InstallError("旧版 Forge 安装器缺少版本 id");
		}
		string text = Py.Str(o);
		string text2 = Py.Str(Py.Or(pyDict.Get("minecraft"), mc_version));
		if (!instance.HasVersion(text2))
		{
			on_progress?.Invoke("安装 Minecraft " + text2, 0L, 1L);
			InstallVersion(text2, force);
		}
		string text3 = PPath.Join(instance.VersionsDir(), text);
		Utils.EnsureDir(text3);
		Utils.WriteJson(PPath.Join(text3, text + ".json"), pyDict2);
		string text4 = PPath.Join(instance.VersionsDir(), text2, text2 + ".jar");
		string text5 = PPath.Join(text3, text + ".jar");
		if (!new PPath(text4).IsFile())
		{
			throw new InstallError("缺少原版客户端 jar: " + text4);
		}
		if (force || !new PPath(text5).IsFile())
		{
			CopyFile2(text4, text5);
		}
		object o2 = pyDict.Get("path");
		object o3 = pyDict.Get("filePath");
		if (Py.Truthy(o2) && Py.Truthy(o3))
		{
			string path = PPath.Join(instance.LibrariesDir(), Utils.MavenArtifactPath(Py.Str(o2)));
			Directory.CreateDirectory(Path.GetDirectoryName(Utils.AbsPath(path)));
			byte[] bytes;
			try
			{
				bytes = ZipRead(installer_jar, Py.Str(o3));
			}
			catch (KeyError)
			{
				throw new InstallError("Forge 安装器中找不到 " + Py.Str(o3));
			}
			File.WriteAllBytes(Utils.AbsPath(path), bytes);
		}
		on_progress?.Invoke("安装 Forge 依赖库 " + text, 0L, 1L);
		PyDict resolved = Manifest.ResolveInherits(pyDict2, _load_parent_json);
		_install_libraries(resolved, text, force);
		instance.SetMeta("mc_version", text);
		Utils.log.Info("旧版 Forge %s 安装完成", text);
		return text;
	}

	public string InstallNeoforge(string mc_version, string version = null, bool force = false)
	{
		List<string> list = ParseMavenVersions(dm.FetchText("https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml"));
		string text = null;
		if (!string.IsNullOrEmpty(version))
		{
			if (list.Contains(version))
			{
				text = version;
			}
			else if (list.Contains(mc_version + "-" + version))
			{
				text = mc_version + "-" + version;
			}
			else
			{
				Utils.log.Warning("NeoForge %s 不存在 (MC %s)，改用该版本最新 NeoForge", version, mc_version);
				on_progress?.Invoke("NeoForge " + version + " 不可用，改用最新版", 0L, 1L);
				version = null;
			}
		}
		if (string.IsNullOrEmpty(version))
		{
			List<string> list2;
			if (!TupleLess(_mc_tuple(mc_version), 1L, 20L, 2L))
			{
				string prefix = NeoforgeCalverPrefix(mc_version) ?? string.Join(".", mc_version.Split('.').Skip(1));
				list2 = list.Where((string v) => v.StartsWith(prefix + ".")).ToList();
			}
			else
			{
				list2 = list.Where((string v) => v.StartsWith(mc_version + "-")).ToList();
				if (list2.Count == 0)
				{
					string prefix2 = NEOFORGE_MC_MAP.Get(mc_version) as string;
					list2 = list.Where((string v) => !string.IsNullOrEmpty(prefix2) && v.StartsWith(prefix2 + ".")).ToList();
				}
			}
			if (list2.Count == 0)
			{
				throw new InstallError("NeoForge 没有支持 Minecraft " + mc_version + " 的版本");
			}
			text = list2[list2.Count - 1];
		}
		string url = "https://maven.neoforged.net/releases/net/neoforged/neoforge/" + text + "/neoforge-" + text + "-installer.jar";
		string text2 = PPath.Join(Utils.ROOT, "cache", "neoforge-" + text + "-installer.jar");
		on_progress?.Invoke("下载 NeoForge 安装器 " + text, 0L, 1L);
		DownloadManager downloadManager = dm;
		bool force2 = force;
		downloadManager.Download(url, text2, null, null, force2);
		PyDict pyDict = _read_forge_install_profile(text2);
		if (Py.Truthy(pyDict) && (Py.Truthy(pyDict.Get("processors")) || Py.Truthy(pyDict.Get("json"))))
		{
			on_progress?.Invoke("离线安装 NeoForge " + text, 0L, 1L);
			return _install_forge_modern(text2, pyDict, mc_version, force);
		}
		string java_exe = Java.JavaForInstaller("neoforge", dm, on_progress, InstallerJavaMajor(mc_version));
		return _install_forge_cli(text2, mc_version, text, java_exe, "NeoForge");
	}

	private void _merge_libraries_to_shared()
	{
		if (!Py.Truthy(Config.CONFIG.Get("shared_libraries")))
		{
			return;
		}
		string text = PPath.Join(instance.path, "libraries");
		string text2 = instance.LibrariesDir();
		if (!new PPath(text).IsDir() || new PPath(text).Resolve().Equals(new PPath(text2).Resolve()))
		{
			return;
		}
		int num = 0;
		string text3 = Utils.AbsPath(text);
		string[] files = Directory.GetFiles(text3, "*", SearchOption.AllDirectories);
		foreach (string text4 in files)
		{
			string text5 = PPath.Join(text2, text4.Substring(text3.Length).TrimStart(Path.DirectorySeparatorChar));
			if (!new PPath(text5).IsFile())
			{
				Directory.CreateDirectory(Path.GetDirectoryName(Utils.AbsPath(text5)));
				CopyFile2(text4, text5);
				num++;
			}
		}
		if (num > 0)
		{
			Utils.log.Info("已把 %s 个库文件合并到共享目录 %s", num, text2);
		}
	}

	public string InstallOptifine(string mc_version, string typ = "", string patch = "", bool force = false)
	{
		return Optifine.Install(this, mc_version, typ, patch, force);
	}

	public string InstallLiteloader(string mc_version, bool force = false)
	{
		return Liteloader.Install(this, mc_version, force);
	}

	public void UninstallVersion(object version_id)
	{
		string text = Py.SS(version_id);
		if (text.Length == 0 || text == "." || text == ".." || text.IndexOfAny(new char[2] { '/', '\\' }) >= 0 || text.Contains(":"))
		{
			throw new InstallError(Py.Fmt("非法的版本 ID: {0!r}", version_id));
		}
		string text2 = instance.VersionsDir();
		string text3 = PPath.Join(text2, text);
		PPath pPath = new PPath(text3).Resolve();
		PPath pPath2 = new PPath(text2).Resolve();
		if (!pPath.Equals(pPath2) && !pPath.IsRelativeTo(pPath2.ToString()))
		{
			throw new InstallError(Py.Fmt("非法的版本 ID: {0!r}", version_id));
		}
		if (!LocalFs.IsDir(text3))
		{
			throw new InstallError(Py.Fmt("版本 {0} 未安装", version_id));
		}
		Utils.RemoveTree(text3);
		Utils.log.Info("已卸载版本 %s", version_id);
	}

	public static List<string> SplitForgeArtifact(string full, string mc_version = null)
	{
		full = Py.Strip(full ?? "");
		string text = Py.Strip(mc_version ?? "");
		string text2 = full;
		if (text.Length > 0 && full.StartsWith(text + "-", StringComparison.Ordinal))
		{
			text2 = full.Substring(text.Length + 1);
		}
		else
		{
			List<string> list = Py.Split(full, "-", 1);
			if (list.Count == 2 && Regex.IsMatch(list[0], "^\\d+\\.\\d+"))
			{
				text = list[0];
				text2 = list[1];
			}
		}
		string item = null;
		string item2 = text2;
		if (text.Length > 0 && text2.EndsWith("-" + text, StringComparison.Ordinal) && text2.Length > text.Length + 1)
		{
			item2 = text2.Substring(0, text2.Length - (text.Length + 1));
			item = text;
		}
		return new List<string> { text, item2, item };
	}

	public static List<string> ParseMavenVersions(string xml)
	{
		List<string> list = new List<string>();
		if (string.IsNullOrEmpty(xml))
		{
			return list;
		}
		XmlDocument xmlDocument = new XmlDocument
		{
			XmlResolver = null
		};
		try
		{
			xmlDocument.LoadXml(xml);
		}
		catch (XmlException)
		{
			return list;
		}
		foreach (XmlNode item in Iter(xmlDocument.DocumentElement))
		{
			if (item.LocalName != "version")
			{
				continue;
			}
			string text = "";
			foreach (XmlNode childNode in item.ChildNodes)
			{
				if (childNode.NodeType == XmlNodeType.Element)
				{
					break;
				}
				if (childNode.NodeType == XmlNodeType.Text || childNode.NodeType == XmlNodeType.CDATA || childNode.NodeType == XmlNodeType.Whitespace || childNode.NodeType == XmlNodeType.SignificantWhitespace)
				{
					text += childNode.Value;
				}
			}
			if (Py.Strip(text).Length > 0)
			{
				list.Add(Py.Strip(text));
			}
		}
		return list;
	}

	private static IEnumerable<XmlNode> Iter(XmlNode n)
	{
		if (n == null)
		{
			yield break;
		}
		yield return n;
		foreach (XmlNode childNode in n.ChildNodes)
		{
			if (childNode.NodeType != XmlNodeType.Element)
			{
				continue;
			}
			foreach (XmlNode item in Iter(childNode))
			{
				yield return item;
			}
		}
	}

	public static List<object> BmclForgeArtifacts(object data, string mc_version)
	{
		string b = Py.Strip(mc_version ?? "");
		List<object> list = new List<object>();
		if (!(data is List<object> list2))
		{
			return list;
		}
		foreach (object item2 in list2)
		{
			if (!(item2 is PyDict pyDict))
			{
				continue;
			}
			string text = Py.Strip(Py.Str(Py.Or(pyDict.Get("version"), "")));
			string text2 = Py.Strip(Py.Str(Py.Or(pyDict.Get("mcversion"), b)));
			string text3 = Py.Strip(Py.Str(Py.Or(pyDict.Get("branch"), "")));
			if (text.Length != 0)
			{
				string item = ((text2.Length > 0 && (text == text2 || text.StartsWith(text2 + "-", StringComparison.Ordinal))) ? text : ((text3.Length <= 0) ? (text2 + "-" + text) : (text2 + "-" + text + "-" + text3)));
				if (!list.Contains(item))
				{
					list.Add(item);
				}
			}
		}
		return list;
	}

	public static int ForgeSortCompare(string a, string b, string mc_version)
	{
		List<object> list = ForgeSortKey(a, mc_version);
		List<object> list2 = ForgeSortKey(b, mc_version);
		List<long> list3 = (List<long>)list[0];
		List<long> list4 = (List<long>)list2[0];
		for (int i = 0; i < Math.Min(list3.Count, list4.Count); i++)
		{
			int num = list3[i].CompareTo(list4[i]);
			if (num != 0)
			{
				return num;
			}
		}
		if (list3.Count != list4.Count)
		{
			return list3.Count.CompareTo(list4.Count);
		}
		int num2 = ((long)list[1]).CompareTo((long)list2[1]);
		if (num2 != 0)
		{
			return num2;
		}
		return ((long)list[2]).CompareTo((long)list2[2]);
	}

	public static List<object> ForgeSortKey(string full, string mc_version)
	{
		List<string> list = SplitForgeArtifact(full, mc_version);
		List<long> list2 = new List<long>();
		string[] array = Regex.Split(list[1] ?? "", "[^\\d]+");
		foreach (string text in array)
		{
			if (text.Length > 0 && text.All(char.IsDigit))
			{
				long num = 0L;
				string text2 = text;
				foreach (char c in text2)
				{
					num = num * 10 + (long)char.GetNumericValue(c);
				}
				list2.Add(num);
			}
		}
		return new List<object>
		{
			list2,
			(long)((list[2] != null) ? 1 : 0),
			(long)(full ?? "").Length
		};
	}

	public static long[] _mc_tuple(string mc_version)
	{
		string[] array = Regex.Split(mc_version ?? "", "[.\\-]");
		try
		{
			long num = Py.Int(array[0]);
			long num2 = Py.Int(array[1]);
			long num3 = ((array.Length > 2) ? Py.Int(array[2]) : 0);
			return new long[3] { num, num2, num3 };
		}
		catch (Exception ex) when (ex is IndexOutOfRangeException || ex is ValueError)
		{
			return new long[3];
		}
	}
}
