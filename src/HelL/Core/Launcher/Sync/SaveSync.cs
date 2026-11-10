using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace HelL.Core.Launcher.Sync;

/// <summary>同步决策结果，手机端和电脑端用同一套规则。</summary>
public enum SyncDecision
{
	/// <summary>两边一样，什么都不用做。</summary>
	Same,
	/// <summary>只有本端改过：把本端推给对端。</summary>
	Push,
	/// <summary>只有对端改过：把对端拉到本端。</summary>
	Pull,
	/// <summary>两边都改过：交给玩家选。</summary>
	Conflict,
	/// <summary>只有本端有这个世界。</summary>
	OnlyLocal,
	/// <summary>只有对端有这个世界。</summary>
	OnlyRemote
}

/// <summary>
/// 存档同步的核心逻辑：世界指纹、同步决策、导入导出。和手机通信在 <see cref="PhoneLink"/>。
/// </summary>
public static class SaveSync
{
	public const string LockFile = "session.lock";

	private static readonly Dictionary<string, (long size, long ticks, string sha)> _fileCache = new Dictionary<string, (long, long, string)>();

	private static readonly object _cacheLock = new object();

	/// <summary>
	/// 世界指纹：对世界目录下除 session.lock 外的每个文件生成一行 "相对路径\t大小\tsha256\n"，
	/// 按相对路径的 UTF-16 码元顺序（C# string.CompareOrdinal / Java String.compareTo）排序后拼起来，
	/// 再取整体 UTF-8 的 sha256（小写十六进制）。路径分隔符统一用 "/"。
	/// </summary>
	public static string WorldHash(string worldDir)
	{
		return Sha256Hex(Encoding.UTF8.GetBytes(Manifest(worldDir)));
	}

