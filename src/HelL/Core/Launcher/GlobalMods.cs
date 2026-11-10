using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace HelL.Core.Launcher;

public static class GlobalMods
{
	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool CreateSymbolicLinkW(string lpSymlinkFileName, string lpTargetFileName, int dwFlags);

	private static bool IsSymlink(string p)
	{
		try
		{
			FileInfo fileInfo = new FileInfo(p);
			return (fileInfo.Exists || Directory.Exists(p)) ? ((fileInfo.Attributes & FileAttributes.ReparsePoint) != 0) : ((File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0);
		}
		catch (Exception)
		{
			return false;
		}
	}

	public static bool _link_file(string dest, string src)
	{
		string text = Utils.AbsPath(dest);
		string text2 = Utils.AbsPath(src);
		if (File.Exists(text) || Directory.Exists(text) || IsSymlink(text))
		{
			return false;
		}
		Utils.EnsureDir(new PPath(dest).Parent.ToString());
		try
		{
			if (CreateSymbolicLinkW(text, text2, 2))
			{
				return true;
			}
			throw new IOException("symlink failed");
		}
		catch (Exception)
		{
			try
			{
				File.Copy(text2, text);
				File.SetLastWriteTimeUtc(text, File.GetLastWriteTimeUtc(text2));
				return true;
			}
			catch (Exception)
			{
				return false;
			}
		}
	}

	public static long Apply(string game_mods_dir)
	{
		string text = Root();
		Utils.EnsureDir(game_mods_dir);
		if (!LocalFs.IsDir(text))
		{
			return 0L;
		}
		long num = 0L;
		foreach (string item in LocalFs.SortedChildren(text))
		{
			string name = new PPath(item).Name;
			if (LocalFs.IsFile(item) && name.ToLowerInvariant().EndsWith(".jar") && _link_file(PPath.Join(game_mods_dir, name), item))
			{
				num++;
			}
		}
		return num;
	}

	public static string Root()
	{
		string text = Py.SS(Config.CONFIG.Get("global_mods_dir"));
		if (text.Length > 0)
		{
			return PPath.Norm(text);
		}
		return PPath.Join(Utils.ROOT, "shared", "mods");
	}

	public static List<object> ListEntries()
	{
		string text = Root();
		List<object> list = new List<object>();
		if (!LocalFs.IsDir(text))
		{
			return list;
		}
		foreach (string item in LocalFs.SortedChildren(text))
		{
			if (LocalFs.IsFile(item))
			{
				string name = new PPath(item).Name;
				string text2 = name.ToLowerInvariant();
				if (text2.EndsWith(".jar"))
				{
					list.Add(PyDict.Of("filename", name, "enabled", true, "bytes", LocalFs.Size(item)));
				}
				else if (text2.EndsWith(".jar.disabled") || text2.EndsWith(".disabled"))
				{
					list.Add(PyDict.Of("filename", name, "enabled", false, "bytes", LocalFs.Size(item)));
				}
			}
		}
		return list;
	}

	public static string SetEnabled(object filename, object enabled)
	{
		string text = Root();
		PPath pPath = new PPath(text) / Py.AsStr(filename, "__fspath__");
		if (!pPath.IsFile())
		{
			throw new FileNotFoundError(Py.Str(filename));
		}
		string name = pPath.Name;
		if (Py.Truthy(enabled))
		{
			if (name.ToLowerInvariant().EndsWith(".disabled"))
			{
				PPath pPath2 = pPath.WithName(name.Substring(0, name.Length - ".disabled".Length));
				if (pPath2.Exists())
				{
					throw new OSError("启用失败，已存在: " + pPath2.Name);
				}
				Utils.OsReplace(pPath.Abs(), pPath2.Abs());
				return pPath2.Name;
			}
			return name;
		}
		if (name.ToLowerInvariant().EndsWith(".disabled"))
		{
			return name;
		}
		PPath pPath3 = pPath.WithName(name + ".disabled");
		if (pPath3.Exists())
		{
			throw new OSError("禁用失败，已存在: " + pPath3.Name);
		}
		Utils.OsReplace(pPath.Abs(), pPath3.Abs());
		return pPath3.Name;
	}
}
