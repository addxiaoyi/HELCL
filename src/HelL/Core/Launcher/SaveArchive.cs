using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace HelL.Core.Launcher;

internal static class SaveArchive
{
	public static void Extract(IEnumerable<ZipArchiveEntry> entries, string destination)
	{
		string root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
		var files = new List<(ZipArchiveEntry Entry, string Path)>();
		var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (ZipArchiveEntry entry in entries)
		{
			string name = Utils.ZipEntryName(entry).Replace('\\', '/');
			string[] parts = name.Split('/').Where(part => part.Length > 0).ToArray();
			bool isLink = ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000;
			if (parts.Length == 0 || name.StartsWith("/") || isLink || parts.Any(UnsafePart))
				throw new SaveError("存档压缩包包含非法路径: " + name);
			string path = Path.GetFullPath(Path.Combine(destination, Path.Combine(parts)));
			if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !names.Add(path))
				throw new SaveError("存档压缩包包含重复或越界路径: " + name);
			if (!name.EndsWith("/", StringComparison.Ordinal))
				files.Add((entry, path));
		}
		foreach (var file in files)
		{
			Directory.CreateDirectory(Path.GetDirectoryName(file.Path));
			using Stream input = file.Entry.Open();
			using var output = new FileStream(file.Path, FileMode.CreateNew, FileAccess.Write);
			input.CopyTo(output);
		}
	}

	private static bool UnsafePart(string part)
	{
		if (part == "." || part == ".." || part.EndsWith(".") || part.EndsWith(" ")
			|| part.Any(character => character < 32) || part.IndexOfAny(new[] { ':', '*', '?', '"', '<', '>', '|' }) >= 0)
			return true;
		string stem = part.Split('.')[0].ToUpperInvariant();
		return stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL"
			|| (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] >= '1' && stem[3] <= '9');
	}

	public static IEnumerable<string> WorldFiles(string world)
	{
		foreach (string path in Directory.EnumerateFileSystemEntries(world))
		{
			FileAttributes attributes = File.GetAttributes(path);
			if ((attributes & FileAttributes.ReparsePoint) != 0)
				throw new SaveError("存档包含链接，无法安全备份: " + Path.GetFileName(path));
			if ((attributes & FileAttributes.Directory) != 0)
			{
				foreach (string file in WorldFiles(path))
					 yield return file;
			}
			else if (!string.Equals(Path.GetFileName(path), "session.lock", StringComparison.OrdinalIgnoreCase))
				yield return path;
		}
	}

	public static void EnsureIdle(string world)
	{
		if ((File.GetAttributes(world) & FileAttributes.ReparsePoint) != 0)
			throw new SaveError("不能操作链接到其他目录的存档");
		string lockPath = Path.Combine(world, "session.lock");
		if (!File.Exists(lockPath))
			return;
		try
		{
			using (new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
		}
		catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
		{
			throw new SaveError("存档正在使用或无法访问，请退出游戏后重试");
		}
	}

	public static void EnsureWorld(string world)
	{
		if (Sync.LevelDat.ReadFile(Path.Combine(world, "level.dat")) == null)
			throw new SaveError("存档的 level.dat 缺失或损坏，已取消操作");
	}
}
