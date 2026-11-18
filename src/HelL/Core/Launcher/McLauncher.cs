using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace HelL.Core.Launcher;

public static class McLauncher
{
	private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lparam);

	public sealed class LaunchCommand
	{
		public List<string> cmd;

		public string natives_dir;

		public string version_dir;

		public string game_dir;
	}

	private static readonly HashSet<string> _JVM_VALUE_FLAGS = new HashSet<string> { "-p", "-cp", "-classpath", "--class-path", "--module-path", "--add-modules", "--add-opens", "--add-exports", "--add-reads" };

	private static readonly string[] _GAME_WINDOW_CLASSES = new string[2] { "LWJGL", "GLFW30" };

	public static Func<PyDict, string> OfflineSkinApiHook = delegate(PyDict props)
	{
		byte[] array = Skin.LoadSkinPng(props);
		if (array == null || array.Length == 0)
		{
			return "";
		}
		Authlib.EnsureInjector();
		return SkinServer.ApiFor(Py.Or(props.Get("uuid"), ""), Py.Or(props.Get("name"), "Player"), array, Skin.SkinModel(props));
	};

	private static readonly string[] _KNOT_MAIN_CLASSES = new string[2] { "net.fabricmc.loader.impl.launch.knot.", "org.quiltmc.loader.impl.launch.knot." };

	private static readonly string[] _LOADER_LIB_PREFIXES = new string[2] { "net.fabricmc:fabric-loader:", "org.quiltmc:quilt-loader:" };

	private static string _version_type(string version_id, PyDict resolved)
	{
		PyDict pyDict = Utils.ReadJson(PPath.Join(Utils.ROOT, "cache", "version_manifest.json")) as PyDict;
		if (Py.Truthy(pyDict))
		{
			foreach (object item in (pyDict.Get("versions", new List<object>()) as IEnumerable) ?? new List<object>())
			{
				if (item is PyDict pyDict2 && Py.Eq(pyDict2.Get("id"), version_id))
				{
					return Py.Str(pyDict2.Get("type", "release"));
				}
			}
		}
		return Py.Str(Py.Or(resolved.Get("type"), "custom"));
	}

	private static List<string> _expand_args(object args_raw, PyDict placeholders, PyDict features)
	{
		List<string> list = new List<string>();
		foreach (object item in (Py.Or(args_raw, new List<object>()) as IEnumerable) ?? new List<object>())
		{
			if (item is string text)
			{
				string text2 = Utils.ReplacePlaceholders(text, placeholders);
				if (!Utils.HasPlaceholder(text2))
				{
					list.Add(text2);
				}
			}
			else
			{
				if (!(item is PyDict pyDict) || !Utils.CheckRules(pyDict.Get("rules"), features))
				{
					continue;
				}
				object obj = pyDict.Get("value", new List<object>());
				foreach (object item2 in (obj is List<object> list2) ? list2 : new List<object> { obj })
				{
					string text3 = Utils.ReplacePlaceholders(Py.AsStr(item2, "replace"), placeholders);
					if (!Utils.HasPlaceholder(text3))
					{
						list.Add(text3);
					}
				}
			}
		}
		return _drop_orphan_jvm_flags(list);
	}

	private static List<string> _drop_orphan_jvm_flags(List<string> args)
	{
		List<string> list = new List<string>();
		bool flag = false;
		for (int i = 0; i < args.Count; i++)
		{
			if (flag)
			{
				flag = false;
				continue;
			}
			string item = args[i];
			if (_JVM_VALUE_FLAGS.Contains(item))
			{
				string text = ((i + 1 < args.Count) ? args[i + 1] : "");
				if (text.Length != 0 && !text.StartsWith("-"))
				{
					list.Add(item);
					list.Add(text);
					flag = true;
				}
			}
			else
			{
				list.Add(item);
			}
		}
		return list;
	}

	private static string _coerce_java_exe(PyDict resolved, object java_exe)
	{
		long num = Java.RequiredJavaMajor(resolved);
		if (Java.JavaUsableFor(resolved, java_exe))
		{
			return Py.Str(java_exe);
		}
		string text = Java.PickJavaForVersion(resolved);
		if (Java.JavaUsableFor(resolved, text))
		{
			return text;
		}
		foreach (PyDict item in Java.ListInstalledJavas().Concat<object>(Java.FindSystemJavas()))
		{
			object obj = item.Get("exe");
			if (Java.JavaUsableFor(resolved, obj))
			{
				return Py.Str(obj);
			}
		}
		long? num2 = (Py.Truthy(java_exe) ? Java.GetJavaMajor(java_exe) : ((long?)null));
		throw new LaunchError("Java " + ((num2.HasValue && num2.Value != 0L) ? num2.Value.ToString() : "?") + " 无法启动此版本（需要 Java " + num + "+）。Forge 1.17+ 会向 JVM 传入 --module-path，Java 8 会报 Unrecognized option: -p。请到「Java」页下载 Java 17，启动页 Java 选「自动选择」。");
	}

	private static string Basename(string p)
	{
		int num = (Utils.IS_WINDOWS ? p.LastIndexOfAny(new char[2] { '/', '\\' }) : p.LastIndexOf('/'));
		if (num >= 0)
		{
			return p.Substring(num + 1);
		}
		return p;
	}

	private static List<string> _patch_ignore_list(List<string> jvm_args, List<string> filenames)
	{
		List<string> list = new List<string>();
		foreach (string item in filenames ?? new List<string>())
		{
			string text = Basename(Py.Strip(item ?? ""));
			if (text.Length > 0 && !list.Contains(text))
			{
				list.Add(text);
			}
		}
		if (list.Count == 0)
		{
			return jvm_args;
		}
		List<string> list2 = new List<string>();
		foreach (string jvm_arg in jvm_args)
		{
			string text2 = jvm_arg;
			if (text2.StartsWith("-DignoreList="))
			{
				List<string> list3 = (from p in Py.Split(text2, "=", 1)[1].Split(',')
					where p.Length > 0
					select p).ToList();
				foreach (string name in list)
				{
					if (!list3.Any((string p) => name.StartsWith(p, StringComparison.Ordinal)))
					{
						list3.Add(name);
					}
				}
				text2 = "-DignoreList=" + string.Join(",", list3);
			}
			list2.Add(text2);
		}
		return list2;
	}

	private static List<string> _ignore_list_jars(string jar, PyDict resolved, List<string> classpath_files)
	{
		List<string> list = new List<string>();
		list.Add(new PPath(jar).Name);
		List<string> list2 = list;
		object o = Py.Or(resolved.Get("inheritsFrom"), resolved.Get("jar"));
		if (Py.Truthy(o))
		{
			list2.Add(Py.Str(o) + ".jar");
		}
		foreach (string item in classpath_files ?? new List<string>())
		{
			string text = Basename(item);
			if (text.EndsWith("-extra.jar"))
			{
				list2.Add(text);
			}
		}
		return list2;
	}

	private static List<string> _apply_memory(List<string> jvm_args, object memory_mb)
	{
		long a = Math.Max(512L, Py.Int(Py.Or(memory_mb, 0L)));
		long num = Math.Min(PyFloorDiv(a, 2L), 1024L);
		List<string> list = jvm_args.Where((string text) => !text.StartsWith("-Xmx") && !text.StartsWith("-Xms")).ToList();
		list.Add("-Xmx" + a + "M");
		list.Add("-Xms" + num + "M");
		return list;
	}

	private static long PyFloorDiv(long a, long b)
	{
		long num = a / b;
		if (a % b != 0L && a < 0 != b < 0)
		{
			num--;
		}
		return num;
	}

	internal static string ClientJarPath(Instance instance, string version_id, PyDict vjson, PyDict resolved)
	{
		string text = PPath.Join(instance.VersionsDir(), version_id);
		string text2 = PPath.Join(text, version_id + ".jar");
		if (new PPath(text2).IsFile())
		{
			return text2;
		}
		object[] array = new object[4]
		{
			vjson.Get("jar"),
			resolved.Get("jar"),
			vjson.Get("inheritsFrom"),
			resolved.Get("inheritsFrom")
		};
		foreach (object o in array)
		{
			if (Py.Truthy(o))
			{
				string text3 = PPath.Join(instance.VersionsDir(), Py.Str(o), Py.Str(o) + ".jar");
				if (new PPath(text3).IsFile())
				{
					return text3;
				}
			}
		}
		return text2;
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern IntPtr OpenProcess(int access, bool inherit, int pid);

	[DllImport("kernel32.dll")]
	private static extern bool SetPriorityClass(IntPtr h, int cls);

	[DllImport("kernel32.dll")]
	private static extern bool CloseHandle(IntPtr h);

	[DllImport("user32.dll")]
	private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lparam);

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

	[DllImport("user32.dll")]
	private static extern bool IsWindowVisible(IntPtr hwnd);

	[DllImport("user32.dll")]
	private static extern bool IsWindow(IntPtr hwnd);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int GetWindowTextLengthW(IntPtr hwnd);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int GetClassNameW(IntPtr hwnd, StringBuilder buf, int max);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern bool SetWindowTextW(IntPtr hwnd, string text);

	internal static void _set_priority(int pid, string level)
	{
		if (!Utils.IS_WINDOWS || pid == 0)
		{
			return;
		}
		int num;
		switch (Py.Lower(level ?? "normal"))
		{
		default:
			return;
		case "low":
			num = 64;
			break;
		case "below":
			num = 16384;
			break;
		case "normal":
			num = 32;
			break;
		case "high":
			num = 128;
			break;
		case "realtime":
			num = 256;
			break;
		}
		if (num == 32)
		{
			return;
		}
		try
		{
			IntPtr intPtr = OpenProcess(512, inherit: false, pid);
			if (intPtr == IntPtr.Zero)
			{
				return;
			}
			try
			{
				SetPriorityClass(intPtr, num);
			}
			finally
			{
				CloseHandle(intPtr);
			}
		}
		catch (Exception)
		{
		}
	}

	private static List<IntPtr> _visible_windows_of(int pid)
	{
		List<KeyValuePair<bool, IntPtr>> hits = new List<KeyValuePair<bool, IntPtr>>();
		EnumWindows(delegate(IntPtr hwnd, IntPtr _)
		{
			GetWindowThreadProcessId(hwnd, out var pid2);
			if (pid2 != (uint)pid || !IsWindowVisible(hwnd))
			{
				return true;
			}
			if (GetWindowTextLengthW(hwnd) <= 0)
			{
				return true;
			}
			StringBuilder stringBuilder = new StringBuilder(64);
			GetClassNameW(hwnd, stringBuilder, 64);
			hits.Add(new KeyValuePair<bool, IntPtr>(_GAME_WINDOW_CLASSES.Contains<string>(stringBuilder.ToString()), hwnd));
			return true;
		}, IntPtr.Zero);
		return (from x in hits.Select((KeyValuePair<bool, IntPtr> h, int i) => new { h, i })
			orderby (!x.h.Key) ? 1 : 0, x.i
			select x.h.Value).ToList();
	}

	public static void WatchWindowTitle(Process proc, string title, double timeout = 90.0)
	{
		title = Py.Strip(title ?? "");
		if (!Utils.IS_WINDOWS || title.Length == 0 || proc == null)
		{
			return;
		}
		Thread thread = new Thread((ThreadStart)delegate
		{
			try
			{
				double num = Py.Time() + timeout;
				int id = proc.Id;
				IntPtr intPtr = IntPtr.Zero;
				while (Py.Time() < num && !proc.HasExited)
				{
					if (intPtr == IntPtr.Zero || !IsWindow(intPtr))
					{
						List<IntPtr> list = _visible_windows_of(id);
						intPtr = ((list.Count > 0) ? list[0] : IntPtr.Zero);
					}
					if (intPtr != IntPtr.Zero)
					{
						SetWindowTextW(intPtr, title);
					}
					Thread.Sleep(1000);
				}
			}
			catch (Exception)
			{
			}
		});
		thread.IsBackground = true;
		thread.Start();
	}

	private static string _offline_skin_api(PyDict props)
	{
		if (!Py.Truthy(Py.DictOr(props).Get("skin_file")))
		{
			return "";
		}
		try
		{
			return OfflineSkinApiHook?.Invoke(props) ?? "";
		}
		catch (Exception)
		{
			return "";
		}
	}

	private static List<string> _loader_versions(PyDict resolved)
	{
		List<string> list = new List<string>();
		foreach (PyDict item in Installer.Libs(resolved))
		{
			string text = Py.Str(Py.Or(item.Get("name"), ""));
			string[] lOADER_LIB_PREFIXES = _LOADER_LIB_PREFIXES;
			foreach (string text2 in lOADER_LIB_PREFIXES)
			{
				if (text.StartsWith(text2))
				{
					list.Add(text.Substring(text2.Length));
				}
			}
		}
		return list;
	}

	private static bool _remapped_jar_ready(string gdir, List<string> versions)
	{
		string[] array = new string[2]
		{
			PPath.Join(gdir, ".fabric", "remappedJars"),
			PPath.Join(gdir, ".quilt", "remappedJars")
		};
		foreach (string text in array)
		{
			if (!new PPath(text).IsDir())
			{
				continue;
			}
			foreach (string item in Directory.EnumerateFileSystemEntries(Utils.AbsPath(text)))
			{
				string n = Path.GetFileName(item);
				if (versions.Count > 0 && !versions.Any((string v) => n.EndsWith("-" + v)))
				{
					continue;
				}
				try
				{
					if (Directory.Exists(item) && Directory.EnumerateFiles(item, "*-intermediary.jar").Any())
					{
						return true;
					}
				}
				catch (Exception)
				{
				}
			}
		}
		return false;
	}

	private static string _remap_safe_game_dir(string game_dir, string main_class, PyDict resolved)
	{
		string text = PPath.Norm(game_dir);
		if (!_KNOT_MAIN_CLASSES.Any((string p) => (main_class ?? "").StartsWith(p)))
		{
			return text;
		}
		if (!Utils.VolumeLookupBroken(text))
		{
			return text;
		}
		if (_remapped_jar_ready(text, _loader_versions(resolved)))
		{
			return text;
		}
		string text2 = Utils.UncTwin(text);
		if (string.IsNullOrEmpty(text2))
		{
			return text;
		}
		Utils.log.Warning("系统查不出 %s 属于哪个卷，本次用 %s 启动，否则加载器生成不了重映射 jar", text, text2);
		return text2;
	}

	public static LaunchCommand BuildLaunchCommand(Instance instance, string version_id, PyDict account_props, object java_exe, object memory_mb = null, object width = null, object height = null, IEnumerable<object> extra_game_args = null, object extra_jvm_args = null, string game_directory = null, object authlib_api = null)
	{
		memory_mb = memory_mb ?? ((object)4096L);
		PyDict pyDict = instance.VersionJson(version_id) as PyDict;
		if (!Py.Truthy(pyDict))
		{
			throw new LaunchError("版本 " + version_id + " 未安装，请先安装。");
		}
		PyDict pyDict2;
		try
		{
			pyDict2 = Manifest.ResolveInherits(pyDict, loadParent);
		}
		catch (DownloadError e)
		{
			throw new LaunchError(Py.ExcStr(e));
		}
		string text = _coerce_java_exe(pyDict2, java_exe);
		string version_dir = PPath.Join(instance.VersionsDir(), version_id);
		string text2 = ClientJarPath(instance, version_id, pyDict, pyDict2);
		if (!new PPath(text2).IsFile())
		{
			throw new LaunchError("客户端 jar 缺失: " + text2 + "\n请重新安装版本 " + version_id + "。");
		}
		string text3 = Py.Str(Py.DictOr(Py.Or(pyDict2.Get("assetIndex"), new PyDict())).Get("id", "legacy"));
		string text4 = instance.AssetsDir();
		string text5 = instance.LibrariesDir();
		string text6 = Installer.ExtractNatives(instance, pyDict2, version_id);
		if (Installer.Libs(pyDict2).Where(delegate(PyDict lib)
		{
			object obj5 = lib.Get("clientreq");
			return (!(obj5 is bool) || (bool)obj5) && Utils.CheckRules(lib.Get("rules"));
		}).Any((PyDict lib) => !string.IsNullOrEmpty(Installer.SelectNativeClassifier(lib))) && !Installer.NativesPresent(text6))
		{
			throw new LaunchError("缺少 LWJGL 本地库（natives）。请重新安装该 Minecraft 版本后再启动。");
		}
		Dictionary<string, string> cpById = new Dictionary<string, string>();
		List<string> list = new List<string>();
		foreach (PyDict item in Installer.Libs(pyDict2))
		{
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
			PyDict pyDict3 = Py.DictOr(Py.Or(item.Get("downloads"), new PyDict())).Get("artifact") as PyDict;
			string value;
			if (Py.Truthy(pyDict3) && Py.Truthy(pyDict3.Get("path")))
			{
				value = PPath.Join(text5, Py.Str(pyDict3["path"]));
			}
			else
			{
				if (Py.Truthy(item.Get("natives")))
				{
					continue;
				}
				value = PPath.Join(text5, Utils.MavenArtifactPath(Py.Str(o)));
			}
			string text7 = Manifest.LibraryIdentity(item);
			if (!cpById.ContainsKey(text7))
			{
				list.Add(text7);
			}
			cpById[text7] = value;
		}
		List<string> list2 = list.Select((string k) => cpById[k]).ToList();
		list2.Add(text2);
		string text8 = Path.PathSeparator.ToString();
		string text9 = string.Join(text8, list2);
		string text10 = Py.Str(Py.Or(pyDict2.Get("mainClass"), "net.minecraft.client.main.Main"));
		PyDict pyDict4 = account_props ?? new PyDict();
		string text11 = Py.Str(Py.Or(Utils.DashedUuid(Py.Str(Py.Or(pyDict4.Get("uuid"), ""))), "00000000-0000-0000-0000-000000000000"));
		object obj2 = Py.Or(pyDict4.Get("token"), "0");
		PyDict placeholders = PyDict.Of("auth_player_name", pyDict4.Get("name", "Player"), "auth_uuid", text11, "auth_access_token", obj2, "auth_session", "token:" + Py.Str(obj2) + ":" + text11, "user_type", pyDict4.Get("user_type", "legacy"), "user_properties", "{}", "auth_xuid", pyDict4.Get("xuid", ""), "clientid", Config.MsClientId(), "version_name", version_id, "version_type", _version_type(version_id, pyDict2), "game_directory", _remap_safe_game_dir(Py.Truthy(game_directory) ? game_directory : instance.path, text10, pyDict2), "assets_root", text4, "assets_index_name", text3, "game_assets", PPath.Join(text4, "virtual", text3), "natives_directory", text6, "classpath", text9, "library_directory", text5, "classpath_separator", text8, "launcher_name", "HelL", "launcher_version", AppInfo.LAUNCHER_VERSION, "resolution_width", Py.Str(Py.Or(width, 854L)), "resolution_height", Py.Str(Py.Or(height, 480L)));
		PyDict features = PyDict.Of("is_demo_user", false, "has_custom_resolution", Py.Truthy(width) || Py.Truthy(height));
		List<string> list4;
		string text12;
		List<string> list3;
		if (Py.Truthy(pyDict2.Get("arguments")))
		{
			PyDict obj3 = (PyDict)pyDict2["arguments"];
			list3 = _expand_args(obj3.Get("jvm", new List<object>()), placeholders, features);
			list4 = _expand_args(obj3.Get("game", new List<object>()), placeholders, features);
			if (!list3.Any((string a) => a.StartsWith("-Djava.library.path")))
			{
				list3.Insert(0, "-Djava.library.path=" + text6);
			}
			if (!list3.Contains("-cp") && !list3.Contains("--class-path"))
			{
				list3.Add("-cp");
				list3.Add(text9);
			}
			text12 = "";
		}
		else
		{
			text12 = Py.Str(Py.Or(pyDict2.Get("minecraftArguments"), ""));
			List<string> source;
			try
			{
				source = ArgSplit.ShlexSplit(text12, escape: false);
			}
			catch (ValueError)
			{
				source = (from a in text12.Split(' ')
					where a.Length > 0
					select a).ToList();
			}
			list4 = (from a in source
				select Utils.ReplacePlaceholders(a, placeholders) into a
				where !Utils.HasPlaceholder(a)
				select a).ToList();
			list3 = new List<string>
			{
				"-Djava.library.path=" + text6,
				"-cp",
				text9
			};
		}
		if (list3.Any((string a) => a.StartsWith("-DignoreList=")))
		{
			list3 = _patch_ignore_list(list3, _ignore_list_jars(text2, pyDict2, list2));
		}
		if (text12.Contains("tweakClass") || list4.Any((string a) => a.Contains("tweakClass")))
		{
			list3.Add("-Dfml.ignoreInvalidMinecraftCertificates=true");
			list3.Add("-Dfml.ignorePatchDiscrepancies=true");
		}
		if (Manifest.IsLegacyVersion(pyDict2))
		{
			memory_mb = Math.Min(Py.Int(Py.Or(memory_mb, 1024L)), 1024L);
		}
		list3 = list3.Select((string a) => (a == "-p") ? "--module-path" : a).ToList();
		list3 = _apply_memory(list3, memory_mb);
		if (!list3.Any((string a) => a.Contains("-Dminecraft.launcher.brand")))
		{
			list3.Add("-Dminecraft.launcher.brand=HelL");
		}
		if (!list3.Any((string a) => a.Contains("-Dminecraft.launcher.version")))
		{
			list3.Add("-Dminecraft.launcher.version=" + AppInfo.LAUNCHER_VERSION);
		}
		PyDict pyDict5 = Py.DictOr(Py.Or(pyDict2.Get("logging"), new PyDict())).Get("client") as PyDict;
		if (Py.Truthy(pyDict5) && Py.Truthy(Py.DictOr(Py.Or(pyDict5.Get("file"), new PyDict())).Get("id")))
		{
			string text13 = PPath.Join(text4, "log_configs", Py.Str(((PyDict)pyDict5["file"])["id"]));
			if (new PPath(text13).IsFile() && !list3.Any((string a) => a.Contains("log4j.configurationFile")))
			{
				list3.Add("-Dlog4j.configurationFile=" + text13);
			}
		}
		List<string> list5 = new List<string>();
		foreach (object item2 in extra_game_args ?? new List<object>())
		{
			if (item2 != null && !(item2 is string { Length: 0 }))
			{
				list5.Add(Py.Str(item2));
			}
		}
		List<string> list6 = new List<string>();
		if (Py.Truthy(extra_jvm_args))
		{
			if (extra_jvm_args is IEnumerable enumerable && !(extra_jvm_args is string))
			{
				foreach (object item3 in enumerable)
				{
					list6.Add(Py.Str(item3));
				}
			}
			else
			{
				list6 = ArgSplit.SplitArgs(extra_jvm_args);
			}
		}
		list3 = ArgSplit.SplitArgs(Py.Or(Config.CONFIG.Get("default_jvm_args"), "")).Concat<string>(list6).Concat(list3)
			.ToList();
		list3 = _apply_memory(list3, memory_mb);
		if (!Py.Truthy(authlib_api))
		{
			authlib_api = _offline_skin_api(pyDict4);
		}
		if (Py.Truthy(authlib_api))
		{
			string text15 = Authlib.JavaagentArg(authlib_api);
			list3 = new string[1] { text15 }.Concat<string>(list3.Where((string a) => !a.StartsWith("-javaagent:"))).ToList();
		}
		object obj4 = pyDict4.Get("nide8_id");
		if (Py.Truthy(obj4))
		{
			string text16 = Nide8.JavaagentArg(obj4);
			list3 = new string[1] { text16 }.Concat<string>(list3.Where((string a) => !a.StartsWith("-javaagent:"))).ToList();
		}
		List<string> list7 = new List<string> { text };
		list7.AddRange(list3);
		list7.Add(text10);
		list7.AddRange(list4);
		list7.AddRange(list5);
		long? javaMajor = Java.GetJavaMajor(text);
		if (javaMajor.HasValue && javaMajor.Value < 9 && list7.Any((string a) => a == "-p" || a == "--module-path" || a == "--add-modules"))
		{
			throw new LaunchError("拒绝用 Java " + javaMajor.Value + " 启动：命令含模块参数。请改用 Java 17。");
		}
		return new LaunchCommand
		{
			cmd = list7,
			natives_dir = text6,
			version_dir = version_dir,
			game_dir = (Py.Truthy(game_directory) ? game_directory : instance.path)
		};
		PyDict loadParent(string pid)
		{
			PyDict pyDict6 = instance.VersionJson(pid) as PyDict;
			if (!Py.Truthy(pyDict6))
			{
				throw new LaunchError("缺少被继承的父版本 " + pid + "，请重新安装 " + version_id + "。");
			}
			return pyDict6;
		}
	}
}
