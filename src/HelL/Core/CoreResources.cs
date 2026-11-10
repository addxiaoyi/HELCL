using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using HelL.Core.Launcher;

namespace HelL.Core;

public static class CoreResources
{
	public const string Prefix = "HelL.Core.";

	public const string Suffix = "";

	private static string ResName(string rel)
	{
		return "HelL.Core." + rel.Replace('/', '.').Replace('\\', '.') + Suffix;
	}

	public static byte[] ReadBytes(string rel)
	{
		return ReadByResourceName(ResName(rel));
	}

	public static byte[] ReadByResourceName(string resName)
	{
		using Stream stream = typeof(CoreResources).Assembly.GetManifestResourceStream(resName);
		if (stream == null)
		{
			return null;
		}
		using MemoryStream memoryStream = new MemoryStream();
		stream.CopyTo(memoryStream);
		return memoryStream.ToArray();
	}

	public static string ReadText(string rel)
	{
		byte[] array = ReadBytes(rel);
		if (array != null)
		{
			return Utils.DecodeUtf8Strict(array);
		}
		return null;
	}

	public static List<string> ListDir(string relDir)
	{
		string p = "HelL.Core." + relDir.Replace('/', '.') + ".";
		return (from n in typeof(CoreResources).Assembly.GetManifestResourceNames()
			where n.StartsWith(p, StringComparison.Ordinal)
			select n.Substring(p.Length) into n
			where n.IndexOf('.') == n.LastIndexOf('.')
			select n).OrderBy<string, string>((string n) => n, StringComparer.Ordinal).ToList();
	}
}
