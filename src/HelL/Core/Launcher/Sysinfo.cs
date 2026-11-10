using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace HelL.Core.Launcher;

public static class Sysinfo
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

	private static readonly object _LOCK = new object();

	private static double _cacheT = 0.0;

	private static PyDict _cacheData = null;

	private const double _STATIC_TTL = 1800.0;

	private static readonly object _STATIC_LOCK = new object();

	private static PyDict _staticCpu = null;

	private static double _staticCpuT = 0.0;

	private static List<object> _staticGpus = null;

	private static double _staticGpusT = 0.0;

	private const string _NTCV = "SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion";

	private static readonly string[] _VIRTUAL_GPU = new string[8] { "virtual", "basic render", "basic display", "remote desktop", "mumu", "parsec", "spacedesk", "usb display" };

	public static string _run(IList<string> cmd, double timeout = 7.0)
	{
		try
		{
			using Process process = Process.Start(new ProcessStartInfo
			{
				FileName = cmd[0],
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true,
				Arguments = Utils.List2CmdLine(cmd.Skip(1).ToList())
			});
			MemoryStream memoryStream = new MemoryStream();
			MemoryStream memoryStream2 = new MemoryStream();
			Task task = process.StandardOutput.BaseStream.CopyToAsync(memoryStream);
			Task task2 = process.StandardError.BaseStream.CopyToAsync(memoryStream2);
			if (!process.WaitForExit((int)(timeout * 1000.0)))
			{
				try
				{
					process.Kill();
				}
				catch (Exception)
				{
				}
				return "";
			}
			try
			{
				Task.WaitAll(new Task[2] { task, task2 }, 2000);
			}
			catch (Exception)
			{
			}
			byte[] bytes = memoryStream.ToArray().Concat(memoryStream2.ToArray()).ToArray();
			return Py.Strip(Encoding.UTF8.GetString(bytes));
		}
		catch (Exception)
		{
			return "";
		}
	}

	public static string _ps(string script, double timeout = 8.0)
	{
		if (!Utils.IS_WINDOWS)
		{
			return "";
		}
		return _run(new List<string>
		{
			"powershell",
			"-NoProfile",
			"-NonInteractive",
			"-ExecutionPolicy",
			"Bypass",
			"-Command",
			"[Console]::OutputEncoding=[Text.UTF8Encoding]::new();$OutputEncoding=[Console]::OutputEncoding;" + script
		}, timeout);
	}

	public static object _ps_json(string script, double timeout = 8.0)
	{
		string text = _ps(script, timeout);
		if (text.Length == 0)
		{
			return null;
		}
		int num = text.IndexOf('[');
		int num2 = text.IndexOf('{');
		if (num < 0 && num2 < 0)
		{
			return null;
		}
		if (num < 0 || (num2 >= 0 && num2 < num))
		{
			num = num2;
		}
		try
		{
			return PyJson.Loads(text.Substring(num));
		}
		catch (Exception)
		{
			return null;
		}
	}

	public static string _winreg_value(string path, string name, string dflt = "")
	{
		if (!Utils.IS_WINDOWS)
		{
			return dflt;
		}
		try
		{
			using RegistryKey registryKey = Registry.LocalMachine.OpenSubKey(path);
			if (registryKey == null)
			{
				return dflt;
			}
			object value = registryKey.GetValue(name);
			if (value == null)
			{
				return dflt;
			}
			object obj = ((value is int num) ? ((object)(long)(uint)num) : ((value is long num2) ? ((object)num2) : value));
			string text = ((obj is string text2) ? text2 : Py.Str(obj));
			return Py.Truthy(obj) ? text : dflt;
		}
		catch (Exception)
		{
			return dflt;
		}
	}

	private static PyDict _cpu_info()
	{
		double num = Py.Time();
		lock (_STATIC_LOCK)
		{
			if (_staticCpu != null && num - _staticCpuT < 1800.0)
			{
				return new PyDict(_staticCpu);
			}
		}
		PyDict pyDict = _probe_cpu();
		lock (_STATIC_LOCK)
		{
			_staticCpu = pyDict;
			_staticCpuT = Py.Time();
		}
		return new PyDict(pyDict);
	}

	private static List<object> _gpu_info()
	{
		double num = Py.Time();
		lock (_STATIC_LOCK)
		{
			if (_staticGpus != null && num - _staticGpusT < 1800.0)
			{
				return new List<object>(_staticGpus);
			}
		}
		List<object> list = _probe_gpus();
		lock (_STATIC_LOCK)
		{
			_staticGpus = list;
			_staticGpusT = Py.Time();
		}
		return new List<object>(list);
	}

	[DllImport("libc")]
	private static extern IntPtr gnu_get_libc_version();

	private static string ReadProc(string p)
	{
		try
		{
			return Py.Strip(File.ReadAllText(p));
		}
		catch (Exception)
		{
			return "";
		}
	}

	public static string PlatSystem()
	{
		if (!Utils.IS_WINDOWS)
		{
			if (!Utils.IS_MAC)
			{
				return "Linux";
			}
			return "Darwin";
		}
		return "Windows";
	}

	public static string PlatMachine()
	{
		if (Utils.IS_WINDOWS)
		{
			return Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432") ?? Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") ?? "";
		}
		string text = _run(new List<string> { "uname", "-m" }, 3.0);
		if (text.Length > 0)
		{
			return text;
		}
		return RuntimeInformation.OSArchitecture switch
		{
			Architecture.X64 => "x86_64",
			Architecture.Arm64 => "aarch64",
			Architecture.X86 => "i686",
			_ => "",
		};
	}

	private static Version WinVer()
	{
		try
		{
			string oS_VERSION = Utils.OS_VERSION;
			return new Version((oS_VERSION.Split('.').Length >= 3) ? string.Join(".", oS_VERSION.Split('.').Take(3)) : oS_VERSION);
		}
		catch (Exception)
		{
			return Environment.OSVersion.Version;
		}
	}

	public static string PlatRelease()
	{
		if (Utils.IS_WINDOWS)
		{
			Version version = WinVer();
			if (version.Major >= 10 && version.Build >= 22000)
			{
				return "11";
			}
			if (version.Major >= 10 || (version.Major == 6 && version.Minor >= 4))
			{
				return "10";
			}
			if (version.Major == 6 && version.Minor == 3)
			{
				return "8.1";
			}
			if (version.Major == 6 && version.Minor == 2)
			{
				return "8";
			}
			if (version.Major == 6 && version.Minor == 1)
			{
				return "7";
			}
			return version.Major + "." + version.Minor;
		}
		return ReadProc("/proc/sys/kernel/osrelease");
	}

	public static string PlatVersion()
	{
		if (Utils.IS_WINDOWS)
		{
			Version version = WinVer();
			return $"{version.Major}.{version.Minor}.{version.Build}";
		}
		return ReadProc("/proc/sys/kernel/version");
	}

	public static string PlatProcessor()
	{
		if (Utils.IS_WINDOWS)
		{
			return Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "";
		}
		string text = _run(new List<string> { "uname", "-p" }, 3.0);
		if (!(text == "unknown"))
		{
			return text;
		}
		return "";
	}

	public static string PlatPlatform()
	{
		string source;
		if (Utils.IS_WINDOWS)
		{
			source = "Windows-" + PlatRelease() + "-" + PlatVersion() + "-SP0";
		}
		else
		{
			source = "Linux-" + PlatRelease() + "-" + PlatMachine();
			string text = "";
			try
			{
				text = Marshal.PtrToStringAnsi(gnu_get_libc_version()) ?? "";
			}
			catch (Exception)
			{
			}
			source += ((text.Length > 0) ? ("-with-glibc" + text) : "-with-libc");
		}
		source = new string(source.Select((char c) => (" /\\:;\"()".IndexOf(c) >= 0) ? '-' : c).ToArray());
		source = source.Replace("unknown", "");
		while (source.Contains("--"))
		{
			source = source.Replace("--", "-");
		}
		if (source.EndsWith("-"))
		{
			source = source.Substring(0, source.Length - 1);
		}
		return source;
	}

	private static PyDict _os_info()
	{
		string text = PlatPlatform();
		string text2 = PlatVersion();
		string text3 = PlatRelease();
		if (Utils.IS_WINDOWS)
		{
			string text4 = _winreg_value("SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion", "ProductName");
			string text5 = _winreg_value("SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion", "DisplayVersion");
			string text6 = (Py.Or(_winreg_value("SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion", "CurrentBuild"), _winreg_value("SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion", "CurrentBuildNumber")) as string) ?? "";
			string text7 = _winreg_value("SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion", "UBR");
			List<string> list = new List<string>();
			if (text4.Length > 0)
			{
				list.Add(text4);
			}
			if (text5.Length > 0)
			{
				list.Add(text5);
			}
			if (text6.Length > 0)
			{
				list.Add(text6 + ((text7.Length > 0) ? ("." + text7) : ""));
			}
			if (list.Count > 0)
			{
				text = string.Join(" ", list);
			}
			text2 = ((text6.Length > 0) ? text6 : text2);
			text3 = ((text5.Length > 0) ? text5 : text3);
		}
		return PyDict.Of("name", Utils.OS_NAME, "platform", PlatPlatform(), "system", PlatSystem(), "release", text3, "version", text2, "display", text, "arch", Utils.ARCH, "machine", PlatMachine());
	}

	private static PyDict _probe_cpu()
	{
		string text = PlatProcessor();
		if (Utils.IS_WINDOWS)
		{
			string text2 = _winreg_value("HARDWARE\\DESCRIPTION\\System\\CentralProcessor\\0", "ProcessorNameString", text);
			text = ((text2.Length > 0) ? text2 : text);
		}
		else if (!Utils.IS_MAC)
		{
			try
			{
				foreach (string item2 in Py.SplitLines(File.ReadAllText("/proc/cpuinfo")))
				{
					if (Py.Lower(item2).StartsWith("model name"))
					{
						text = Py.Strip(Py.Split(item2, ":", 1).Last());
						break;
					}
				}
			}
			catch (Exception)
			{
			}
		}
		long num = CpuCountLogical();
		long num2 = 0L;
		if (Utils.IS_WINDOWS)
		{
			object obj = _ps_json("Get-CimInstance Win32_Processor | Select-Object Name,NumberOfCores,NumberOfLogicalProcessors | ConvertTo-Json -Compress", 6.0);
			List<object> obj2 = ((obj is List<object> list) ? list : ((obj is PyDict item) ? new List<object> { item } : new List<object>()));
			long num3 = 0L;
			long num4 = 0L;
			foreach (object item3 in obj2)
			{
				if (item3 is PyDict pyDict)
				{
					try
					{
						num3 += Py.Int(Py.Or(pyDict["NumberOfCores"], 0L));
						num4 += Py.Int(Py.Or(pyDict["NumberOfLogicalProcessors"], 0L));
					}
					catch (Exception)
					{
					}
					if (Py.Truthy(pyDict["Name"]))
					{
						string text3 = Py.Strip(Py.Str(pyDict["Name"]));
						text = ((text3.Length > 0) ? text3 : text);
					}
				}
			}
			num2 = num3;
			if (num4 != 0L)
			{
				num = num4;
			}
		}
		else if (!Utils.IS_MAC)
		{
			num2 = CpuCountPhysical();
		}
		return PyDict.Of("name", string.Join(" ", Py.SplitWs(text ?? "")), "cores_logical", num, "cores_physical", num2);
	}

	public static List<object> _prefer_physical_gpus(List<object> gpus)
	{
		return Py.Sorted(gpus, delegate(object g)
		{
			string name = Py.Lower(Py.S((g as PyDict)?["name"]));
			return (long)(_VIRTUAL_GPU.Any((string k) => name.Contains(k)) ? 1 : 0);
		});
	}

	private static List<object> _probe_gpus()
	{
		List<object> list = new List<object>();
		if (Utils.IS_WINDOWS)
		{
			object obj = _ps_json("Get-CimInstance Win32_VideoController | Where-Object { $_.Name } | Select-Object Name,DriverVersion,AdapterRAM,PNPDeviceID | ConvertTo-Json -Compress");
			foreach (object item2 in (obj is List<object> list2) ? list2 : ((obj is PyDict item) ? new List<object> { item } : new List<object>()))
			{
				if (!(item2 is PyDict pyDict))
				{
					continue;
				}
				string text = Py.Strip(Py.Str(Py.Or(pyDict["Name"], "")));
				if (text.Length != 0)
				{
					long num = 0L;
					try
					{
						num = Py.Int(Py.Or(pyDict["AdapterRAM"], 0L));
					}
					catch (Exception)
					{
					}
					list.Add(PyDict.Of("name", text, "driver", Py.Str(Py.Or(pyDict["DriverVersion"], "")), "vram_mb", (num > 0) ? (num / 1048576) : 0, "pnp", Py.Str(Py.Or(pyDict["PNPDeviceID"], ""))));
				}
			}
			return _prefer_physical_gpus(list);
		}
		string text2 = _run(new List<string> { "lspci", "-mm" }, 5.0);
		if (text2.Length == 0)
		{
			text2 = _run(new List<string> { "lspci" }, 5.0);
		}
		foreach (string item3 in Py.SplitLines(text2))
		{
			string text3 = Py.Lower(item3);
			if (text3.Contains("vga") || text3.Contains("3d controller") || text3.Contains("display"))
			{
				string text4 = (item3.Contains(":") ? Py.Strip(Py.Split(item3, ":", 2).Last()) : Py.Strip(item3));
				list.Add(PyDict.Of("name", text4, "driver", "", "vram_mb", 0L));
			}
		}
		return _prefer_physical_gpus(list);
	}

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool GetDiskFreeSpaceExW(string dir, out ulong availToCaller, out ulong total, out ulong totalFree);

	private static (long total, long free) DiskUsage(string path)
	{
		if (Utils.IS_WINDOWS)
		{
			if (!GetDiskFreeSpaceExW(path, out var _, out var total, out var totalFree))
			{
				throw new OSError("disk_usage");
			}
			return (total: (long)total, free: (long)totalFree);
		}
		if (!Directory.Exists(path) && !File.Exists(path))
		{
			throw new OSError("disk_usage");
		}
		string fullPath = Path.GetFullPath(path);
		DriveInfo driveInfo = null;
		DriveInfo[] drives = DriveInfo.GetDrives();
		foreach (DriveInfo driveInfo2 in drives)
		{
			string fullName;
			try
			{
				fullName = driveInfo2.RootDirectory.FullName;
			}
			catch (Exception)
			{
				continue;
			}
			string value = (fullName.EndsWith("/") ? fullName : (fullName + "/"));
			if (((fullPath + "/").StartsWith(value) || fullPath == fullName) && (driveInfo == null || fullName.Length > driveInfo.RootDirectory.FullName.Length))
			{
				driveInfo = driveInfo2;
			}
		}
		if (driveInfo == null)
		{
			throw new OSError("disk_usage");
		}
		return (total: driveInfo.TotalSize, free: driveInfo.AvailableFreeSpace);
	}

	private static string Anchor(string p)
	{
		if (Utils.IS_WINDOWS)
		{
			if (p.StartsWith("\\\\"))
			{
				string[] array = p.Substring(2).Split('\\');
				if (array.Length < 2)
				{
					return p;
				}
				return "\\\\" + array[0] + "\\" + array[1] + "\\";
			}
			if (p.Length >= 2 && p[1] == ':')
			{
				if (p.Length < 3 || (p[2] != '\\' && p[2] != '/'))
				{
					return p.Substring(0, 2);
				}
				return p.Substring(0, 2) + "\\";
			}
			if (!p.StartsWith("\\") && !p.StartsWith("/"))
			{
				return "";
			}
			return "\\";
		}
		if (!p.StartsWith("//") || p.StartsWith("///"))
		{
			if (!p.StartsWith("/"))
			{
				return "";
			}
			return "/";
		}
		return "//";
	}

	private static List<object> _disk_info()
	{
		List<object> list = new List<object>();
		HashSet<string> hashSet = new HashSet<string>();
		string text = PPath.Norm(Utils.ROOT);
		List<string> list2 = new List<string> { text };
		if (Utils.IS_WINDOWS)
		{
			string text2 = Anchor(text);
			if (text2.Length > 0)
			{
				list2.Add(text2);
			}
			list2.Add("C:\\");
		}
		else
		{
			list2.Add("/");
			string text3 = PPath.Norm(Environment.GetEnvironmentVariable("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
			if (text3.Length > 0)
			{
				list2.Add(text3);
			}
		}
		foreach (string item in list2)
		{
			(long, long) tuple;
			try
			{
				tuple = DiskUsage(item);
			}
			catch (Exception)
			{
				continue;
			}
			string text4 = Anchor(item);
			string text5 = ((text4.Length > 0) ? text4 : item);
			if (!hashSet.Contains(text5))
			{
				hashSet.Add(text5);
				list.Add(PyDict.Of("path", text5, "total_gb", Py.Round((double)tuple.Item1 / Math.Pow(1024.0, 3.0), 1), "free_gb", Py.Round((double)tuple.Item2 / Math.Pow(1024.0, 3.0), 1)));
			}
		}
		return list;
	}

	[DllImport("user32.dll")]
	private static extern int GetSystemMetrics(int n);

	private static PyDict _display_info()
	{
		long num = 0L;
		long num2 = 0L;
		long num3 = 1L;
		if (Utils.IS_WINDOWS)
		{
			try
			{
				num = GetSystemMetrics(0);
				num2 = GetSystemMetrics(1);
				int systemMetrics = GetSystemMetrics(80);
				num3 = ((systemMetrics == 0) ? 1 : systemMetrics);
			}
			catch (Exception)
			{
			}
		}
		return PyDict.Of("width", num, "height", num2, "screens", num3);
	}

	private static List<object> _java_info(bool scanSystem)
	{
		List<object> list = new List<object>();
		try
		{
			foreach (PyDict item in (scanSystem ? Java.AllJavas() : Java.ListInstalledJavas()).Take(16))
			{
				list.Add(PyDict.Of("name", Py.Or(item["name"], ""), "major", item["major"], "path", Py.Or(item["exe"], item["path"], "")));
			}
		}
		catch (Exception)
		{
		}
		return list;
	}

	private static List<object> _instance_info()
	{
		List<object> list = new List<object>();
		try
		{
			foreach (string item in Instances.ListInstances().Take(24))
			{
				Instance instance = new Instance(item);
				List<object> list2 = instance.InstalledIds().Take(16).ToList();
				long num = 0L;
				try
				{
					num = Mods.ListInstanceMods(instance).Count;
				}
				catch (Exception)
				{
				}
				list.Add(PyDict.Of("name", item, "versions", list2, "mod_count", num, "java", instance.JavaPref()));
			}
		}
		catch (Exception)
		{
		}
		return list;
	}

	private static object G(object d, string k)
	{
		if (d is PyDict pyDict)
		{
			return pyDict[k];
		}
		throw new AttributeError("'" + Py.TypeName(d) + "' object has no attribute 'get'");
	}

	private static object OrDict(object x)
	{
		if (!Py.Truthy(x))
		{
			return new PyDict();
		}
		return x;
	}

	public static string Summarize(object info)
	{
		object d = OrDict(G(info, "os"));
		string item = Py.Strip(Py.AsStr(Py.Or(G(OrDict(G(info, "cpu")), "name"), ""), "strip"));
		long num = Py.Int(Py.Or(G(OrDict(G(info, "memory")), "total_mb"), 0L));
		string item2 = ((num != 0L) ? (Py.RoundInt((double)num / 1024.0) + "GB") : "");
		object o = Py.Or(G(info, "gpus"), new List<object>());
		object item3 = "";
		if (Py.Truthy(o))
		{
			item3 = Py.Or(G(Idx0(o), "name"), "");
		}
		List<object> source = new List<object>
		{
			Py.Or(G(d, "display"), G(d, "platform"), ""),
			item,
			item2,
			item3
		};
		return JoinStr(" · ", source.Where(Py.Truthy));
	}

	public static PyDict Collect(bool force = false, bool scan_system_java = false, double max_age = 120.0)
	{
		double num = Py.Time();
		lock (_LOCK)
		{
			if (!force && _cacheData != null && _cacheData.Count > 0 && num - _cacheT < max_age)
			{
				return new PyDict(_cacheData);
			}
		}
		PyDict pyDict = PyDict.Of("collected_at", DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture), "hostname", HostName(), "os", _os_info(), "cpu", _cpu_info(), "memory", MemoryInfo(), "gpus", _gpu_info(), "disks", _disk_info(), "display", _display_info(), "java", _java_info(scan_system_java), "launcher", PyDict.Of("name", "HelL", "version", AppInfo.APP_VERSION, "frozen", true, "python", "", "root", PPath.Norm(Utils.ROOT), "memory_mb", Py.Int(Py.Or(Config.CONFIG.Get("memory_mb"), 0L)), "download_threads", Py.Int(Py.Or(Config.CONFIG.Get("download_threads"), 0L)), "download_source", Py.Or(Config.CONFIG.Get("download_source"), "auto"), "community_source", Py.Or(Config.CONFIG.Get("community_source"), "auto")), "instances", _instance_info());
		pyDict["summary"] = Summarize(pyDict);
		lock (_LOCK)
		{
			_cacheT = Py.Time();
			_cacheData = pyDict;
		}
		return new PyDict(pyDict);
	}

	private static string HostName()
	{
		try
		{
			return Dns.GetHostName() ?? "";
		}
		catch (Exception)
		{
			return "";
		}
	}

	private static string JoinStr(string sep, IEnumerable<object> items)
	{
		List<object> list = items.ToList();
		for (int i = 0; i < list.Count; i++)
		{
			if (!(list[i] is string))
			{
				throw new TypeError($"sequence item {i}: expected str instance, {Py.TypeName(list[i])} found");
			}
		}
		return string.Join(sep, list.Cast<string>());
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

	private static object Idx0(object o)
	{
		if (!(o is List<object> list))
		{
			string obj = o as string;
			if (obj == null)
			{
				if (o is PyDict)
				{
					throw new KeyError(0L);
				}
				throw new TypeError("'" + Py.TypeName(o) + "' object is not subscriptable");
			}
			return obj.Substring(0, 1);
		}
		return list[0];
	}

	private static string PathName(object x)
	{
		if (!(x is string text))
		{
			throw new TypeError("argument should be a str or an os.PathLike object where __fspath__ returns a str, not '" + Py.TypeName(x) + "'");
		}
		return new PPath(text).Name;
	}

	private static string F(object x)
	{
		return Py.Str(x);
	}

	public static string FormatText(object info = null)
	{
		object d = (Py.Truthy(info) ? info : Collect());
		List<object> list = new List<object> { Py.Or(G(d, "summary"), "") };
		object d2 = OrDict(G(d, "cpu"));
		object d3 = OrDict(G(d, "memory"));
		object d4 = OrDict(G(d, "display"));
		list.Add("CPU " + F(Py.Or(G(d2, "name"), "?")) + "  " + F(Py.Or(G(d2, "cores_physical"), "?")) + "C/" + F(Py.Or(G(d2, "cores_logical"), "?")) + "T");
		list.Add("内存 " + F(Py.Or(G(d3, "total_mb"), 0L)) + " MB  可用 " + F(Py.Or(G(d3, "avail_mb"), 0L)) + " MB  占用 " + F(Py.Or(G(d3, "load_percent"), 0L)) + "%");
		foreach (object item in Iter(Py.Or(G(d, "gpus"), new List<object>())))
		{
			string text = (Py.Truthy(G(item, "vram_mb")) ? ("  " + F(G(item, "vram_mb")) + " MB") : "");
			string text2 = (Py.Truthy(G(item, "driver")) ? ("  驱动 " + F(G(item, "driver"))) : "");
			list.Add("显卡 " + F(G(item, "name")) + text + text2);
		}
		if (!Py.Truthy(Py.Or(G(d, "gpus"), new List<object>())))
		{
			list.Add("显卡 未检测到");
		}
		if (Py.Truthy(G(d4, "width")))
		{
			list.Add("分辨率 " + F(G(d4, "width")) + "×" + F(G(d4, "height")) + "  屏幕 " + F(Py.Or(G(d4, "screens"), 1L)));
		}
		foreach (object item2 in Iter(Py.Or(G(d, "disks"), new List<object>())))
		{
			list.Add("磁盘 " + F(G(item2, "path")) + "  " + F(G(item2, "free_gb")) + " / " + F(G(item2, "total_gb")) + " GB 可用");
		}
		object o = Py.Or(G(d, "java"), new List<object>());
		if (Py.Truthy(o))
		{
			list.Add("Java " + string.Join(", ", from j in Iter(o).Take(6)
				select F(Py.Or(G(j, "major"), "?")) + "(" + PathName(Py.Or(G(j, "path"), "")) + ")"));
		}
		object d5 = OrDict(G(d, "launcher"));
		list.Add("启动器 " + F(G(d5, "version")) + "  Python " + F(G(d5, "python")) + "  内存默认 " + F(G(d5, "memory_mb")) + " MB");
		foreach (object item3 in Iter(Py.Or(G(d, "instances"), new List<object>())))
		{
			string text3 = JoinStr(", ", Iter(Py.Or(G(item3, "versions"), new List<object>())));
			if (text3.Length == 0)
			{
				text3 = "无版本";
			}
			list.Add("实例 " + F(G(item3, "name")) + "  " + text3 + "  mods=" + F(Py.Or(G(item3, "mod_count"), 0L)));
		}
		return JoinStr("\n", list.Where(Py.Truthy));
	}

	[DllImport("kernel32.dll")]
	private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX st);

	public static PyDict MemoryInfo()
	{
		long num = 0L;
		long num2 = 0L;
		long num3 = 0L;
		if (Utils.IS_WINDOWS)
		{
			try
			{
				MEMORYSTATUSEX st = new MEMORYSTATUSEX
				{
					dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX))
				};
				if (GlobalMemoryStatusEx(ref st))
				{
					num = (long)st.ullTotalPhys;
					num2 = (long)st.ullAvailPhys;
					num3 = st.dwMemoryLoad;
				}
			}
			catch (Exception)
			{
			}
		}
		else if (!Utils.IS_MAC)
		{
			try
			{
				PyDict pyDict = new PyDict();
				foreach (string item in Py.SplitLines(File.ReadAllText("/proc/meminfo")))
				{
					if (item.Contains(":"))
					{
						List<string> list = Py.Split(item, ":", 1);
						string text = new string(list[1].Where(char.IsDigit).ToArray());
						pyDict[list[0]] = ((text.Length > 0) ? long.Parse(text) : 0) * 1024;
					}
				}
				num = (long)pyDict.Get("MemTotal", 0L);
				num2 = (Py.Truthy(pyDict.Get("MemAvailable")) ? ((long)pyDict["MemAvailable"]) : ((long)pyDict.Get("MemFree", 0L)));
				if (num != 0L)
				{
					num3 = Py.RoundInt((double)(num - num2) * 100.0 / (double)num);
				}
			}
			catch (Exception)
			{
			}
		}
		return PyDict.Of("total_mb", (num != 0L) ? (num / 1048576) : 0, "avail_mb", (num2 != 0L) ? (num2 / 1048576) : 0, "load_percent", num3, "total_bytes", num, "avail_bytes", num2);
	}

	public static long CpuCountLogical()
	{
		if (!Utils.IS_WINDOWS && !Utils.IS_MAC)
		{
			try
			{
				long num = Py.SplitLines(File.ReadAllText("/proc/cpuinfo")).Count((string l) => l.StartsWith("processor"));
				if (num > 0)
				{
					return num;
				}
			}
			catch (Exception)
			{
			}
		}
		return Environment.ProcessorCount;
	}

	public static long CpuCountPhysical()
	{
		if (Utils.IS_WINDOWS || Utils.IS_MAC)
		{
			return 0L;
		}
		try
		{
			return (from l in Py.SplitLines(File.ReadAllText("/proc/cpuinfo"))
				where l.StartsWith("physical id")
				select Py.Strip(l.Split(':').Last())).Distinct().LongCount();
		}
		catch (Exception)
		{
			return 0L;
		}
	}

	public static PyDict GetSmartRecommendation()
	{
		PyDict pyDict = PyDict.Of("memory_mb", 4096L, "java_major", 17L, "window_width", 854L, "window_height", 480L, "gc_preset", "auto", "cpu_count", 4L, "total_ram_gb", 8.0);
		try
		{
			long num = Py.Int(Py.Or(MemoryInfo().Get("total_bytes"), 0L));
			if (num != 0L)
			{
				double num2 = (double)num / Math.Pow(1024.0, 3.0);
				long num3 = ((num2 >= 32.0) ? 12288 : ((num2 >= 16.0) ? 8192 : ((num2 >= 8.0) ? 4096 : 2048)));
				long num4 = (long)(num2 * 0.75 * 1024.0);
				if (num3 > num4)
				{
					num3 = Math.Max(1024L, num4);
				}
				pyDict["memory_mb"] = num3;
				pyDict["total_ram_gb"] = Py.Round(num2, 1);
			}
			long num5 = CpuCountLogical();
			pyDict["cpu_count"] = ((num5 != 0L) ? num5 : ((CpuCountPhysical() != 0L) ? CpuCountPhysical() : 4));
		}
		catch (Exception)
		{
		}
		return pyDict;
	}
}
