using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace HelL.Core.Launcher;

public static class Java
{
	public static readonly PyDict JAVA_VENDORS = PyDict.Of("adoptium", "Adoptium Temurin", "zulu", "Azul Zulu", "microsoft", "Microsoft OpenJDK");

	public static readonly string[] RUNTIME_MANIFEST_URLS = new string[2] { "https://piston-meta.mojang.com/v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json", "https://launchermeta.mojang.com/v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json" };

	public const long RUNTIME_MANIFEST_TTL = 86400L;

	public const string ADOPTIUM_BINARY_URL = "https://api.adoptium.net/v3/binary/latest/{major}/ga/{os}/{arch}/jre/hotspot/normal/eclipse";

	public static readonly string PLATFORM_KEY = _PlatformKey();

	private static readonly Dictionary<string, (long Major, long Length, DateTime Written)> _MAJOR_CACHE = new Dictionary<string, (long, long, DateTime)>();

	private static readonly object _MAJOR_LOCK = new object();

	private static double _SYS_JAVA_T;

	private static List<object> _SYS_JAVA_DATA;

	private static readonly object _SYS_JAVA_LOCK = new object();

	public static Func<string, string> VersionOutputHook;

	private static readonly Regex _VER_RE = new Regex("version \"([^\"]+)\"", RegexOptions.CultureInvariant);

	private static readonly string[] _JAVA_HINTS = new string[7] { "java", "jdk", "jre", "zulu", "jbr", "temurin", "adoptium" };

	public static List<object> JavaVendorList()
	{
		return JAVA_VENDORS.Keys.Cast<object>().ToList();
	}

	public static string JavaVendorLabel(object vendor)
	{
		string key = Py.Lower(Py.S(vendor));
		if (!JAVA_VENDORS.ContainsKey(key))
		{
			return Py.Str(Py.Or(vendor, "adoptium"));
		}
		return (string)JAVA_VENDORS[key];
	}

	private static string _PlatformKey()
	{
		string oS_NAME = Utils.OS_NAME;
		string aRCH = Utils.ARCH;
		switch (oS_NAME)
		{
		case "windows":
			if (!(aRCH == "x64") && !(aRCH == "arm64"))
			{
				return "windows-x86";
			}
			return "windows-" + aRCH;
		case "linux":
			return aRCH switch
			{
				"arm64" => "linux-arm64",
				"x86" => "linux-i386",
				"x64" => "linux",
				_ => "linux",
			};
		case "osx":
			if (!(aRCH == "arm64"))
			{
				return "osx";
			}
			return "osx-arm64";
		default:
			return "linux";
		}
	}

	private static string Pick(string key, params string[] kv)
	{
		for (int i = 0; i + 1 < kv.Length; i += 2)
		{
			if (kv[i] == key)
			{
				return kv[i + 1];
			}
		}
		throw new KeyError(Py.ReprStr(key));
	}

	private static string _AdoptiumOs()
	{
		return Pick(Utils.OS_NAME, "windows", "windows", "osx", "mac", "linux", "linux");
	}

	private static string _AdoptiumArch(string arch = null)
	{
		return Pick(arch ?? Utils.ARCH, "x64", "x64", "x86", "x86", "arm64", "aarch64");
	}

	public static long AdoptiumMajor(long major)
	{
		if (major <= 8)
		{
			return 8L;
		}
		if (major <= 11)
		{
			return 11L;
		}
		if (major <= 17)
		{
			return 17L;
		}
		return 21L;
	}

	public static string JavaVersionOutput(object java_exe)
	{
		if (!Py.Truthy(java_exe))
		{
			return "";
		}
		Func<string, string> versionOutputHook = VersionOutputHook;
		if (versionOutputHook != null)
		{
			return versionOutputHook(Py.Str(java_exe));
		}
		try
		{
			ProcessStartInfo processStartInfo = new ProcessStartInfo
			{
				FileName = Utils.AbsPath(Py.Str(java_exe)),
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				RedirectStandardInput = true,
				WorkingDirectory = Utils.AbsPath(Utils.ROOT)
			};
			processStartInfo.Arguments = "-version";
			Process proc = Process.Start(processStartInfo);
			try
			{
				try
				{
					proc.StandardInput.Close();
				}
				catch (Exception)
				{
				}
				MemoryStream outMs = new MemoryStream();
				MemoryStream errMs = new MemoryStream();
				Thread thread = new Thread((ThreadStart)delegate
				{
					try
					{
						proc.StandardOutput.BaseStream.CopyTo(outMs);
					}
					catch (Exception)
					{
					}
				})
				{
					IsBackground = true
				};
				Thread thread2 = new Thread((ThreadStart)delegate
				{
					try
					{
						proc.StandardError.BaseStream.CopyTo(errMs);
					}
					catch (Exception)
					{
					}
				})
				{
					IsBackground = true
				};
				thread.Start();
				thread2.Start();
				if (!proc.WaitForExit(6000))
				{
					try
					{
						proc.Kill();
					}
					catch (Exception)
					{
					}
					return "";
				}
				thread.Join(2000);
				thread2.Join(2000);
				byte[] bytes = errMs.ToArray().Concat(outMs.ToArray()).ToArray();
				string text = Encoding.UTF8.GetString(bytes);
				if (!Py.Lower(text).Contains("version"))
				{
					text = Gbk().GetString(bytes);
				}
				return text;
			}
			finally
			{
				if (proc != null)
				{
					((IDisposable)proc).Dispose();
				}
			}
		}
		catch (Exception)
		{
			return "";
		}
	}

