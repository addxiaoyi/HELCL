using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace HelL.Core.Launcher.Sync;

/// <summary>
/// 电脑 ↔ 手机存档同步：记住上次连接的手机，对比两边世界，按「谁改了传谁」推送或拉取。
/// 手机端只给出修改时间，所以每次同步成功后记下"电脑端世界指纹 + 手机端修改时间"作为基准。
/// 状态存在数据目录 phone_link.json。
/// </summary>
public static class PhoneLink
{
	public sealed class Base
	{
		public string local_hash { get; set; } = "";

		public long remote_mtime { get; set; }
	}

	private sealed class State
	{
		public string address { get; set; } = "";

		public string token { get; set; } = "";

		public string device { get; set; } = "";

		public Dictionary<string, Base> bases { get; set; } = new Dictionary<string, Base>();
	}

	public sealed class Row
	{
		public string Name = "";

		public string LocalDir;

		public string LocalHash;

		public long LocalMtime;

		public bool LocalLocked;

		public PhoneClient.World Remote;

		public SyncDecision Decision;

		public bool HasLocal => LocalDir != null;

		public bool HasRemote => Remote != null;
	}

	private static readonly object _lock = new object();

	private static string StatePath => Path.Combine(Utils.ResolveRoot(), "phone_link.json");

	private static State Load()
	{
		try
		{
			if (File.Exists(StatePath))
			{
				State s = JsonSerializer.Deserialize<State>(File.ReadAllText(StatePath, Encoding.UTF8));
				if (s != null)
				{
					s.bases = s.bases ?? new Dictionary<string, Base>();
					return s;
				}
			}
		}
		catch (Exception)
		{
		}
		return new State();
	}

