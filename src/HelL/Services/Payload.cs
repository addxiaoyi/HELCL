using System;
using System.IO;
using System.Linq;
using HelL.Core;

namespace HelL.Services;

internal static class Payload
{
	private static readonly object Gate = new object();

	private static string? _root;

	public static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HelL");

	public static string? ActiveRoot => _root;

	public static string? EmbeddedPython()
	{
		if (_root == null)
		{
			return null;
		}
		string text = Path.Combine(_root, "runtime", "python", "python.exe");
		if (!File.Exists(text))
		{
			return null;
		}
		return text;
	}

	internal static bool BackdoorRequested()
	{
		if (!string.Equals(Environment.GetEnvironmentVariable("TIPCL_BRIDGE"), "python", StringComparison.OrdinalIgnoreCase))
		{
			return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TIPCL_BRIDGE_EXE"));
		}
		return true;
	}

	public static string? EnsureRoot()
	{
		lock (Gate)
		{
			if (_root != null)
			{
				return _root;
			}
			string defaultRoot = DefaultRoot;
			try
			{
				Directory.CreateDirectory(defaultRoot);
			}
			catch (Exception)
			{
			}
			if (!BackdoorRequested())
			{
				CleanupLegacy(defaultRoot);
			}
			WriteLocales(defaultRoot);
			return _root = defaultRoot;
		}
	}

	internal static bool HasLegacy(string root)
	{
		try
		{
			if (Directory.Exists(Path.Combine(root, "runtime")) || Directory.Exists(Path.Combine(root, "bridge")) || File.Exists(Path.Combine(root, ".payload-stamp")))
			{
				return true;
			}
			string path = Path.Combine(root, "mclauncher");
			if (Directory.Exists(path))
			{
				return Directory.EnumerateFileSystemEntries(path).Any((string e) => !string.Equals(Path.GetFileName(e), "locales", StringComparison.OrdinalIgnoreCase));
			}
		}
		catch (Exception)
		{
		}
		return false;
	}

	internal static void CleanupLegacy(string root)
	{
		if (!HasLegacy(root))
		{
			return;
		}
		string[] array = new string[4] { "runtime", "mclauncher", "bridge", ".payload-stamp" };
		foreach (string path in array)
		{
			string path2 = Path.Combine(root, path);
			try
			{
				if (Directory.Exists(path2))
				{
					Directory.Delete(path2, recursive: true);
				}
				else if (File.Exists(path2))
				{
					File.Delete(path2);
				}
			}
			catch (IOException)
			{
			}
			catch (UnauthorizedAccessException)
			{
			}
		}
	}

	internal static void WriteLocales(string root)
	{
		try
		{
			string text = Path.Combine(root, "mclauncher", "locales");
			foreach (string item in CoreResources.ListDir("locales"))
			{
				byte[] array = CoreResources.ReadBytes("locales/" + item);
				if (array == null)
				{
					continue;
				}
				string text2 = Path.Combine(text, item);
				try
				{
					if (!File.Exists(text2) || !File.ReadAllBytes(text2).SequenceEqual(array))
					{
						Directory.CreateDirectory(text);
						string text3 = text2 + ".tmp";
						File.WriteAllBytes(text3, array);
						if (File.Exists(text2))
						{
							File.Replace(text3, text2, null);
						}
						else
						{
							File.Move(text3, text2);
						}
					}
				}
				catch (IOException)
				{
				}
				catch (UnauthorizedAccessException)
				{
				}
			}
		}
		catch (Exception)
		{
		}
	}
}