	internal static Encoding Gbk()
	{
		try
		{
			return Encoding.GetEncoding(936);
		}
		catch (Exception)
		{
			return Encoding.UTF8;
		}
	}

	public static long? GetJavaMajor(object java_exe)
	{
		string key = Py.Str(java_exe);
		var executable = new FileInfo(Utils.AbsPath(key));
		if (!executable.Exists) { lock (_MAJOR_LOCK) _MAJOR_CACHE.Remove(key); return null; }
		lock (_MAJOR_LOCK)
		{
			if (_MAJOR_CACHE.TryGetValue(key, out var value) && value.Length == executable.Length && value.Written == executable.LastWriteTimeUtc)
			{
				return value.Major;
			}
		}
		string input = JavaVersionOutput(java_exe);
		Match match = _VER_RE.Match(input);
		if (!match.Success)
		{
			return null;
		}
		string value2 = match.Groups[1].Value;
		long value3;
		try
		{
			string[] array = value2.Split('.');
			if (value2.StartsWith("1."))
			{
				if (array.Length < 2)
				{
					return null;
				}
				value3 = Py.Int(array[1]);
			}
			else
			{
				value3 = Py.Int(array[0]);
			}
		}
		catch (ValueError)
		{
			return null;
		}
		catch (TypeError)
		{
			return null;
		}
		lock (_MAJOR_LOCK)
		{
			_MAJOR_CACHE[key] = (value3, executable.Length, executable.LastWriteTimeUtc);
		}
		return value3;
	}

	public static void ResetCaches()
	{
		lock (_MAJOR_LOCK)
		{
			_MAJOR_CACHE.Clear();
		}
		lock (_SYS_JAVA_LOCK)
		{
			_SYS_JAVA_DATA = null;
			_SYS_JAVA_T = 0.0;
		}
	}

	private static object MajorObj(long? m)
	{
		if (!m.HasValue)
		{
			return null;
		}
		return m.Value;
	}

	public static bool JavaUsableFor(PyDict version_json, object java_exe)
	{
		if (!Py.Truthy(java_exe))
		{
			return false;
		}
		try
		{
			if (!new PPath(Py.Str(java_exe)).IsFile())
			{
				return false;
			}
		}
		catch (Exception)
		{
			return false;
		}
		long num = RequiredJavaMajor(version_json);
		long? javaMajor = GetJavaMajor(java_exe);
		if (javaMajor.HasValue)
		{
			return javaMajor.Value >= num;
		}
		return false;
	}

	public static string ResolveLaunchJava(PyDict version_json, object prefer = null, DownloadManager dm = null, Action<string, long, long> on_progress = null, Action<string> on_note = null)
	{
		long num = RequiredJavaMajor(version_json);
		string text = PickJavaForVersion(version_json, prefer);
		if (text != null && !JavaUsableFor(version_json, text))
		{
			on_note?.Invoke("候选 Java " + Py.Str(Py.Or(MajorObj(GetJavaMajor(text)), "?")) + " 无法启动此版本（需要 " + num + "+）");
			text = PickJavaForVersion(version_json);
		}
		if ((text == null || !JavaUsableFor(version_json, text)) && dm != null)
		{
			on_note?.Invoke("未找到 Java " + num + "，自动下载中…");
			text = EnsureJavaForVersion(version_json, dm, on_progress);
		}
		if (text != null && JavaUsableFor(version_json, text))
		{
			return PPath.Norm(text);
		}
		foreach (PyDict item in ListInstalledJavas().Concat<object>(FindSystemJavas()))
		{
			object obj = item["exe"];
			if (JavaUsableFor(version_json, obj))
			{
				return PPath.Norm(Py.Str(obj));
			}
		}
		return text;
	}

	internal static string RealPath(string p)
	{
		string text = PPath.Abs(p);
		return new PPath(text).Resolve().ToString();
	}

	private static bool _TakeJava(string home, List<string> results, HashSet<string> seen)
	{
		string[] array = new string[2]
		{
			PPath.Join(home, "bin"),
			PPath.Norm(home)
		};
		foreach (string text in array)
		{
			string[] array2 = new string[3] { "java.exe", "java", "javaw.exe" };
			foreach (string text2 in array2)
			{
				string text3 = PPath.Join(text, text2);
				if (new PPath(text3).IsFile())
				{
					string item = RealPath(text3);
					if (seen.Add(item))
					{
						results.Add(text3);
					}
					return true;
				}
			}
		}
		return false;
	}

