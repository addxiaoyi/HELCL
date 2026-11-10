using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HelL.Core.Launcher;

public static class LocalFs
{
	public static List<string> SortedChildren(string dir)
	{
		string path = PPath.Abs(dir);
		if (!Directory.Exists(path))
		{
			return new List<string>();
		}
		List<string> list = Directory.EnumerateFileSystemEntries(path).Select(Path.GetFileName).ToList();
		list.Sort((string a, string b) => Utils.IS_WINDOWS ? string.CompareOrdinal(a.ToLowerInvariant(), b.ToLowerInvariant()) : string.CompareOrdinal(a, b));
		return list.Select((string n) => PPath.Join(dir, n)).ToList();
	}

	public static bool IsFile(string p)
	{
		return File.Exists(PPath.Abs(p));
	}

	public static bool IsDir(string p)
	{
		return Directory.Exists(PPath.Abs(p));
	}

	public static bool Exists(string p)
	{
		string path = PPath.Abs(p);
		if (!File.Exists(path))
		{
			return Directory.Exists(path);
		}
		return true;
	}

	public static long Size(string p)
	{
		return new FileInfo(PPath.Abs(p)).Length;
	}

	public static List<string> Children(string dir)
	{
		string path = PPath.Abs(dir);
		if (!Directory.Exists(path))
		{
			return new List<string>();
		}
		return (from x in Directory.EnumerateFileSystemEntries(path)
			select PPath.Join(dir, Path.GetFileName(x))).ToList();
	}

	public static List<string> RglobFiles(string dir)
	{
		List<string> outp = new List<string>();
		if (IsDir(dir))
		{
			Walk(dir);
		}
		return outp;
		void Walk(string d)
		{
			List<string> list = new List<string>();
			foreach (string item in Children(d))
			{
				if (IsFile(item))
				{
					outp.Add(item);
				}
				if (IsDir(item) && !SingleRoot.IsSymlink(item))
				{
					list.Add(item);
				}
			}
			foreach (string item2 in list)
			{
				Walk(item2);
			}
		}
	}

	public static long MTimeInt(string p)
	{
		string path = PPath.Abs(p);
		return (long)Math.Floor(((Directory.Exists(path) ? Directory.GetLastWriteTimeUtc(path) : File.GetLastWriteTimeUtc(path)) - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds);
	}

	public static double MTime(string p)
	{
		string path = PPath.Abs(p);
		return (double)((Directory.Exists(path) ? Directory.GetLastWriteTimeUtc(path) : File.GetLastWriteTimeUtc(path)) - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).Ticks / 10000000.0;
	}

	public static void Unlink(string p)
	{
		File.Delete(PPath.Abs(p));
	}

	public static void Copy2(string src, string dst)
	{
		string text = PPath.Abs(src);
		string text2 = PPath.Abs(dst);
		if (Directory.Exists(text2))
		{
			text2 = Path.Combine(text2, Path.GetFileName(text));
		}
		File.Copy(text, text2, overwrite: true);
		try
		{
			File.SetLastWriteTimeUtc(text2, File.GetLastWriteTimeUtc(text));
		}
		catch (Exception)
		{
		}
	}

	public static void CopyTree(string src, string dst, bool dirsExistOk = false)
	{
		string path = PPath.Abs(src);
		string text = PPath.Abs(dst);
		if (!Directory.Exists(path))
		{
			throw FileNotFoundErrorFor(src);
		}
		if (!dirsExistOk && (Directory.Exists(text) || File.Exists(text)))
		{
			throw new FileExistsError("[Errno 17] File exists: " + Py.Repr(dst));
		}
		Directory.CreateDirectory(text);
		foreach (string item in Directory.EnumerateFileSystemEntries(path))
		{
			string fileName = Path.GetFileName(item);
			string dst2 = Path.Combine(text, fileName);
			if (Directory.Exists(item))
			{
				CopyTree(item, dst2, dirsExistOk);
			}
			else
			{
				Copy2(item, dst2);
			}
		}
		try
		{
			Directory.SetLastWriteTimeUtc(text, Directory.GetLastWriteTimeUtc(path));
		}
		catch (Exception)
		{
		}
	}

	private static OSError FileNotFoundErrorFor(string p)
	{
		return FileNotFoundError.ForPath(p);
	}
}
