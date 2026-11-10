using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HelL.Core.Launcher.Ai;

internal static class AiFs
{
	public static List<string> RglobSorted(string dir)
	{
		List<string> outp = new List<string>();
		if (LocalFs.IsDir(dir))
		{
			Walk(dir);
		}
		outp.Sort(ComparePaths);
		return outp;
		void Walk(string d)
		{
			foreach (string item in LocalFs.Children(d))
			{
				outp.Add(item);
				if (LocalFs.IsDir(item) && !SingleRoot.IsSymlink(item))
				{
					Walk(item);
				}
			}
		}
	}

	public static int ComparePaths(string a, string b)
	{
		List<string> parts = new PPath(a).Parts;
		List<string> parts2 = new PPath(b).Parts;
		int num = Math.Min(parts.Count, parts2.Count);
		for (int i = 0; i < num; i++)
		{
			int num2 = (Utils.IS_WINDOWS ? string.CompareOrdinal(parts[i].ToLowerInvariant(), parts2[i].ToLowerInvariant()) : string.CompareOrdinal(parts[i], parts2[i]));
			if (num2 != 0)
			{
				return num2;
			}
		}
		return parts.Count.CompareTo(parts2.Count);
	}

	public static void WriteText(string path, string text)
	{
		if (Utils.IS_WINDOWS)
		{
			text = (text ?? "").Replace("\n", "\r\n");
		}
		File.WriteAllBytes(PPath.Abs(path), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text ?? ""));
	}

	public static string ReadText(string path, bool replace = false)
	{
		byte[] array = File.ReadAllBytes(PPath.Abs(path));
		return (replace ? Encoding.UTF8.GetString(array) : Utils.DecodeUtf8Strict(array)).Replace("\r\n", "\n").Replace("\r", "\n");
	}

	public static double MTime(string path)
	{
		string path2 = PPath.Abs(path);
		return (double)((Directory.Exists(path2) ? Directory.GetLastWriteTimeUtc(path2) : File.GetLastWriteTimeUtc(path2)) - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).Ticks / 10000000.0;
	}
}
