using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace HelL.Core.Launcher;

public static class VersionSettings
{
	public const string FILE_NAME = "tipcl.json";

	public const string ISOLATION_NONE = "none";

	public const string ISOLATION_SAVES = "saves";

	public const string ISOLATION_MODS = "mods";

	public const string ISOLATION_ALL = "all";

	public static readonly PyDict ISOLATION_LABELS = PyDict.Of("none", "大锅饭（与其他版本共用）", "saves", "隔离存档", "mods", "隔离 Mod 与配置", "all", "完全独立");

	public static readonly string[] SHARED_LINKS = new string[5] { "mods", "config", "resourcepacks", "shaderpacks", "downloads" };

	public static readonly string[] SAVES_LINKS = new string[1] { "saves" };

	public const string ISOLATED_DEFAULT = "all";

	public static readonly string[] SEED_DIRS = new string[4] { "mods", "config", "resourcepacks", "shaderpacks" };

	public static readonly string[] FULLSCREEN_MODES = new string[2] { "maximize", "fullscreen" };

	public static PyDict DEFAULTS => PyDict.Of("isolation", "none", "memory_mb", null, "java", "自动选择", "jvm_args", "", "game_args", "", "pre_launch", "", "post_launch", "", "pre_launch_wait", true, "server", "", "port", "", "process_priority", "normal", "icon", "", "hidden", false, "login_account", "", "auth_server", "", "auth_server_name", "", "nide8_id", "", "gc", "", "window_title", "", "window_mode", "window", "window_width", null, "window_height", null, "skip_assets", false, "offline_skin", "default");

	private static bool IsIsoLabel(object v)
	{
		if (v is string key)
		{
			return ISOLATION_LABELS.ContainsKey(key);
		}
		return false;
	}

	public static string _file(Instance instance, object version_id)
	{
		string name = Utils.SafeVersionId(version_id);
		string text = Utils.SafeChildPath(instance.VersionsDir(), name);
		return PPath.Join(text, "tipcl.json");
	}

	public static PyDict Load(Instance instance, object version_id)
	{
		PyDict dEFAULTS = DEFAULTS;
		if (Utils.ReadJson(_file(instance, version_id)) is PyDict other)
		{
			dEFAULTS.Update(other);
		}
		object obj = Py.Or(dEFAULTS["isolation"], Config.CONFIG.Get("default_isolation"), "none");
		if (!IsIsoLabel(obj))
		{
			obj = "none";
		}
		dEFAULTS["isolation"] = obj;
		return dEFAULTS;
	}

	public static PyDict Save(Instance instance, object version_id, object data)
	{
		PyDict pyDict = Load(instance, version_id);
		if (Py.Truthy(data))
		{
			if (!(data is PyDict other))
			{
				throw new ValueError("dictionary update sequence element #0 has length 1; 2 is required");
			}
			pyDict.Update(other);
		}
		if (!IsIsoLabel(pyDict["isolation"]))
		{
			pyDict["isolation"] = "none";
		}
		Utils.WriteJson(_file(instance, version_id), pyDict);
		return pyDict;
	}

	public static bool IsIsolated(PyDict settings)
	{
		object a = Py.Or((settings ?? new PyDict())["isolation"], "none");
		if (!Py.Eq(a, "mods"))
		{
			return Py.Eq(a, "all");
		}
		return true;
	}

	public static PyDict SetIsolation(Instance instance, object version_id, object mode, bool seed = false)
	{
		if (!IsIsoLabel(mode))
		{
			throw new ValueError(Py.Fmt("未知的隔离模式: {0!r}", mode));
		}
		PyDict settings = Load(instance, version_id);
		PyDict pyDict = Save(instance, version_id, PyDict.Of("isolation", mode));
		if (seed && IsIsolated(pyDict) && !IsIsolated(settings))
		{
			_seed_from_shared(instance, Py.Str(version_id));
		}
		ApplyIsolation(instance, version_id, pyDict);
		return pyDict;
	}