	private static void _WalkJavas(string root, List<string> results, HashSet<string> seen, int depth = 0)
	{
		if (depth > 6 || string.IsNullOrEmpty(root) || !new PPath(root).IsDir())
		{
			return;
		}
		_TakeJava(root, results, seen);
		List<FileSystemInfo> items;
		try
		{
			items = new DirectoryInfo(Utils.AbsPath(root)).EnumerateFileSystemInfos().ToList();
			items = Py.Sorted(items, (FileSystemInfo e) => Py.Lower(e.Name));
		}
		catch (Exception)
		{
			return;
		}
		foreach (FileSystemInfo item in items)
		{
			string name = item.Name;
			if (name.StartsWith("."))
			{
				continue;
			}
			bool flag;
			try
			{
				flag = item is DirectoryInfo && (item.Attributes & FileAttributes.ReparsePoint) == 0;
			}
			catch (Exception)
			{
				continue;
			}
			if (!flag)
			{
				continue;
			}
			switch (name)
			{
			case "node_modules":
			case "site-packages":
			case "WinSxS":
			case "Windows":
			case "AppData":
				continue;
			}
			string text = PPath.Join(root, name);
			bool flag2 = _TakeJava(text, results, seen);
			string low = Py.Lower(name);
			if (!(low == "bin") && !(low == "lib"))
			{
				bool flag3 = _JAVA_HINTS.Any((string t) => low.Contains(t));
				if (!flag2 || flag3 || name == "Contents")
				{
					_WalkJavas(text, results, seen, depth + 1);
				}
			}
		}
	}

	private static string Which(string name)
	{
		string text = Environment.GetEnvironmentVariable("PATH") ?? "";
		List<string> list = new List<string> { "" };
		string[] array;
		if (Utils.IS_WINDOWS)
		{
			list = new List<string>();
			array = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD").Split(';');
			foreach (string text2 in array)
			{
				if (text2.Length > 0)
				{
					list.Add(text2);
				}
			}
		}
		array = text.Split(Path.PathSeparator);
		foreach (string text3 in array)
		{
			if (text3.Length == 0)
			{
				continue;
			}
			foreach (string item in list)
			{
				try
				{
					string text4 = Path.Combine(text3, name + item);
					if (File.Exists(text4))
					{
						return text4;
					}
				}
				catch (Exception)
				{
				}
			}
		}
		return null;
	}

	public static List<object> FindSystemJavas()
	{
		double num = Py.Time();
		lock (_SYS_JAVA_LOCK)
		{
			if (_SYS_JAVA_DATA != null && num - _SYS_JAVA_T < 60.0)
			{
				return _SYS_JAVA_DATA;
			}
			List<string> list = new List<string>();
			HashSet<string> seen = new HashSet<string>();
			List<string> list2 = new List<string>();
			string environmentVariable = Environment.GetEnvironmentVariable("JAVA_HOME");
			if (!string.IsNullOrEmpty(environmentVariable))
			{
				list2.Add(PPath.Norm(environmentVariable));
			}
			string[] array = new string[2] { "java", "javaw" };
			for (int i = 0; i < array.Length; i++)
			{
				string text = Which(array[i]);
				if (text != null)
				{
					list2.Add(new PPath(RealPath(text)).Parent.Parent.ToString());
				}
			}
			if (Utils.IS_WINDOWS)
			{
				array = new string[3]
				{
					Environment.GetEnvironmentVariable("ProgramFiles") ?? "C:\\Program Files",
					Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? "C:\\Program Files (x86)",
					Py.Truthy(Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "") ? Environment.GetEnvironmentVariable("LOCALAPPDATA") : "C:/Users/Public"
				};
				for (int j = 0; j < array.Length; j++)
				{
					string text2 = PPath.Norm(array[j]);
					if (!new PPath(text2).IsDir())
					{
						continue;
					}
					IEnumerable<string> enumerable;
					try
					{
						enumerable = Directory.EnumerateFileSystemEntries(Utils.AbsPath(text2)).Select(Path.GetFileName).ToList();
					}
					catch (Exception)
					{
						continue;
					}
					foreach (string item in enumerable)
					{
						string text3 = Py.Lower(item);
						if (text3.Contains("java") || text3.Contains("jdk") || text3.Contains("jre") || text3.Contains("zulu") || text3.Contains("jbr"))
						{
							list2.Add(PPath.Join(text2, item));
						}
					}
				}
			}
			else if (Utils.IS_MAC)
			{
				list2.Add("/Library/Java/JavaVirtualMachines");
				list2.Add("/opt/homebrew/opt");
				list2.Add("/usr/local/opt");
			}
			else
			{
				list2.Add("/usr/lib/jvm");
			}
			foreach (string item2 in list2)
			{
				_WalkJavas(item2, list, seen);
			}
			List<object> list3 = new List<object>();
			foreach (string item3 in list.Take(50))
			{
				long? javaMajor = GetJavaMajor(item3);
				list3.Add(PyDict.Of("name", "Java " + Py.Str(Py.Or(MajorObj(javaMajor), "?")) + " (" + item3 + ")", "exe", item3, "major", MajorObj(javaMajor)));
			}
			list3 = Py.Sorted(list3, (object x) => Py.L(((PyDict)x)["major"] == null, Py.Or(((PyDict)x)["major"], 0L)), reverse: true);
			_SYS_JAVA_T = Py.Time();
			_SYS_JAVA_DATA = list3;
			return list3;
		}
	}