	private static void Save(State s)
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(StatePath));
			File.WriteAllText(StatePath, JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
		}
		catch (Exception)
		{
		}
	}

	#region 连接

	public static string LastAddress => Load().address;

	public static string LastToken => Load().token;

	public static string LastDevice => Load().device;

	/// <summary>当前连上的手机；没连为 null。</summary>
	public static PhoneClient Current { get; private set; }

	public static PhoneClient.Info CurrentInfo { get; private set; }

	/// <summary>用地址和配对码握手，成功后记住，下次自动填好。</summary>
	public static PhoneClient.Info Connect(string address, string token)
	{
		PhoneClient c = new PhoneClient(address, token);
		PhoneClient.Info info = c.Ping();
		Remember(c, info);
		return info;
	}

	public static PhoneClient.Info ConnectUsb(string token, Action<string> log = null)
	{
		string addr = PhoneClient.ConnectUsb(token, out PhoneClient.Info info, log);
		Remember(new PhoneClient(addr, token), info);
		return info;
	}

	private static void Remember(PhoneClient c, PhoneClient.Info info)
	{
		Current = c;
		CurrentInfo = info;
		lock (_lock)
		{
			State s = Load();
			// USB 转发的本机端口每次不同，不记它
			if (!c.BaseUrl.Contains("://127.0.0.1:"))
			{
				s.address = c.BaseUrl.Substring(c.BaseUrl.IndexOf("//", StringComparison.Ordinal) + 2);
			}
			s.token = c.Token;
			s.device = info.Device;
			Save(s);
		}
	}

	public static void Disconnect()
	{
		Current = null;
		CurrentInfo = null;
	}

	private static PhoneClient Need()
	{
		return Current ?? throw new InvalidOperationException("还没连接手机");
	}

	#endregion

	#region 对比

	/// <summary>本机位置对应的手机位置：实例 → global，版本隔离 → v:版本名（手机上有才算）。</summary>
	public static string DefaultRoot(SaveSync.Target t, IEnumerable<PhoneClient.Root> roots)
	{
		List<PhoneClient.Root> list = roots?.ToList() ?? new List<PhoneClient.Root>();
		if (t != null && t.Version.Length > 0 && list.Any((PhoneClient.Root r) => r.Id == "v:" + t.Version))
		{
			return "v:" + t.Version;
		}
		return list.Any((PhoneClient.Root r) => r.Id == "global") || list.Count == 0 ? "global" : list[0].Id;
	}

	private static string Key(SaveSync.Target t, string rootId, string name)
	{
		return Path.GetFullPath(t.SavesDir).ToLowerInvariant() + "|" + rootId + "|" + name;
	}

	public static Base GetBase(SaveSync.Target t, string rootId, string name)
	{
		lock (_lock)
		{
			return Load().bases.TryGetValue(Key(t, rootId, name), out Base b) ? b : null;
		}
	}

	private static void SetBase(SaveSync.Target t, string rootId, string name, string localHash, long remoteMtime)
	{
		lock (_lock)
		{
			State s = Load();
			string k = Key(t, rootId, name);
			if (localHash == null)
			{
				s.bases.Remove(k);
			}
			else
			{
				s.bases[k] = new Base { local_hash = localHash, remote_mtime = remoteMtime };
			}
			Save(s);
		}
	}

	/// <summary>世界目录内所有文件（不含 session.lock）的最大修改时间，Unix 毫秒，和手机端口径一致。</summary>
	public static long MaxMtime(string worldDir)
	{
		long max = 0L;
		foreach (FileInfo fi in new DirectoryInfo(worldDir).EnumerateFiles("*", SearchOption.AllDirectories))
		{
			if (fi.Name == SaveSync.LockFile && fi.DirectoryName.TrimEnd('\\', '/') == Path.GetFullPath(worldDir).TrimEnd('\\', '/'))
			{
				continue;
			}
			max = Math.Max(max, new DateTimeOffset(fi.LastWriteTimeUtc).ToUnixTimeMilliseconds());
		}
		return max;
	}

	/// <summary>
	/// 三方判定。有基准时：电脑端看指纹变没变，手机端看修改时间变没变。
	/// 没基准（第一次见到）时：两边修改时间相差不到 2 秒（zip 时间精度）算一样，否则交给玩家选。
	/// </summary>
	public static SyncDecision Decide(string localHash, long localMtime, PhoneClient.World remote, Base b)
	{
		if (localHash == null || remote == null)
		{
			return SaveSync.Decide(localHash, remote == null ? null : "r", null);
		}
		if (b == null)
		{
			return Math.Abs(localMtime - remote.LastModified) <= 2000 ? SyncDecision.Same : SyncDecision.Conflict;
		}
		bool lc = localHash != b.local_hash;
		bool rc = remote.LastModified != b.remote_mtime;
		return SaveSync.Decide(lc ? "L" : "B", rc ? "R" : "B", "B");
	}

	public static List<Row> Compare(SaveSync.Target t, string rootId)
	{
		PhoneClient.Worlds remote = Need().ListWorlds(rootId);
		Dictionary<string, Row> rows = new Dictionary<string, Row>(StringComparer.Ordinal);
		if (Directory.Exists(t.SavesDir))
		{
			foreach (string d in Directory.GetDirectories(t.SavesDir))
			{
				string name = Path.GetFileName(d);
				if (!SaveSync.SafeWorldName(name) || !File.Exists(Path.Combine(d, "level.dat")))
				{
					continue;
				}
				rows[name] = new Row
				{
					Name = name,
					LocalDir = d,
					LocalHash = SaveSync.WorldHash(d),
					LocalMtime = MaxMtime(d),
					LocalLocked = SaveSync.IsLocked(d)
				};
			}
		}
		foreach (PhoneClient.World w in remote.List)
		{
			if (!rows.TryGetValue(w.DirName, out Row r))
			{
				r = rows[w.DirName] = new Row { Name = w.DirName };
			}
			r.Remote = w;
		}
		foreach (Row r in rows.Values)
		{
			r.Decision = Decide(r.LocalHash, r.LocalMtime, r.Remote, GetBase(t, rootId, r.Name));
		}
		return rows.Values.OrderBy((Row r) => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
	}

	public static List<PhoneClient.Root> Roots()
	{
		return Need().ListWorlds(null).Roots;
	}

	#endregion

	#region 传输

	private static string TempZip()
	{
		string dir = Path.Combine(Path.GetTempPath(), "tipcl-phone-sync");
		Directory.CreateDirectory(dir);
		return Path.Combine(dir, Guid.NewGuid().ToString("N") + ".zip");
	}

	private static string BackupPath(SaveSync.Target t, string world, string tag)
	{
		string bdir = Saves.BackupsDir(new Instance(t.Instance), t.Version.Length == 0 ? null : t.Version);
		Utils.EnsureDir(bdir);
		string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
		string p;
		int n = 1;
		do
		{
			p = Path.Combine(bdir, world + "-" + tag + "-" + stamp + (n > 1 ? "-" + n : "") + ".zip");
			n++;
		}
		while (File.Exists(p));
		return p;
	}

	private static PhoneClient.World RemoteWorld(string rootId, string name)
	{
		return Need().ListWorlds(rootId).List.FirstOrDefault((PhoneClient.World w) => w.DirName == name);
	}

	/// <summary>电脑 → 手机。手机上已有同名世界时，先把手机上的版本下载到本机备份目录再覆盖。</summary>
	public static string Push(SaveSync.Target t, string rootId, string name, Action<string> log = null)
	{
		PhoneClient c = Need();
		string dir = Path.Combine(t.SavesDir, name);
		if (!File.Exists(Path.Combine(dir, "level.dat")))
		{
			throw new InvalidOperationException("电脑上没有这个世界：" + name);
		}
		if (SaveSync.IsLocked(dir))
		{
			throw new InvalidOperationException("「" + name + "」正在游戏中打开，先退出世界再同步");
		}
		bool exists = RemoteWorld(rootId, name) != null;
		if (exists)
		{
			string bk = BackupPath(t, name, "phone");
			log?.Invoke("备份手机上的「" + name + "」");
			c.Download(rootId, name, bk);
		}
		string zip = TempZip();
		try
		{
			using (FileStream fs = File.Create(zip))
			{
				SaveSync.ExportZip(dir, fs);
			}
			log?.Invoke("上传「" + name + "」 " + new FileInfo(zip).Length / 1024 + " KB");
			c.Upload(rootId, name, zip, exists);
		}
		finally
		{
			try
			{
				File.Delete(zip);
			}
			catch (Exception)
			{
			}
		}
		PhoneClient.World after = RemoteWorld(rootId, name);
		SetBase(t, rootId, name, SaveSync.WorldHash(dir), after?.LastModified ?? 0L);
		return name;
	}

	/// <summary>手机 → 电脑。电脑上已有同名世界时先自动备份（导入逻辑里完成），收到的 zip 也留在备份目录。</summary>
	public static void Pull(SaveSync.Target t, string rootId, string name, Action<string> log = null)
	{
		if (!SaveSync.SafeWorldName(name))
		{
			throw new InvalidOperationException("世界名不合法：" + name);
		}
		PhoneClient.World remote = RemoteWorld(rootId, name) ?? throw new InvalidOperationException("手机上没有这个世界：" + name);
		string dir = Path.Combine(t.SavesDir, name);
		if (Directory.Exists(dir) && SaveSync.IsLocked(dir))
		{
			throw new InvalidOperationException("「" + name + "」正在游戏中打开，先退出世界再同步");
		}
		string zip = TempZip();
		try
		{
			log?.Invoke("下载「" + name + "」");
			Need().Download(rootId, name, zip);
			string bad = SaveSync.CheckZip(zip);
			if (bad != null)
			{
				throw new InvalidOperationException(bad);
			}
			Utils.EnsureDir(t.SavesDir);
			SaveSync.ImportZip(t, name, zip);
		}
		finally
		{
			try
			{
				if (File.Exists(zip))
				{
					File.Delete(zip);
				}
			}
			catch (Exception)
			{
			}
		}
		SetBase(t, rootId, name, SaveSync.WorldHash(dir), remote.LastModified);
	}

	/// <summary>
	/// 一键同步：一样的跳过，只有一边改过的按方向传，只在一边有的复制过去，两边都改过的问 resolve
	/// （返回 Push / Pull，其他值跳过）。返回每个世界的处理结果。
	/// </summary>
	public static List<string> SyncAll(SaveSync.Target t, string rootId, Func<Row, SyncDecision> resolve, Action<string> log = null)
	{
		List<string> done = new List<string>();
		foreach (Row r in Compare(t, rootId))
		{
			SyncDecision d = r.Decision;
			if (d == SyncDecision.Conflict)
			{
				d = resolve?.Invoke(r) ?? SyncDecision.Same;
			}
			try
			{
				switch (d)
				{
				case SyncDecision.Push:
				case SyncDecision.OnlyLocal:
					Push(t, rootId, r.Name, log);
					done.Add("→ 手机：" + r.Name);
					break;
				case SyncDecision.Pull:
				case SyncDecision.OnlyRemote:
					Pull(t, rootId, r.Name, log);
					done.Add("→ 电脑：" + r.Name);
					break;
				}
			}
			catch (Exception e)
			{
				done.Add("失败：" + r.Name + "（" + e.Message + "）");
				log?.Invoke("「" + r.Name + "」失败：" + e.Message);
			}
		}
		return done;
	}

	#endregion
}