	public static string Manifest(string worldDir)
	{
		string root = Path.GetFullPath(worldDir);
		List<(string rel, string full)> files = new List<(string, string)>();
		foreach (string f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
		{
			string rel = f.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');
			if (rel == LockFile)
			{
				continue;
			}
			files.Add((rel, f));
		}
		files.Sort((a, b) => string.CompareOrdinal(a.rel, b.rel));
		StringBuilder sb = new StringBuilder();
		foreach ((string rel, string full) in files)
		{
			FileInfo fi = new FileInfo(full);
			sb.Append(rel).Append('\t').Append(fi.Length).Append('\t').Append(FileSha(fi)).Append('\n');
		}
		return sb.ToString();
	}

	private static string FileSha(FileInfo fi)
	{
		long ticks = fi.LastWriteTimeUtc.Ticks;
		lock (_cacheLock)
		{
			if (_fileCache.TryGetValue(fi.FullName, out var c) && c.size == fi.Length && c.ticks == ticks)
			{
				return c.sha;
			}
		}
		string sha;
		using (FileStream fs = new FileStream(fi.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
		using (SHA256 h = SHA256.Create())
		{
			sha = Hex(h.ComputeHash(fs));
		}
		// 刚写过的文件不进缓存：修改时间精度有限，同一刻内改成同样大小会被误判为没变
		if ((DateTime.UtcNow - fi.LastWriteTimeUtc).TotalSeconds > 3.0)
		{
			lock (_cacheLock)
			{
				if (_fileCache.Count > 50000)
				{
					_fileCache.Clear();
				}
				_fileCache[fi.FullName] = (fi.Length, ticks, sha);
			}
		}
		return sha;
	}

	public static string Sha256Hex(byte[] data)
	{
		using SHA256 h = SHA256.Create();
		return Hex(h.ComputeHash(data));
	}

	private static string Hex(byte[] b)
	{
		StringBuilder sb = new StringBuilder(b.Length * 2);
		foreach (byte x in b)
		{
			sb.Append(x.ToString("x2"));
		}
		return sb.ToString();
	}

	/// <summary>
	/// 三方比较：base 是上次两边同步成功时的指纹（没同步过传 null）。
	/// local / remote 为 null 表示那一端没有这个世界。
	/// </summary>
	public static SyncDecision Decide(string local, string remote, string baseHash)
	{
		bool hasL = !string.IsNullOrEmpty(local);
		bool hasR = !string.IsNullOrEmpty(remote);
		if (!hasL && !hasR)
		{
			return SyncDecision.Same;
		}
		if (!hasR)
		{
			return SyncDecision.OnlyLocal;
		}
		if (!hasL)
		{
			return SyncDecision.OnlyRemote;
		}
		if (local == remote)
		{
			return SyncDecision.Same;
		}
		if (string.IsNullOrEmpty(baseHash))
		{
			return SyncDecision.Conflict;
		}
		if (remote == baseHash)
		{
			return SyncDecision.Push;
		}
		if (local == baseHash)
		{
			return SyncDecision.Pull;
		}
		return SyncDecision.Conflict;
	}

	/// <summary>游戏正在用这个世界时 session.lock 会被占住（Windows 上独占打开会失败）。</summary>
	public static bool IsLocked(string worldDir)
	{
		string p = Path.Combine(worldDir, LockFile);
		if (!File.Exists(p))
		{
			return false;
		}
		try
		{
			using (new FileStream(p, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
			{
			}
			return false;
		}
		catch (IOException)
		{
			return true;
		}
		catch (UnauthorizedAccessException)
		{
			return false;
		}
	}

	/// <summary>世界文件夹名是否安全（不能带路径、不能是隐藏目录）。</summary>
	public static bool SafeWorldName(string name)
	{
		if (string.IsNullOrWhiteSpace(name) || name.StartsWith(".") || name.Length > 200)
		{
			return false;
		}
		return name.IndexOfAny(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|', '\0' }) < 0 && name.Trim() == name;
	}

	/// <summary>一个可同步的位置：实例，或开了版本隔离的某个版本。</summary>
	public sealed class Target
	{
		public string Instance = "";

		public string Version = "";

		public string SavesDir = "";

		public string Label => Version.Length == 0 ? Instance : Instance + " / " + Version;
	}

	public static List<Target> Targets()
	{
		List<Target> list = new List<Target>();
		foreach (object o in Instances.ListInstances())
		{
			string name = Py.Str(o);
			Instance inst;
			try
			{
				inst = new Instance(name);
			}
			catch (Exception)
			{
				continue;
			}
			list.Add(new Target { Instance = inst.name, SavesDir = Path.Combine(inst.path, "saves") });
			try
			{
				string vdir = inst.VersionsDir();
				if (Directory.Exists(vdir))
				{
					foreach (string v in Directory.GetDirectories(vdir).OrderBy((string x) => x, StringComparer.OrdinalIgnoreCase))
					{
						string saves = Path.Combine(v, "saves");
						if (Directory.Exists(saves))
						{
							list.Add(new Target { Instance = inst.name, Version = Path.GetFileName(v), SavesDir = saves });
						}
					}
				}
			}
			catch (Exception)
			{
			}
		}
		return list;
	}

	public static Target FindTarget(string instance, string version)
	{
		instance = instance ?? "";
		version = version ?? "";
		List<Target> all = Targets();
		if (instance.Length == 0)
		{
			string def = Py.Str(Py.Or(Config.CONFIG.Get("default_instance", ""), ""));
			instance = all.Any((Target t) => t.Instance == def) ? def : all.FirstOrDefault()?.Instance ?? "";
		}
		return all.FirstOrDefault((Target t) => t.Instance == instance && t.Version == version);
	}

	public static PyDict WorldInfo(string worldDir, bool withHash = true)
	{
		LevelDat ld = LevelDat.ReadFile(Path.Combine(worldDir, "level.dat"));
		long bytes = 0L;
		long mtime = 0L;
		try
		{
			foreach (FileInfo fi in new DirectoryInfo(worldDir).EnumerateFiles("*", SearchOption.AllDirectories))
			{
				bytes += fi.Length;
			}
			mtime = new DateTimeOffset(Directory.GetLastWriteTimeUtc(worldDir)).ToUnixTimeMilliseconds();
		}
		catch (Exception)
		{
		}
		bool locked = IsLocked(worldDir);
		PyDict d = PyDict.Of("name", Path.GetFileName(worldDir), "level_name", ld?.LevelName ?? "", "last_played", ld?.LastPlayed ?? 0L, "data_version", (long)(ld?.DataVersion ?? 0), "game_version", ld?.VersionName ?? "", "bytes", bytes, "mtime", mtime, "locked", locked);
		if (withHash)
		{
			d["hash"] = WorldHash(worldDir);
		}
		return d;
	}

	public static List<object> ListWorlds(Target t, bool withHash = true)
	{
		List<object> list = new List<object>();
		if (!Directory.Exists(t.SavesDir))
		{
			return list;
		}
		foreach (string d in Directory.GetDirectories(t.SavesDir).OrderBy((string x) => x, StringComparer.OrdinalIgnoreCase))
		{
			string name = Path.GetFileName(d);
			if (!SafeWorldName(name) || !File.Exists(Path.Combine(d, "level.dat")))
			{
				continue;
			}
			list.Add(WorldInfo(d, withHash));
		}
		return list;
	}

	/// <summary>把世界打成 zip（根目录为世界文件夹名，不含 session.lock）。</summary>
	public static void ExportZip(string worldDir, Stream output)
	{
		string root = Path.GetFullPath(worldDir);
		string top = Path.GetFileName(root);
		using ZipArchive zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
		foreach (string f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
		{
			string rel = f.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');
			if (rel == LockFile)
			{
				continue;
			}
			ZipArchiveEntry e = zip.CreateEntry(top + "/" + rel, CompressionLevel.Optimal);
			DateTime t = File.GetLastWriteTime(f);
			e.LastWriteTime = t.Year < 1980 ? new DateTime(1980, 1, 1) : t;
			using Stream dst = e.Open();
			using FileStream src = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
			src.CopyTo(dst);
		}
	}

	/// <summary>检查上传的 zip：要有 level.dat（在根目录或唯一的顶层文件夹里），且没有越界路径。</summary>
	public static string CheckZip(string zipPath)
	{
		try
		{
			using ZipArchive zip = ZipFile.OpenRead(zipPath);
			bool level = false;
			foreach (ZipArchiveEntry e in zip.Entries)
			{
				string n = e.FullName.Replace('\\', '/');
				if (n.StartsWith("/") || n.Split('/').Contains("..") || (n.Length > 1 && n[1] == ':'))
				{
					return "压缩包包含非法路径: " + n;
				}
				string[] parts = n.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
				if ((parts.Length == 1 && parts[0] == "level.dat") || (parts.Length == 2 && parts[1] == "level.dat"))
				{
					level = true;
				}
			}
			return level ? null : "压缩包里没有 level.dat，不是 Minecraft 存档";
		}
		catch (InvalidDataException)
		{
			return "不是有效的 zip 文件";
		}
	}

	/// <summary>
	/// 把上传的 zip 导入为指定世界。已有同名世界时先自动备份再覆盖。
	/// zipPath 会被移动进备份目录，作为"来自手机的版本"保留。返回导入后的世界信息。
	/// </summary>
	public static PyDict ImportZip(Target t, string world, string zipPath)
	{
		Instance inst = new Instance(t.Instance);
		object ver = t.Version.Length == 0 ? null : t.Version;
		string worldDir = Path.Combine(t.SavesDir, world);
		PyDict backup = null;
		if (Directory.Exists(worldDir))
		{
			backup = Saves.BackupSave(inst, world, ver);
		}
		string bdir = Saves.BackupsDir(inst, ver);
		Utils.EnsureDir(bdir);
		string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
		string dest;
		int n = 1;
		do
		{
			dest = Path.Combine(bdir, world + "-" + stamp + "-" + n + ".zip");
			n++;
		}
		while (File.Exists(dest));
		File.Move(zipPath, dest);
		Saves.RestoreBackup(inst, Path.GetFileName(dest), ver, world, overwrite: true);
		PyDict info = WorldInfo(worldDir);
		info["backup"] = backup?.Get("name") ?? "";
		info["received"] = Path.GetFileName(dest);
		return info;
	}
}