	public static List<object> ListInstalledJavas()
	{
		string java_dir = Config.CONFIG.java_dir;
		List<object> list = new List<object>();
		if (!new PPath(java_dir).IsDir())
		{
			return list;
		}
		List<string> list2;
		try
		{
			list2 = Directory.EnumerateFileSystemEntries(Utils.AbsPath(java_dir)).Select(Path.GetFileName).ToList();
		}
		catch (Exception)
		{
			return list;
		}
		list2.Sort(string.CompareOrdinal);
		foreach (string item in list2)
		{
			string text = PPath.Join(java_dir, item);
			if (!new PPath(text).IsDir())
			{
				continue;
			}
			PyDict pyDict = Utils.ReadJson(PPath.Join(text, "runtime.meta.json")) as PyDict;
			if (!Py.Truthy(pyDict))
			{
				pyDict = new PyDict();
			}
			string text2 = Utils.FindExecutable(text);
			if (text2 != null)
			{
				object obj = pyDict.Get("major");
				if (!(obj is long) && !(obj is bool))
				{
					obj = MajorObj(GetJavaMajor(text2));
				}
				list.Add(PyDict.Of("name", pyDict.Get("name", item), "exe", text2, "major", obj, "dir", text, "meta", pyDict));
			}
		}
		return list;
	}

	public static List<object> CachedSystemJavas()
	{
		lock (_SYS_JAVA_LOCK)
		{
			return (_SYS_JAVA_DATA != null && _SYS_JAVA_DATA.Count > 0) ? new List<object>(_SYS_JAVA_DATA) : new List<object>();
		}
	}

	private static List<object> Unique(IEnumerable<object> rows)
	{
		HashSet<string> hashSet = new HashSet<string>();
		List<object> list = new List<object>();
		foreach (PyDict row in rows)
		{
			string item = Py.Str(row.Req("exe"));
			if (hashSet.Add(item))
			{
				list.Add(row);
			}
		}
		return list;
	}

	public static List<object> CachedAllJavas()
	{
		return Unique(ListInstalledJavas().Concat<object>(CachedSystemJavas()));
	}

	public static void WarmSystemJavasAsync()
	{
		Thread thread = new Thread((ThreadStart)delegate
		{
			try
			{
				FindSystemJavas();
			}
			catch (Exception)
			{
			}
		});
		thread.IsBackground = true;
		thread.Name = "tipcl-java-warmup";
		thread.Start();
	}

	public static List<object> AllJavas()
	{
		return Unique(ListInstalledJavas().Concat<object>(FindSystemJavas()));
	}

	public static long RequiredJavaMajor(PyDict version_json)
	{
		version_json = version_json ?? new PyDict();
		object o = Py.DictOr(Py.Or(version_json.Get("javaVersion"), new PyDict())).Get("majorVersion");
		if (Py.Truthy(o))
		{
			try
			{
				return Py.Int(o);
			}
			catch (TypeError)
			{
			}
			catch (ValueError)
			{
			}
		}
		if (Py.Lower(Py.Str(Py.Or(version_json.Get("mainClass"), ""))).Contains("bootstraplauncher"))
		{
			return 17L;
		}
		IEnumerable obj = (Py.Or(Py.DictOr(Py.Or(version_json.Get("arguments"), new PyDict())).Get("jvm"), new List<object>()) as IEnumerable) ?? new List<object>();
		List<object> list = new List<object>();
		foreach (object item2 in obj)
		{
			if (item2 is string item)
			{
				list.Add(item);
			}
			else if (item2 is PyDict pyDict)
			{
				object obj2 = pyDict.Get("value", new List<object>());
				if (obj2 is List<object> collection)
				{
					list.AddRange(collection);
				}
				else
				{
					list.Add(obj2);
				}
			}
		}
		if (list.Any(delegate(object t)
		{
			string text = t as string;
			switch (text)
			{
			default:
				return text == "--add-modules";
			case "-p":
			case "--module-path":
				return true;
			case null:
				return false;
			}
		}))
		{
			return 17L;
		}
		return 8L;
	}

	public static string PreferencePath(object prefer)
	{
		if (!Py.Truthy(prefer))
		{
			return null;
		}
		string text = Py.Str(prefer);
		PPath pPath = new PPath(text);
		if (pPath.IsFile())
		{
			return pPath.ToString();
		}
		if (pPath.IsDir())
		{
			string text2 = Utils.FindExecutable(pPath.ToString());
			if (text2 != null)
			{
				return text2;
			}
		}
		foreach (PyDict item in AllJavas())
		{
			if (Py.Eq(item.Get("exe"), prefer) || Py.Eq(item.Get("dir"), prefer) || Py.Eq(item.Get("name"), prefer))
			{
				return Py.Str(item["exe"]);
			}
		}
		return null;
	}

