using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace HelL.Core.Launcher;

public static class ContentExport
{
	public const string EXPORT_DIR_NAME = "exports";

	public static readonly PyDict CONTENT_DIRS = PyDict.Of("mod", "mods", "shader", "shaderpacks", "resourcepack", "resourcepacks", "datapack", "datapacks", "world", "saves");

	public static readonly string[] ARCHIVED_KINDS = new string[1] { "world" };

	private static readonly string[] _DOUBLE_SUFFIXES = new string[3] { ".jar.disabled", ".zip.disabled", ".tar.gz" };

	public static string DefaultExportDir()
	{
		string text = Py.SS(Config.CONFIG.Get("export_dir"));
		if (text.Length > 0)
		{
			PPath pPath = new PPath(text);
			if (pPath.IsDir())
			{
				return pPath.ToString();
			}
		}
		string text2 = PPath.Join(Utils.ROOT, "exports");
		try
		{
			Directory.CreateDirectory(PPath.Abs(text2));
			return text2;
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
		{
			return DesktopDir();
		}
	}

	public static string RememberExportDir(object path)
	{
		PPath pPath = new PPath(Py.AsStr(path, "__fspath__"));
		if (!pPath.IsDir())
		{
			return DefaultExportDir();
		}
		Config.CONFIG.Set("export_dir", pPath.ToString());
		Config.CONFIG.Save();
		return pPath.ToString();
	}

	internal static string DesktopDir()
	{
		if (Utils.IS_WINDOWS)
		{
			string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
			if (!string.IsNullOrEmpty(folderPath) && Directory.Exists(folderPath))
			{
				return PPath.Norm(folderPath);
			}
		}
		string text = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		if (string.IsNullOrEmpty(text))
		{
			text = Environment.GetEnvironmentVariable("HOME") ?? "";
		}
		string[] array = new string[2] { "Desktop", "桌面" };
		foreach (string text2 in array)
		{
			string text3 = PPath.Join(text, text2);
			if (Directory.Exists(text3))
			{
				return text3;
			}
		}
		return PPath.Norm(text);
	}

	public static string ContentDir(Instance instance, object kind, object version = null)
	{
		if (kind is PyDict || kind is List<object>)
		{
			throw new TypeError("unhashable type: '" + Py.TypeName(kind) + "'");
		}
		string text = ((kind is string key) ? (CONTENT_DIRS[key] as string) : null);
		if (string.IsNullOrEmpty(text))
		{
			throw new ExportError(Py.Fmt("不支持导出的类型: {0!r}", kind));
		}
		if (Py.Truthy(version))
		{
			return PPath.Join(VersionSettings.GameDir(instance, version), text);
		}
		return PPath.Join(instance.path, text);
	}

	public static string SourcePath(Instance instance, object kind, object name, object version = null)
	{
		PPath pPath = new PPath(ContentDir(instance, kind, version)).Resolve();
		PPath pPath2 = (pPath / Py.AsStr(name, "__fspath__")).Resolve();
		if (!pPath2.Parent.Equals(pPath))
		{
			throw new ExportError(Py.Fmt("非法的文件名: {0!r}", name));
		}
		if (!pPath2.Exists())
		{
			throw new ExportError(Py.Fmt("文件不存在: {0}", name));
		}
		return pPath2.ToString();
	}

	private static (string stem, string suffix) _split_name(string filename)
	{
		string text = Py.Lower(filename);
		string[] dOUBLE_SUFFIXES = _DOUBLE_SUFFIXES;
		foreach (string text2 in dOUBLE_SUFFIXES)
		{
			if (text.EndsWith(text2))
			{
				return (stem: filename.Substring(0, filename.Length - text2.Length), suffix: filename.Substring(filename.Length - text2.Length));
			}
		}
		PPath pPath = new PPath(filename);
		return (stem: pPath.Stem, suffix: pPath.Suffix);
	}

	private static string _free_path(string folder, string filename)
	{
		string text = PPath.Join(folder, filename);
		if (!LocalFs.Exists(text))
		{
			return text;
		}
		(string stem, string suffix) tuple = _split_name(filename);
		string item = tuple.stem;
		string item2 = tuple.suffix;
		int num = 2;
		string text2;
		while (true)
		{
			text2 = PPath.Join(folder, item + " (" + num + ")" + item2);
			if (!LocalFs.Exists(text2))
			{
				break;
			}
			num++;
		}
		return text2;
	}

	private static string MakeZipArchive(string base_name, string root_dir)
	{
		string fullPath = Path.GetFullPath(PPath.Abs(base_name + ".zip"));
		string directoryName = Path.GetDirectoryName(fullPath);
		if (!string.IsNullOrEmpty(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		using (FileStream stream = new FileStream(fullPath, FileMode.Create, FileAccess.ReadWrite))
		{
			ZipArchive zf = new ZipArchive(stream, ZipArchiveMode.Create);
			try
			{
				Walk(root_dir, "");
			}
			finally
			{
				if (zf != null)
				{
					((IDisposable)zf).Dispose();
				}
			}
			void Walk(string dir, string rel)
			{
				List<string> list = Directory.GetDirectories(PPath.Abs(dir)).Select(Path.GetFileName).OrderBy<string, string>((string x) => x, StringComparer.Ordinal)
					.ToList();
				foreach (string item in list)
				{
					zf.CreateEntry(rel + item + "/").LastWriteTime = ClampZipTime(Directory.GetLastWriteTime(PPath.Abs(PPath.Join(dir, item))));
				}
				string[] files = Directory.GetFiles(PPath.Abs(dir));
				foreach (string path in files)
				{
					ZipArchiveEntry zipArchiveEntry = zf.CreateEntry(rel + Path.GetFileName(path), CompressionLevel.Optimal);
					zipArchiveEntry.LastWriteTime = ClampZipTime(File.GetLastWriteTime(path));
					using Stream destination = zipArchiveEntry.Open();
					using FileStream fileStream = File.OpenRead(path);
					fileStream.CopyTo(destination);
				}
				foreach (string item2 in list)
				{
					if (!SingleRoot.IsSymlink(PPath.Join(dir, item2)))
					{
						Walk(PPath.Join(dir, item2), rel + item2 + "/");
					}
				}
			}
		}
		return PPath.Norm(fullPath);
	}

	private static DateTime ClampZipTime(DateTime t)
	{
		if (t.Year >= 1980)
		{
			return t;
		}
		return new DateTime(1980, 1, 1);
	}

	public static string ExportOne(Instance instance, object kind, object name, object dest_dir = null, object version = null)
	{
		string text = SourcePath(instance, kind, name, version);
		string text2 = (Py.Truthy(dest_dir) ? new PPath(Py.Str(dest_dir)).ToString() : DefaultExportDir());
		Utils.EnsureDir(text2);
		if (Py.Eq(kind, "world") || LocalFs.IsDir(text))
		{
			if (Py.Eq(kind, "world"))
			{
				return Saves.ExportSave(instance, name, text2, version);
			}
			string text3 = _free_path(text2, new PPath(text).Name + ".zip");
			return MakeZipArchive(new PPath(text3).WithSuffix("").ToString(), text);
		}
		string text4 = _free_path(text2, new PPath(text).Name);
		LocalFs.Copy2(text, text4);
		return text4;
	}

	public static PyDict ExportMany(Instance instance, object kind, object names, object dest_dir = null, object version = null)
	{
		string text = (Py.Truthy(dest_dir) ? new PPath(Py.Str(dest_dir)).ToString() : DefaultExportDir());
		Utils.EnsureDir(text);
		List<object> list = new List<object>();
		List<object> list2 = new List<object>();
		IEnumerable<object> enumerable = ((!Py.Truthy(names)) ? new List<object>() : ((!(names is string source)) ? ((!(names is List<object> list3)) ? ((names as PyDict) ?? throw new TypeError("'" + Py.TypeName(names) + "' object is not iterable")).Keys.Cast<object>() : list3) : ((IEnumerable<char>)source).Select((Func<char, object>)((char ch) => ch.ToString()))));
		foreach (object item in enumerable)
		{
			try
			{
				list.Add(ExportOne(instance, kind, item, text, version));
			}
			catch (Exception e)
			{
				list2.Add(PyDict.Of("name", Py.Str(item), "error", Py.ExcStr(e)));
			}
		}
		return PyDict.Of("dir", text, "exported", list, "failed", list2);
	}
}