	private static void _seed_from_shared(Instance instance, string version_id)
	{
		string path = instance.path;
		string text = PPath.Join(instance.VersionsDir(), version_id);
		string[] sEED_DIRS = SEED_DIRS;
		foreach (string text2 in sEED_DIRS)
		{
			string text3 = PPath.Join(path, text2);
			if (!LocalFs.IsDir(text3))
			{
				continue;
			}
			string text4 = PPath.Join(text, text2);
			Utils.EnsureDir(text4);
			foreach (string item in LocalFs.Children(text3))
			{
				string text5 = PPath.Join(text4, new PPath(item).Name);
				if (LocalFs.Exists(text5) || SingleRoot.IsSymlink(item))
				{
					continue;
				}
				try
				{
					if (LocalFs.IsDir(item))
					{
						LocalFs.CopyTree(item, text5, dirsExistOk: true);
					}
					else
					{
						LocalFs.Copy2(item, text5);
					}
				}
				catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is OSError)
				{
				}
			}
		}
	}

	public static string GameDir(Instance instance, object version_id, PyDict settings = null)
	{
		Utils.SafeVersionId(version_id);
		settings = (Py.Truthy(settings) ? settings : Load(instance, version_id));
		object a = Py.Or(settings["isolation"], "none");
		if (Py.Eq(a, "all") || Py.Eq(a, "saves") || Py.Eq(a, "mods"))
		{
			return Utils.SafeChildPath(instance.VersionsDir(), version_id, "版本 ID");
		}
		return instance.path;
	}

	public static string ModsDir(Instance instance, object version_id, PyDict settings = null)
	{
		return PPath.Join(GameDir(instance, version_id, settings), "mods");
	}

	private static void _junction(string link, string target)
	{
		if (LocalFs.Exists(link) || SingleRoot.IsSymlink(link))
		{
			if (LocalFs.IsDir(link) && !SingleRoot.IsSymlink(link) && LocalFs.Children(link).Count > 0)
			{
				return;
			}
			try
			{
				if (SingleRoot.IsSymlink(link) || LocalFs.IsFile(link))
				{
					LocalFs.Unlink(link);
				}
				else if (LocalFs.IsDir(link) && LocalFs.Children(link).Count == 0)
				{
					Directory.Delete(PPath.Abs(link), recursive: false);
				}
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
			{
				return;
			}
		}
		Utils.EnsureDir(target);
		Utils.EnsureDir(new PPath(link).Parent.ToString());
		if (Utils.IS_WINDOWS)
		{
			_win_dir_link(link, target);
			return;
		}
		try
		{
			CreateDirSymlink(link, target);
		}
		catch (Exception)
		{
		}
	}

	private static void CreateDirSymlink(string link, string target)
	{
		if (!CreateSymbolicLinkW(PPath.Abs(link), target, 1u))
		{
			throw new IOException("symlink failed");
		}
	}

	private static void _win_dir_link(string link, string target)
	{
		if (!_try_symlink(link, target))
		{
			_create_junction(link, target);
		}
	}

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	[return: MarshalAs(UnmanagedType.U1)]
	private static extern bool CreateSymbolicLinkW(string lpSymlinkFileName, string lpTargetFileName, uint dwFlags);

	private static bool _try_symlink(string link, string target)
	{
		try
		{
			CreateDirSymlink(link, target);
			return true;
		}
		catch (Exception)
		{
		}
		if (!Utils.IS_WINDOWS)
		{
			return false;
		}
		try
		{
			return CreateSymbolicLinkW(PPath.Abs(link), target, 3u);
		}
		catch (Exception)
		{
			return false;
		}
	}

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr sa, uint disp, uint flags, IntPtr tmpl);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool DeviceIoControl(IntPtr h, uint code, byte[] inBuf, int inSize, IntPtr outBuf, int outSize, out int returned, IntPtr ov);

	[DllImport("kernel32.dll")]
	private static extern bool CloseHandle(IntPtr h);

	private static bool _create_junction(string link, string target)
	{
		if (!Utils.IS_WINDOWS)
		{
			return false;
		}
		try
		{
			string fullPath = Path.GetFullPath(PPath.Abs(target));
			byte[] bytes = Encoding.Unicode.GetBytes("\\??\\" + fullPath);
			byte[] bytes2 = Encoding.Unicode.GetBytes(fullPath);
			int num = 8 + bytes.Length + 2 + bytes2.Length + 2;
			int num2 = 8 + num;
			byte[] buf = new byte[num2];
			U(0, 2684354563u);
			U16(4, num);
			U16(6, 0);
			U16(8, 0);
			U16(10, bytes.Length);
			U16(12, bytes.Length + 2);
			U16(14, bytes2.Length);
			int num3 = 16;
			bytes.CopyTo(buf, num3);
			num3 += bytes.Length + 2;
			bytes2.CopyTo(buf, num3);
			Utils.EnsureDir(link);
			IntPtr intPtr = CreateFileW(PPath.Abs(link), 1073741824u, 0u, IntPtr.Zero, 3u, 35651584u, IntPtr.Zero);
			if (intPtr == IntPtr.Zero || intPtr == new IntPtr(-1))
			{
				return false;
			}
			try
			{
				int returned;
				return DeviceIoControl(intPtr, 589988u, buf, num2, IntPtr.Zero, 0, out returned, IntPtr.Zero);
			}
			finally
			{
				CloseHandle(intPtr);
			}
			void U(int off, uint v)
			{
				BitConverter.GetBytes(v).CopyTo(buf, off);
			}
			void U16(int off, int v)
			{
				BitConverter.GetBytes((ushort)v).CopyTo(buf, off);
			}
		}
		catch (Exception)
		{
			return false;
		}
	}

	public static string ApplyIsolation(Instance instance, object version_id, PyDict settings = null)
	{
		settings = (Py.Truthy(settings) ? settings : Load(instance, version_id));
		string text = GameDir(instance, version_id, settings);
		Utils.EnsureDir(text);
		object a = Py.Or(settings["isolation"], "none");
		if (Py.Eq(a, "saves"))
		{
			string[] sHARED_LINKS = SHARED_LINKS;
			foreach (string text2 in sHARED_LINKS)
			{
				_junction(PPath.Join(text, text2), PPath.Join(instance.path, text2));
			}
			Utils.EnsureDir(PPath.Join(text, "saves"));
		}
		else if (Py.Eq(a, "mods"))
		{
			foreach (string item in SAVES_LINKS.Concat<string>(new string[3] { "resourcepacks", "shaderpacks", "screenshots" }))
			{
				_junction(PPath.Join(text, item), PPath.Join(instance.path, item));
			}
			string[] sHARED_LINKS = new string[2] { "mods", "config" };
			foreach (string text3 in sHARED_LINKS)
			{
				Utils.EnsureDir(PPath.Join(text, text3));
			}
		}
		else if (Py.Eq(a, "all"))
		{
			foreach (string item2 in SHARED_LINKS.Concat<string>(SAVES_LINKS).Concat(new string[1] { "screenshots" }).Distinct())
			{
				SingleRoot.DropLink(PPath.Join(text, item2));
			}
			string[] sHARED_LINKS = new string[5] { "mods", "config", "saves", "resourcepacks", "shaderpacks" };
			foreach (string text4 in sHARED_LINKS)
			{
				Utils.EnsureDir(PPath.Join(text, text4));
			}
		}
		return text;
	}
}