	public static string PickJavaForVersion(PyDict version_json, object prefer = null)
	{
		long required = RequiredJavaMajor(version_json);
		if (Py.Truthy(prefer))
		{
			string text = PreferencePath(prefer);
			if (text != null)
			{
				long? javaMajor = GetJavaMajor(text);
				if (javaMajor.HasValue && javaMajor.Value >= required)
				{
					return text;
				}
			}
		}
		bool needX86 = _IsLegacy32bit(version_json);
		string text2 = Match(ListInstalledJavas());
		if (text2 != null)
		{
			return text2;
		}
		return Match(FindSystemJavas());
		string Match(List<object> javas)
		{
			List<PyDict> list = (from PyDict j in javas
				where Py.Eq(j.Get("major"), required)
				select j).ToList();
			if (needX86)
			{
				List<PyDict> list2 = list.Where((PyDict j) => Py.S(j.Get("dir")).Contains("x86") || Py.S(j.Get("exe")).Contains("x86")).ToList();
				if (list2.Count == 0)
				{
					return null;
				}
				list = list2;
			}
			if (list.Count > 0)
			{
				return Py.Str(list[0]["exe"]);
			}
			List<PyDict> list3 = (from PyDict j in javas
				where Py.Truthy(j.Get("major")) && Py.Cmp(j["major"], required) > 0
				select j).ToList();
			if (list3.Count > 0 && required >= 17)
			{
				PyDict pyDict = list3[0];
				foreach (PyDict item in list3)
				{
					if (Py.Cmp(item["major"], pyDict["major"]) < 0)
					{
						pyDict = item;
					}
				}
				return Py.Str(pyDict["exe"]);
			}
			return null;
		}
	}

	public static object GetRuntimeManifest(DownloadManager dm, bool force = false)
	{
		string text = PPath.Join(Utils.ROOT, "cache", "java_runtime_manifest.json");
		string path = PPath.Join(Utils.ROOT, "cache", "java_runtime_manifest.meta");
		if (!force)
		{
			PyDict pyDict = Utils.ReadJson(path) as PyDict;
			if (Py.Truthy(pyDict) && new PPath(text).IsFile() && Py.Time() - Py.Float(pyDict.Get("fetched_at", 0L)) < 86400.0)
			{
				object obj = Utils.ReadJson(text);
				if (Py.Truthy(obj))
				{
					return obj;
				}
			}
		}
		Exception ex = null;
		string[] rUNTIME_MANIFEST_URLS = RUNTIME_MANIFEST_URLS;
		foreach (string text2 in rUNTIME_MANIFEST_URLS)
		{
			try
			{
				object obj2 = dm.FetchJson(text2, 60L);
				Utils.EnsureDir(new PPath(text).Parent.ToString());
				Utils.WriteJson(text, obj2);
				Utils.WriteJson(path, PyDict.Of("fetched_at", Py.Time(), "url", text2));
				return obj2;
			}
			catch (Exception ex2)
			{
				ex = ex2;
				Utils.log.Warning("获取 Java 运行时清单失败 %s: %s", text2, Py.ExcStr(ex2));
			}
		}
		object obj3 = Utils.ReadJson(text);
		if (Py.Truthy(obj3))
		{
			Utils.log.Warning("使用缓存中的旧 Java 运行时清单。");
			return obj3;
		}
		throw new DownloadError("无法获取 Java 运行时清单: " + ((ex == null) ? "None" : Py.ExcStr(ex)));
	}

	private static bool _ArchiveValid(string path, string suffix)
	{
		PPath pPath = new PPath(path);
		try
		{
			if (!pPath.IsFile() || new FileInfo(Utils.AbsPath(path)).Length == 0L)
			{
				return false;
			}
			if (suffix == ".zip")
			{
				try
				{
					using (ZipArchive zipArchive = Utils.OpenZip(path))
					{
						_ = zipArchive.Entries.Count;
					}
					return true;
				}
				catch (Exception)
				{
					return false;
				}
			}
			try
			{
				using FileStream stream = File.OpenRead(Utils.AbsPath(path));
				using GZipStream gZipStream = new GZipStream(stream, CompressionMode.Decompress);
				byte[] buffer = new byte[512];
				int i;
				int num;
				for (i = 0; i < 512; i += num)
				{
					num = gZipStream.Read(buffer, i, 512 - i);
					if (num <= 0)
					{
						break;
					}
				}
				return i == 512;
			}
			catch (Exception)
			{
				return false;
			}
		}
		catch (IOException)
		{
			return false;
		}
	}

	private static void _SafeUnlink(string path)
	{
		try
		{
			string path2 = Utils.AbsPath(path);
			if (File.Exists(path2))
			{
				File.Delete(path2);
			}
		}
		catch (Exception)
		{
		}
	}

