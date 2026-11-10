using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace HelL.Core.Launcher;

public static class Saves
{
	public const string BACKUP_DIR_NAME = "backups";

	private static readonly Regex _STAMP_RE = new Regex("-(\\d{8}-\\d{6})(?:-\\d+)?$");

	internal static string StripStamp(string stem)
	{
		return _STAMP_RE.Replace(stem, "", 1);
	}

	private static string _game_dir(Instance instance, object version_id)
	{
		if (Py.Truthy(version_id))
		{
			return VersionSettings.GameDir(instance, version_id);
		}
		return instance.path;
	}

	private static string _safe_child(string folder0, object name, string what = "存档")
	{
		PPath pPath = new PPath(folder0).Resolve();
		PPath pPath2 = (pPath / Py.Str(name)).Resolve();
		if (!pPath2.Parent.Equals(pPath))
		{
			throw new SaveError(Py.Fmt("非法{0}名: {1}", what, name));
		}
		return pPath2.ToString();
	}

	public static List<object> ListSaves(Instance instance, object version_id = null)
	{
		string text = PPath.Join(_game_dir(instance, version_id), "saves");
		if (!LocalFs.IsDir(text))
		{
			return new List<object>();
		}
		List<object> list = new List<object>();
		foreach (string item in LocalFs.Children(text).OrderBy<string, string>((string x) => Py.Lower(new PPath(x).Name), StringComparer.Ordinal).ToList())
		{
			string name = new PPath(item).Name;
			if (LocalFs.IsDir(item) && !name.StartsWith("."))
			{
				string text2 = PPath.Join(item, "icon.png");
				list.Add(PyDict.Of("name", name, "path", item, "icon", LocalFs.IsFile(text2) ? text2 : "", "bytes", _dir_size(item), "mtime", LocalFs.MTimeInt(item)));
			}
		}
		return list;
	}

	public static void DeleteSave(Instance instance, object name, object version_id = null)
	{
		string text = _safe_child(PPath.Join(_game_dir(instance, version_id), "saves"), name);
		if (!LocalFs.Exists(text))
		{
			throw new SaveError(Py.Fmt("存档不存在: {0}", name));
		}
		Utils.RemoveTree(text);
	}

	public static string OpenSave(Instance instance, object name, object version_id = null)
	{
		string text = PPath.Join(_game_dir(instance, version_id), "saves", Py.Str(name));
		if (!LocalFs.IsDir(text))
		{
			throw new SaveError(Py.Fmt("存档不存在: {0}", name));
		}
		Crash.OpenPath(text);
		return text;
	}

	public static string InstallDatapackIntoSave(Instance instance, object filename, object save_name, object version_id = null)
	{
		PPath pPath = new PPath(PPath.Join(instance.path, "datapacks", Py.Str(filename))).Resolve();
		PPath other = new PPath(PPath.Join(instance.path, "datapacks")).Resolve();
		if (!pPath.Parent.Equals(other) || !pPath.IsFile())
		{
			throw new SaveError(Py.Fmt("数据包不存在: {0}", filename));
		}
		string text = _safe_child(PPath.Join(_game_dir(instance, version_id), "saves"), save_name);
		string text2 = PPath.Join(text, "datapacks");
		Utils.EnsureDir(text2);
		string text3 = PPath.Join(text2, pPath.Name);
		LocalFs.Copy2(pPath.ToString(), text3);
		return text3;
	}

