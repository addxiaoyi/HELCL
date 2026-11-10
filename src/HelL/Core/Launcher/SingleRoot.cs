using System;
using System.IO;

namespace HelL.Core.Launcher;

public static class SingleRoot
{
	public static bool IsLink(string path)
	{
		try
		{
			string text = PPath.Abs(path);
			FileSystemInfo fileSystemInfo = new FileInfo(text);
			if (!fileSystemInfo.Exists && Directory.Exists(text))
			{
				fileSystemInfo = new DirectoryInfo(text);
			}
			if (!fileSystemInfo.Exists && fileSystemInfo.LinkTargetOrNull() == null)
			{
				return false;
			}
			return (fileSystemInfo.Attributes & FileAttributes.ReparsePoint) != 0;
		}
		catch (Exception)
		{
			return false;
		}
	}

	public static bool IsSymlink(string path)
	{
		try
		{
			string path2 = PPath.Abs(path);
			if (!File.Exists(path2) && !Directory.Exists(path2))
			{
				return false;
			}
			return (File.GetAttributes(path2) & FileAttributes.ReparsePoint) != 0;
		}
		catch (Exception)
		{
			return false;
		}
	}

	public static bool DropLink(string path)
	{
		if (!IsLink(path))
		{
			return false;
		}
		string path2 = PPath.Abs(path);
		try
		{
			Directory.Delete(path2, recursive: false);
			return true;
		}
		catch (Exception)
		{
		}
		try
		{
			File.Delete(path2);
			return !File.Exists(path2) && !Directory.Exists(path2);
		}
		catch (Exception)
		{
		}
		return false;
	}

	private static string LinkTargetOrNull(this FileSystemInfo fi)
	{
		return null;
	}
}
