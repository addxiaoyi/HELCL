using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace HelL.Core.Launcher;

public static class Worlds
{
	public static List<object> SearchWorlds(DownloadManager dm, object query, PyDict extra = null)
	{
		extra = new PyDict(extra ?? new PyDict());
		extra["kind"] = "world";
		return CatalogFiles.SearchProjects(dm, "world", query, Py.Or(extra["source"], "CurseForge"), extra);
	}

	public static string SavesRoot(Instance instance, object version_id = null)
	{
		if (Py.Truthy(version_id))
		{
			return PPath.Join(VersionSettings.GameDir(instance, version_id), "saves");
		}
		return PPath.Join(instance.path, "saves");
	}

	public static PyDict InstallWorld(DownloadManager dm, PyDict extra, Instance instance, Action<string, long, long> on_progress = null, object version_id = null)
	{
		instance.EnsureStandardDirs();
		string text = SavesRoot(instance, Py.Truthy(version_id) ? version_id : Py.S((extra ?? new PyDict())["version"]));
		Directory.CreateDirectory(PPath.Abs(text));
		extra = new PyDict(extra ?? new PyDict());
		object o = extra["path"];
		if (Py.Truthy(o) && LocalFs.IsFile(Py.Str(o)))
		{
			return _extract_world(PPath.Norm(Py.Str(o)), text);
		}
		object o2 = extra["url"];
		if (Py.Truthy(o2) && Py.Str(o2).StartsWith("http", StringComparison.Ordinal))
		{
			string text2 = Py.Str(o2).Split('/').Last()
				.Split('?')[0];
			string text3 = PPath.Join(Utils.ROOT, "cache", (text2.Length > 0) ? text2 : "world.zip");
			on_progress?.Invoke("下载世界", 0L, 1L);
			dm.Download(Py.Str(o2), text3, null, null, force: false, 900L);
			return _extract_world(text3, text);
		}
		object obj = extra["id"];
		if (!Py.Truthy(obj))
		{
			throw new WorldError("缺少 CurseForge 世界项目 id");
		}
		object file_id = Py.Or(extra["file_id"], extra["version_id"]);
		object obj2;
		if (Py.Truthy(file_id))
		{
			obj2 = Mods.CfFiles(dm, obj, null, null, null, 50L).FirstOrDefault((object f) => Py.Str(Mods.Gt(f, "id")) == Py.Str(file_id));
			if (obj2 == null)
			{
				throw new WorldError("找不到指定世界文件");
			}
		}
		else
		{
			object obj3 = Py.Or(extra["game_version"], extra["mc_version"]);
			List<object> list = Mods.CfFiles(dm, obj, null, Py.Truthy(obj3) ? obj3 : null, null, 50L);
			if (list.Count == 0)
			{
				throw new WorldError("该世界没有可下载文件");
			}
			obj2 = list[0];
		}
		string text4 = Py.Str(Py.Or(Mods.Gt(obj2, "fileName"), "world-" + Py.Str(obj) + ".zip"));
		string text5 = PPath.Join(Utils.ROOT, "cache", text4);
		List<object> list2 = new List<object>();
		if (Py.Truthy(Mods.Gt(obj2, "downloadUrl")))
		{
			list2.Add(Mods.Gt(obj2, "downloadUrl"));
		}
		list2.AddRange(Mods.CfModDownloadUrls(obj, Mods.Gt(obj2, "id"), text4));
		Exception ex = null;
		foreach (object item in list2)
		{
			try
			{
				on_progress?.Invoke("下载世界 " + text4, 0L, 1L);
				dm.Download(item, text5, null, null, force: false, 900L);
				ex = null;
			}
			catch (Exception ex2)
			{
				ex = ex2;
				continue;
			}
			break;
		}
		if (ex != null)
		{
			throw new WorldError("下载世界失败: " + Py.ExcStr(ex));
		}
		return _extract_world(text5, text);
	}

	private static PyDict _extract_world(string archive, string dest_root)
	{
		if (!string.Equals(Path.GetExtension(archive), ".zip", StringComparison.OrdinalIgnoreCase))
			throw new WorldError("请选择包含 level.dat 的 ZIP 世界压缩包");

		string root = PPath.Abs(dest_root);
		string staging = Path.Combine(root, ".import-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(staging);
		var installed = new List<string>();
		try
		{
			using (ZipArchive zip = Utils.OpenZip(archive))
				SaveArchive.Extract(zip.Entries, staging);

			var worlds = new List<(string Source, string Target)>();
			if (File.Exists(Path.Combine(staging, "level.dat")))
			{
				string name = Path.GetFileNameWithoutExtension(archive);
				if (!Sync.SaveSync.SafeWorldName(name))
					throw new WorldError("世界压缩包名称无效");
				worlds.Add((staging, Path.Combine(root, name)));
			}
			else
			{
				foreach (string folder in Directory.GetDirectories(staging))
				{
					if (File.Exists(Path.Combine(folder, "level.dat")))
						worlds.Add((folder, Path.Combine(root, Path.GetFileName(folder))));
				}
			}
			if (worlds.Count == 0)
				throw new WorldError("世界压缩包中缺少 level.dat");
			foreach (var world in worlds)
			{
				SaveArchive.EnsureWorld(world.Source);
				if (Directory.Exists(world.Target) || File.Exists(world.Target))
					throw new WorldError("同名存档已存在，请先重命名或备份: " + Path.GetFileName(world.Target));
			}
			foreach (var world in worlds)
			{
				Directory.Move(world.Source, world.Target);
				installed.Add(world.Target);
			}
			return PyDict.Of("files", installed.Select(path => (object)Path.GetFileName(path)).ToList(), "source", "zip");
		}
		catch (Exception error)
		{
			// Remove only directories created by this import; existing worlds are never merged.
			foreach (string path in installed)
				Directory.Delete(path, recursive: true);
			if (error is SaveError || error is InvalidDataException)
				throw new WorldError("世界压缩包无效: " + error.Message);
			throw;
		}
		finally
		{
			if (Directory.Exists(staging))
				Directory.Delete(staging, recursive: true);
		}
	}
}