	public static List<object> ListMedia(Instance instance, object kind, object version_id = null)
	{
		string text;
		string[] source;
		switch (kind as string)
		{
		case "screenshots":
			text = "screenshots";
			source = new string[3] { ".png", ".jpg", ".jpeg" };
			break;
		case "crash-reports":
			text = "crash-reports";
			source = new string[1] { ".txt" };
			break;
		case "logs":
			text = "logs";
			source = new string[3] { ".log", ".gz", ".txt" };
			break;
		default:
			if (kind is PyDict || kind is List<object>)
			{
				throw new TypeError("unhashable type: '" + Py.TypeName(kind) + "'");
			}
			throw new SaveError(Py.Fmt("未知类型: {0}", kind));
		}
		string text2 = PPath.Join(_game_dir(instance, version_id), text);
		if (!LocalFs.IsDir(text2))
		{
			return new List<object>();
		}
		List<object> list = new List<object>();
		foreach (string item in (from x in LocalFs.Children(text2)
			orderby LocalFs.MTime(x) descending
			select x).ToList())
		{
			if (LocalFs.IsFile(item) && source.Contains<string>(Py.Lower(new PPath(item).Suffix)))
			{
				list.Add(PyDict.Of("name", new PPath(item).Name, "path", item, "bytes", LocalFs.Size(item), "mtime", LocalFs.MTimeInt(item)));
			}
		}
		return list.Take(200).ToList();
	}

	public static string BackupsDir(Instance instance, object version_id = null)
	{
		return PPath.Join(_game_dir(instance, version_id), "backups");
	}

	private static void ZipFolder(string src, string outPath, Action<int, int> onFile)
	{
		SaveArchive.EnsureIdle(PPath.Abs(src));
		List<string> list = SaveArchive.WorldFiles(PPath.Abs(src)).ToList();
		int arg = ((list.Count <= 0) ? 1 : list.Count);
		string name = new PPath(src).Name;
		using FileStream stream = new FileStream(PPath.Abs(outPath), FileMode.Create, FileAccess.ReadWrite);
		using ZipArchive zipArchive = new ZipArchive(stream, ZipArchiveMode.Create);
		int num = 0;
		foreach (string item in list)
		{
			num++;
			string text = new PPath(item).RelativeTo(src).ToString().Replace('\\', '/');
			string entryName = name + "/" + text;
			ZipArchiveEntry zipArchiveEntry = zipArchive.CreateEntry(entryName, CompressionLevel.Optimal);
			DateTime dateTime = File.GetLastWriteTime(PPath.Abs(item));
			if (dateTime.Year < 1980)
			{
				dateTime = new DateTime(1980, 1, 1);
			}
			zipArchiveEntry.LastWriteTime = dateTime;
			using (Stream destination = zipArchiveEntry.Open())
			{
				using FileStream fileStream = File.OpenRead(PPath.Abs(item));
				fileStream.CopyTo(destination);
			}
			onFile?.Invoke(num, arg);
		}
	}

	public static PyDict BackupSave(Instance instance, object name, object version_id = null, Action<string, long, long> on_progress = null)
	{
		string text = _safe_child(PPath.Join(_game_dir(instance, version_id), "saves"), name);
		if (!LocalFs.IsDir(text))
		{
			throw new SaveError(Py.Fmt("存档不存在: {0}", name));
		}
		string srcName = new PPath(text).Name;
		string text2 = BackupsDir(instance, version_id);
		Utils.EnsureDir(text2);
		string text3 = DateTime.Now.ToString("yyyyMMdd-HHmmss");
		string text4 = PPath.Join(text2, srcName + "-" + text3 + ".zip");
		int num = 1;
		while (LocalFs.Exists(text4))
		{
			text4 = PPath.Join(text2, srcName + "-" + text3 + "-" + num + ".zip");
			num++;
		}
		string text5 = text4 + "." + Guid.NewGuid().ToString("N") + ".part";
		try
		{
			ZipFolder(text, text5, delegate(int i, int total)
			{
				on_progress?.Invoke("备份 " + srcName, i, total);
			});
			File.Move(PPath.Abs(text5), PPath.Abs(text4));
		}
		catch (Exception)
		{
			try
			{
				File.Delete(PPath.Abs(text5));
			}
			catch (Exception cleanupError)
			{
				Utils.log.Warning("清理未完成的存档备份失败: %s", cleanupError.Message);
			}
			throw;
		}
		return PyDict.Of("name", new PPath(text4).Name, "path", text4, "save", srcName, "bytes", LocalFs.Size(text4), "mtime", LocalFs.MTimeInt(text4));
	}