	internal static void Chmod755(string p)
	{
#if HELL_PORTABLE
		if (!OperatingSystem.IsWindows())
			File.SetUnixFileMode(Utils.AbsPath(p), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
				| UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
#endif
	}

	public static string InstallMojangRuntime(DownloadManager dm, string component, Action<string, long, long> on_progress = null, bool force = false)
	{
		string java_dir = Config.CONFIG.java_dir;
		PyDict pyDict = Py.DictOr(GetRuntimeManifest(dm)).Get(PLATFORM_KEY) as PyDict;
		if (!Py.Truthy(pyDict))
		{
			Utils.log.Warning("Mojang 运行时清单中没有平台 %s", PLATFORM_KEY);
			return null;
		}
		List<object> list = pyDict.Get(component) as List<object>;
		if (!Py.Truthy(list))
		{
			return null;
		}
		PyDict pyDict2 = Py.DictOr(list[list.Count - 1]);
		PyDict pyDict3 = Py.DictOr(Py.Or(pyDict2.Get("manifest"), new PyDict()));
		object obj = pyDict3.Get("url");
		object obj2 = pyDict3.Get("sha1");
		if (!Py.Truthy(obj))
		{
			return null;
		}
		string text = Py.Str(Py.DictOr(Py.Or(pyDict2.Get("version"), new PyDict())).Get("name", "?"));
		string text2 = PPath.Join(java_dir, component + "-" + PLATFORM_KEY);
		string text3 = Utils.FindExecutable(text2);
		if (text3 != null && !force && GetJavaMajor(text3).HasValue)
		{
			return text3;
		}
		Utils.EnsureDir(java_dir);
		_SafeUnlink(PPath.Join(java_dir, component + "-" + PLATFORM_KEY + ".zip"));
		on_progress?.Invoke("下载 Mojang Java 运行时 " + component + " (" + text + ")", 0L, 1L);
		string text4 = PPath.Join(java_dir, component + "-" + PLATFORM_KEY + ".manifest.json");
		dm.Download(obj, text4, obj2 as string, (pyDict3.Get("size") == null) ? ((long?)null) : new long?(Py.Int(pyDict3.Get("size"))), force);
		PyDict pyDict4 = Py.DictOr(Py.Or(Py.DictOr(Utils.ReadJson(text4)).Get("files"), new PyDict()));
		if (pyDict4.Count == 0)
		{
			throw new DownloadError("Mojang 运行时清单无效: " + component);
		}
		if (force)
		{
			Utils.RemoveTree(text2);
		}
		Utils.EnsureDir(text2);
		List<DownloadTask> list2 = new List<DownloadTask>();
		foreach (KeyValuePair<string, object> item in pyDict4)
		{
			PyDict pyDict5 = Py.DictOr(item.Value);
			object a = pyDict5.Get("type");
			string text5 = PPath.Join(text2, item.Key);
			if (Py.Eq(a, "directory"))
			{
				Directory.CreateDirectory(Utils.AbsPath(text5));
			}
			else
			{
				if (!Py.Eq(a, "file"))
				{
					continue;
				}
				PyDict pyDict6 = Py.DictOr(Py.Or(Py.DictOr(Py.Or(pyDict5.Get("downloads"), new PyDict())).Get("raw"), new PyDict()));
				object obj3 = pyDict6.Get("url");
				object obj4 = pyDict6.Get("sha1");
				object obj5 = pyDict6.Get("size");
				if (!Py.Truthy(obj3))
				{
					Utils.log.Warning("Mojang 运行时缺少 raw 下载: %s", item.Key);
					continue;
				}
				Directory.CreateDirectory(Path.GetDirectoryName(Utils.AbsPath(text5)));
				if (force || !Utils.FileMatches(text5, obj4, obj5))
				{
					list2.Add(new DownloadTask(obj3, text5, obj4 as string, (obj5 == null) ? ((long?)null) : new long?(Py.Int(obj5))));
				}
			}
		}
		if (list2.Count > 0)
		{
			dm.DownloadAll(list2, "下载 Mojang Java " + component);
		}
		if (!Utils.IS_WINDOWS)
		{
			foreach (KeyValuePair<string, object> item2 in pyDict4)
			{
				if (Py.Truthy(Py.DictOr(item2.Value).Get("executable")))
				{
					string text6 = PPath.Join(text2, item2.Key);
					if (new PPath(text6).IsFile())
					{
						Chmod755(text6);
					}
				}
			}
		}
		text3 = Utils.FindExecutable(text2);
		if (text3 != null)
		{
			Utils.WriteJson(PPath.Join(text2, "runtime.meta.json"), PyDict.Of("kind", "mojang", "name", "Mojang " + component + " (" + text + ")", "component", component, "version", text, "major", MajorObj(GetJavaMajor(text3))));
			return text3;
		}
		Utils.log.Warning("Mojang 运行时安装后未找到 java 可执行文件: %s", text2);
		return null;
	}

	private static string ArchiveSuffix()
	{
		if (!Utils.IS_WINDOWS)
		{
			return ".tar.gz";
		}
		return ".zip";
	}

	public static string InstallAdoptium(DownloadManager dm, long major, Action<string, long, long> on_progress = null, bool force = false, string arch = null)
	{
		major = AdoptiumMajor(major);
		arch = arch ?? Utils.ARCH;
		string newValue = _AdoptiumArch(arch);
		string java_dir = Config.CONFIG.java_dir;
		string text = PPath.Join(java_dir, "adoptium-" + major + "-" + arch);
		string text2 = Utils.FindExecutable(text);
		if (text2 != null && !force)
		{
			return text2;
		}
		string url = "https://api.adoptium.net/v3/binary/latest/{major}/ga/{os}/{arch}/jre/hotspot/normal/eclipse".Replace("{major}", major.ToString()).Replace("{os}", _AdoptiumOs()).Replace("{arch}", newValue);
		Utils.EnsureDir(java_dir);
		string text3 = ArchiveSuffix();
		string text4 = PPath.Join(java_dir, "adoptium-" + major + "-" + arch + text3);
		on_progress?.Invoke("下载 Adoptium Java " + major + " (" + arch + ") 运行时", 0L, 1L);
		if (!_ArchiveValid(text4, text3))
		{
			dm.Download(url, text4, null, null, force: true);
		}
		try
		{
			Utils.RemoveTree(text);
			Utils.EnsureDir(text);
			DownloadManager.ExtractArchive(text4, text);
		}
		catch (Exception)
		{
			Utils.RemoveTree(text);
			_SafeUnlink(text4);
			throw;
		}
		finally
		{
			_SafeUnlink(text4);
		}
		text2 = Utils.FindExecutable(text);
		if (text2 == null)
		{
			Utils.RemoveTree(text);
			throw new DownloadError("Adoptium Java " + major + " 解压后未找到 java 可执行文件");
		}
		Utils.WriteJson(PPath.Join(text, "runtime.meta.json"), PyDict.Of("kind", "adoptium", "name", "Adoptium Java " + major + " (" + arch + ")", "version", major.ToString(), "major", MajorObj(GetJavaMajor(text2)), "arch", arch));
		return text2;
	}

	private static bool _IsLegacy32bit(PyDict version_json)
	{
		if (!Utils.IS_WINDOWS || !(Utils.ARCH == "x64"))
		{
			return false;
		}
		return Manifest.IsLegacyVersion(version_json);
	}

	public static string EnsureJavaForVersion(PyDict version_json, DownloadManager dm, Action<string, long, long> on_progress = null, bool force = false)
	{
		string text = PickJavaForVersion(version_json);
		if (text != null && !force)
		{
			return text;
		}
		PyDict pyDict = Py.DictOr(Py.Or(version_json.Get("javaVersion"), new PyDict()));
		object obj = pyDict.Get("component");
		object o = Py.Or(pyDict.Get("majorVersion"), RequiredJavaMajor(version_json));
		if (Py.Truthy(obj) && !Py.Eq(obj, "jre-legacy"))
		{
			try
			{
				string text2 = InstallMojangRuntime(dm, Py.Str(obj), on_progress, force);
				if (text2 != null)
				{
					return text2;
				}
			}
			catch (Exception e)
			{
				Utils.log.Warning("Mojang 运行时下载失败，回退 Adoptium: %s", Py.ExcStr(e));
			}
		}
		string arch = (_IsLegacy32bit(version_json) ? "x86" : null);
		return InstallAdoptium(dm, Py.Int(o), on_progress, force, arch);
	}

	public static string JavaForInstaller(string loader, DownloadManager dm, Action<string, long, long> on_progress = null, long minMajor = 17L)
	{
		if (loader == "forge-legacy")
		{
			foreach (PyDict item in ListInstalledJavas().Concat<object>(FindSystemJavas()))
			{
				if (Py.Eq(item.Get("major"), 8L))
				{
					return Py.Str(item["exe"]);
				}
			}
			return InstallAdoptium(dm, 8L, on_progress);
		}
		long num = Math.Max(17L, minMajor);
		foreach (PyDict item2 in ListInstalledJavas())
		{
			if (Py.Truthy(item2.Get("major")) && Py.Cmp(item2["major"], num) >= 0)
			{
				return Py.Str(item2["exe"]);
			}
		}
		foreach (PyDict item3 in FindSystemJavas())
		{
			if (Py.Truthy(item3.Get("major")) && Py.Cmp(item3["major"], num) >= 0)
			{
				return Py.Str(item3["exe"]);
			}
		}
		return InstallAdoptium(dm, num, on_progress);
	}

	public static string InstallJavaVendor(DownloadManager dm, object major, object vendor = null, Action<string, long, long> on_progress = null, bool force = false, string arch = null)
	{
		string text = Py.Lower(Py.Str(Py.Or(vendor, "adoptium")));
		return text switch
		{
			"adoptium" => InstallAdoptium(dm, Py.Int(major), on_progress, force, arch),
			"zulu" => _InstallZulu(dm, Py.Int(major), on_progress, force, arch),
			"microsoft" => _InstallMicrosoft(dm, Py.Int(major), on_progress, force, arch),
			_ => throw new DownloadError("未知的 Java 发行版: " + text),
		};
	}

	private static string _ZuluOs()
	{
		return Pick(Utils.OS_NAME, "windows", "windows", "osx", "macos", "linux", "linux");
	}

	private static string _ZuluArch(string arch = null)
	{
		return Pick(arch ?? Utils.ARCH, "x64", "x64", "x86", "x86", "arm64", "aarch64");
	}

	private static string _InstallZulu(DownloadManager dm, long major, Action<string, long, long> on_progress, bool force, string arch)
	{
		major = AdoptiumMajor(major);
		arch = arch ?? Utils.ARCH;
		string text = _ZuluOs();
		string text2 = _ZuluArch(arch);
		string java_dir = Config.CONFIG.java_dir;
		string text3 = PPath.Join(java_dir, "zulu-" + major + "-" + arch);
		string text4 = Utils.FindExecutable(text3);
		if (text4 != null && !force)
		{
			return text4;
		}
		string url = "https://api.azul.com/metadata/v1/zulu/packages?java_version=" + major + "&os=" + text + "&arch=" + text2 + "&java_package_type=jre&archive_type=zip&latest=true";
		Utils.EnsureDir(java_dir);
		on_progress?.Invoke("查询 Azul Zulu Java " + major + " (" + arch + ")…", 0L, 1L);
		object obj;
		try
		{
			obj = dm.FetchJson(url, 25L);
		}
		catch (Exception)
		{
			obj = null;
		}
		if (!(obj is List<object> { Count: not 0 } list))
		{
			throw new DownloadError("Azul Zulu 没有 Java " + major + " (" + text + "/" + text2 + ") 的 JRE 包");
		}
		object obj2 = Py.DictOr(list[0]).Get("download_url");
		if (!Py.Truthy(obj2))
		{
			throw new DownloadError("Azul 返回的数据缺少下载地址");
		}
		string text5 = ArchiveSuffix();
		string text6 = PPath.Join(java_dir, "zulu-" + major + "-" + arch + text5);
		try
		{
			Utils.RemoveTree(text3);
			Utils.EnsureDir(text3);
			on_progress?.Invoke("下载 Zulu Java " + major + " (" + arch + ") 运行时", 0L, 1L);
			dm.Download(obj2, text6, null, null, force: true);
			DownloadManager.ExtractArchive(text6, text3);
		}
		catch (Exception)
		{
			Utils.RemoveTree(text3);
			_SafeUnlink(text6);
			throw;
		}
		finally
		{
			_SafeUnlink(text6);
		}
		text4 = Utils.FindExecutable(text3);
		if (text4 == null)
		{
			Utils.RemoveTree(text3);
			throw new DownloadError("Zulu Java " + major + " 解压后未找到 java 可执行文件");
		}
		Utils.WriteJson(PPath.Join(text3, "runtime.meta.json"), PyDict.Of("kind", "zulu", "name", "Zulu Java " + major + " (" + arch + ")", "version", major.ToString(), "major", MajorObj(GetJavaMajor(text4)), "arch", arch));
		return text4;
	}

	private static string _InstallMicrosoft(DownloadManager dm, long major, Action<string, long, long> on_progress, bool force, string arch)
	{
		major = AdoptiumMajor(major);
		arch = arch ?? Utils.ARCH;
		string java_dir = Config.CONFIG.java_dir;
		string text = PPath.Join(java_dir, "microsoft-" + major + "-" + arch);
		string text2 = Utils.FindExecutable(text);
		if (text2 != null && !force)
		{
			return text2;
		}
		string text3 = Pick(arch, "x64", "x64", "x86", "x86", "arm64", "aarch64");
		string text4 = Pick(Utils.OS_NAME, "windows", "windows", "osx", "mac", "linux", "linux");
		string url = "https://aka.ms/download-jdk/microsoft-jdk-" + major + "-" + text4 + "-" + text3 + ".zip";
		Utils.EnsureDir(java_dir);
		string text5 = ArchiveSuffix();
		string text6 = PPath.Join(java_dir, "microsoft-" + major + "-" + arch + text5);
		on_progress?.Invoke("下载 Microsoft Java " + major + " (" + arch + ") 运行时", 0L, 1L);
		try
		{
			Utils.RemoveTree(text);
			Utils.EnsureDir(text);
			dm.Download(url, text6, null, null, force: true);
			DownloadManager.ExtractArchive(text6, text);
		}
		catch (Exception)
		{
			Utils.RemoveTree(text);
			_SafeUnlink(text6);
			throw;
		}
		finally
		{
			_SafeUnlink(text6);
		}
		text2 = Utils.FindExecutable(text);
		if (text2 == null)
		{
			Utils.RemoveTree(text);
			throw new DownloadError("Microsoft Java " + major + " 解压后未找到 java 可执行文件");
		}
		Utils.WriteJson(PPath.Join(text, "runtime.meta.json"), PyDict.Of("kind", "microsoft", "name", "Microsoft Java " + major + " (" + arch + ")", "version", major.ToString(), "major", MajorObj(GetJavaMajor(text2)), "arch", arch));
		return text2;
	}
}
