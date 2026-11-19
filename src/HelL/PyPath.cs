using System;
using System.IO;

namespace HelL;

internal static class PyPath
{
	public static string GetRelativePath(string relativeTo, string path)
	{
		try
		{
			string text = Path.GetFullPath(relativeTo);
			if (!text.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
			{
				text += Path.DirectorySeparatorChar;
			}
			string fullPath = Path.GetFullPath(path);
			return fullPath.StartsWith(text, StringComparison.OrdinalIgnoreCase) ? fullPath.Substring(text.Length) : path;
		}
		catch
		{
			return path;
		}
	}
}