	public static List<object> ListBackups(Instance instance, object save_name = null, object version_id = null)
	{
		string text = BackupsDir(instance, version_id);
		if (!LocalFs.IsDir(text))
		{
			return new List<object>();
		}
		List<PyDict> list = new List<PyDict>();
		foreach (string item in LocalFs.Children(text))
		{
			PPath pPath = new PPath(item);
			string name = pPath.Name;
			if ((Utils.IS_WINDOWS ? name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) : name.EndsWith(".zip", StringComparison.Ordinal)) && !name.StartsWith(".") && LocalFs.IsFile(item))
			{
				string text2 = StripStamp(pPath.Stem);
				if (!Py.Truthy(save_name) || Py.Eq(text2, save_name))
				{
					list.Add(PyDict.Of("name", name, "path", item, "save", text2, "bytes", LocalFs.Size(item), "mtime", LocalFs.MTimeInt(item)));
				}
			}
		}
		return list.OrderByDescending((PyDict r) => (long)r["mtime"]).Cast<object>().ToList();
	}

	private static bool WinDrive(string member)
	{
		string text = member.Replace('/', '\\');
		if (text.Length >= 2 && text[1] == ':' && char.IsLetter(text[0]))
		{
			return true;
		}
		if (text.StartsWith("\\\\"))
		{
			string[] array = text.Substring(2).Split('\\');
			if (array.Length >= 2 && array[0].Length > 0)
			{
				return array[1].Length > 0;
			}
			return false;
		}
		return false;
	}

	public static PyDict RestoreBackup(Instance instance, object backup_name, object version_id = null, object target_name = null, bool overwrite = false)
	{
		string text = _safe_child(BackupsDir(instance, version_id), backup_name, "备份");
		if (!LocalFs.IsFile(text))
		{
			throw new SaveError(Py.Fmt("备份不存在: {0}", backup_name));
		}
		string text2 = PPath.Join(_game_dir(instance, version_id), "saves");
		Utils.EnsureDir(text2);
		string text3 = (Py.Truthy(target_name) ? Py.Str(target_name) : StripStamp(new PPath(text).Stem));
		string text4 = _safe_child(text2, text3);
		ZipArchive zipArchive;
		try
		{
			zipArchive = Utils.OpenZip(text);
		}
		catch (InvalidDataException)
		{
			throw new SaveError("备份文件损坏: File is not a zip file");
		}
		using (zipArchive)
		{
			List<ZipArchiveEntry> list = zipArchive.Entries.ToList();
			if ((from m in list.Select(Utils.ZipEntryName)
				where !m.Replace("\\", "/").EndsWith("/")
				select m).ToList().Count == 0)
			{
				throw new SaveError("备份是空的");
			}
			foreach (ZipArchiveEntry item in list)
			{
				string text5 = Utils.ZipEntryName(item);
				string text6 = text5.Replace("\\", "/");
				PPath pPath = new PPath(text5);
				if (pPath.IsAbsolute || pPath.Parts.Contains("..") || text6.StartsWith("/") || text6.Split('/').Contains("..") || WinDrive(text5))
				{
					throw new SaveError(Py.Fmt("备份包含非法路径: {0}", text5));
				}
			}
			string text7 = MkDTemp(".restore-", text2);
			string text8 = PPath.Join(text7, "unpacked");
			bool flag = false;
			try
			{
				Utils.EnsureDir(text8);
				SaveArchive.Extract(list, PPath.Abs(text8));
				List<string> list2 = LocalFs.Children(text8);
				string path = ((list2.Count == 1 && LocalFs.IsDir(list2[0])) ? list2[0] : text8);
				SaveArchive.EnsureWorld(PPath.Abs(path));
				if (!overwrite)
				{
					int num = 1;
					while (LocalFs.Exists(text4))
					{
						text4 = _safe_child(text2, text3 + "-还原" + ((num > 1) ? num.ToString() : ""));
						num++;
					}
				}
				string text9 = null;
				if (overwrite && LocalFs.Exists(text4))
				{
					SaveArchive.EnsureIdle(PPath.Abs(text4));
					text9 = PPath.Join(text7, "previous");
					Directory.Move(PPath.Abs(text4), PPath.Abs(text9));
				}
				try
				{
					Directory.Move(PPath.Abs(path), PPath.Abs(text4));
				}
				catch (Exception)
				{
					if (text9 != null)
					{
						try
						{
							Directory.Move(PPath.Abs(text9), PPath.Abs(text4));
						}
						catch (Exception ex3)
						{
							flag = true;
							throw new SaveError(Py.Fmt("还原失败，原存档保留在 {0}: {1}", text9, ex3.Message));
						}
					}
					throw;
				}
			}
			finally
			{
				if (!flag)
				{
					Utils.RemoveTree(text7);
				}
			}
		}
		return PyDict.Of("name", new PPath(text4).Name, "path", text4, "from", new PPath(text).Name);
	}


	private static string MkDTemp(string prefix, string dir)
	{
		for (int i = 0; i < 100; i++)
		{
			string text = PPath.Join(dir, prefix + Guid.NewGuid().ToString("N").Substring(0, 12));
			if (!LocalFs.Exists(text))
			{
				Directory.CreateDirectory(PPath.Abs(text));
				return text;
			}
		}
		throw new FileExistsError("[Errno 17] No usable temporary directory name found");
	}

	public static void DeleteBackup(Instance instance, object backup_name, object version_id = null)
	{
		string text = _safe_child(BackupsDir(instance, version_id), backup_name, "备份");
		if (!LocalFs.IsFile(text))
		{
			throw new SaveError(Py.Fmt("备份不存在: {0}", backup_name));
		}
		File.Delete(PPath.Abs(text));
	}

	public static string ExportSave(Instance instance, object name, object dest, object version_id = null, Action<string, long, long> on_progress = null)
	{
		string text = _safe_child(PPath.Join(_game_dir(instance, version_id), "saves"), name);
		if (!LocalFs.IsDir(text))
		{
			throw new SaveError(Py.Fmt("存档不存在: {0}", name));
		}
		string srcName = new PPath(text).Name;
		PPath pPath = new PPath(Py.AsStr(dest, "__fspath__"));
		if (pPath.IsDir())
		{
			pPath /= srcName + ".zip";
		}
		if (Py.Lower(pPath.Suffix) != ".zip")
		{
			pPath = pPath.WithSuffix(".zip");
		}
		string destination = PPath.Abs(pPath.ToString());
		string worldRoot = PPath.Abs(text).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
		if (destination.StartsWith(worldRoot, StringComparison.OrdinalIgnoreCase))
			throw new SaveError("请将导出文件保存到存档目录之外");
		Utils.EnsureDir(pPath.Parent.ToString());
		string pending = destination + "." + Guid.NewGuid().ToString("N") + ".part";
		try
		{
			ZipFolder(text, pending, (i, total) => on_progress?.Invoke("导出 " + srcName, i, total));
			Utils.OsReplace(pending, destination);
		}
		finally
		{
			if (File.Exists(pending))
				File.Delete(pending);
		}
		return pPath.ToString();
	}

	private static long _dir_size(string path, int limit = 80)
	{
		long num = 0L;
		int num2 = 0;
		try
		{
			foreach (string item in LocalFs.RglobFiles(path))
			{
				num += LocalFs.Size(item);
				num2++;
				if (num2 >= limit)
				{
					break;
				}
			}
		}
		catch (IOException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
		return num;
	}
}
