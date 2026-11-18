using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace HelL.Core.Launcher.Ai;

/// <summary>
/// 补充工具：电脑配置、卡顿诊断、JVM / GC 调优、存档备份回滚、陶瓦联机、账号皮肤。
/// 尽量复用启动器已有的后端能力，这里只做 AI 侧的包装。
/// </summary>
public static class ExtraTools
{
	public static readonly string[] Names = new string[14]
	{
		"get_system_info", "diagnose_lag", "tune_jvm", "list_saves", "backup_save", "list_save_backups", "restore_save_backup", "multiplayer", "list_accounts", "add_offline_account",
		"switch_account", "set_skin", "open_folder", "phone_sync"
	};

	public static readonly Dictionary<string, string> Labels = new Dictionary<string, string>
	{
		{ "get_system_info", "查看电脑配置" },
		{ "diagnose_lag", "诊断卡顿" },
		{ "tune_jvm", "调优内存和 GC" },
		{ "list_saves", "查看存档" },
		{ "backup_save", "备份存档" },
		{ "list_save_backups", "查看存档备份" },
		{ "restore_save_backup", "回滚存档" },
		{ "multiplayer", "联机" },
		{ "list_accounts", "查看账号" },
		{ "add_offline_account", "添加离线账号" },
		{ "switch_account", "切换账号" },
		{ "set_skin", "设置皮肤" },
		{ "open_folder", "打开文件夹" },
		{ "phone_sync", "手机同步" }
	};

	public static readonly ToolMetaInfo[] Meta = new ToolMetaInfo[14]
	{
		new ToolMetaInfo("get_system_info", ro: true, "read", "low", lr: false),
		new ToolMetaInfo("diagnose_lag", ro: true, "read", "low", lr: false),
		new ToolMetaInfo("tune_jvm", ro: false, "write_local", "medium", lr: false),
		new ToolMetaInfo("list_saves", ro: true, "read", "low", lr: false),
		new ToolMetaInfo("backup_save", ro: false, "write_local", "low", lr: false),
		new ToolMetaInfo("list_save_backups", ro: true, "read", "low", lr: false),
		new ToolMetaInfo("restore_save_backup", ro: false, "delete", "high", lr: false),
		new ToolMetaInfo("multiplayer", ro: false, "write_local", "medium", lr: false),
		new ToolMetaInfo("list_accounts", ro: true, "read", "low", lr: false),
		new ToolMetaInfo("add_offline_account", ro: false, "write_local", "low", lr: false),
		new ToolMetaInfo("switch_account", ro: false, "write_local", "low", lr: false),
		new ToolMetaInfo("set_skin", ro: false, "write_local", "low", lr: false),
		new ToolMetaInfo("open_folder", ro: true, "none", "low", lr: false),
		new ToolMetaInfo("phone_sync", ro: false, "write_local", "low", lr: false)
	};

	public static readonly (string[] keys, string[] extra)[] Keywords = new(string[], string[])[6]
	{
		(new string[14] { "卡顿", "很卡", "掉帧", "帧数", "fps", "FPS", "lag", "优化", "流畅", "内存", "JVM", "jvm", "GC", "电脑配置" }, new string[3] { "get_system_info", "diagnose_lag", "tune_jvm" }),
		(new string[9] { "备份", "回滚", "还原", "恢复", "存档", "坏档", "世界", "save", "backup" }, new string[5] { "list_saves", "backup_save", "list_save_backups", "restore_save_backup", "open_folder" }),
		(new string[9] { "联机", "开房", "房间", "朋友一起", "一起玩", "局域网", "陶瓦", "lan", "multiplayer" }, new string[1] { "multiplayer" }),
		(new string[7] { "皮肤", "账号", "账户", "披风", "离线", "skin", "account" }, new string[4] { "list_accounts", "add_offline_account", "switch_account", "set_skin" }),
		(new string[3] { "文件夹", "目录", "folder" }, new string[1] { "open_folder" }),
		(new string[8] { "手机", "平板", "同步", "安卓", "配对", "传到", "phone", "sync" }, new string[2] { "phone_sync", "list_saves" })
	};

	private const string SchemasJson = "[" +
		"{\"type\": \"function\", \"function\": {\"name\": \"get_system_info\", \"description\": \"查看电脑配置：系统、CPU、显卡、内存总量和可用量、已装 Java。判断卡顿、分配内存前先看。\", \"parameters\": {\"type\": \"object\", \"properties\": {}}}}, " +
		"{\"type\": \"function\", \"function\": {\"name\": \"diagnose_lag\", \"description\": \"诊断游戏卡顿/掉帧：扫 latest.log 里的 Can't keep up、内存溢出、GC 告警，对比内存分配和电脑内存，检查是否缺少常见优化模组。\", \"parameters\": {\"type\": \"object\", \"properties\": {\"instance\": {\"type\": \"string\"}}}}}, " +
		"{\"type\": \"function\", \"function\": {\"name\": \"tune_jvm\", \"description\": \"按电脑内存和模组数量推荐游戏内存与 GC 方案。apply=false 只给建议；apply=true 写入全局设置（之后启动生效）。也可以直接传 memory_mb / gc 指定。\", \"parameters\": {\"type\": \"object\", \"properties\": {\"instance\": {\"type\": \"string\"}, \"apply\": {\"type\": \"boolean\", \"description\": \"是否写入设置，默认 false\"}, \"memory_mb\": {\"type\": \"integer\", \"description\": \"可选，手动指定内存（MB）\"}, \"gc\": {\"type\": \"string\", \"description\": \"可选：auto / g1 / g1_tuned / zgc / none\"}}}}}, " +
		"{\"type\": \"function\", \"function\": {\"name\": \"list_saves\", \"description\": \"列出实例里的存档（世界）\", \"parameters\": {\"type\": \"object\", \"properties\": {\"instance\": {\"type\": \"string\"}, \"version\": {\"type\": \"string\", \"description\": \"版本隔离时填版本号，一般留空\"}}}}}, " +
		"{\"type\": \"function\", \"function\": {\"name\": \"backup_save\", \"description\": \"把一个存档打包备份（zip）。改模组、升级版本、装整合包前建议先备份。\", \"parameters\": {\"type\": \"object\", \"properties\": {\"name\": {\"type\": \"string\", \"description\": \"存档文件夹名\"}, \"instance\": {\"type\": \"string\"}, \"version\": {\"type\": \"string\"}}, \"required\": [\"name\"]}}}, " +
		"{\"type\": \"function\", \"function\": {\"name\": \"list_save_backups\", \"description\": \"列出存档备份，可按存档名过滤\", \"parameters\": {\"type\": \"object\", \"properties\": {\"name\": {\"type\": \"string\"}, \"instance\": {\"type\": \"string\"}, \"version\": {\"type\": \"string\"}}}}}, " +
		"{\"type\": \"function\", \"function\": {\"name\": \"restore_save_backup\", \"description\": \"从备份回滚存档。overwrite=true 会覆盖同名存档，覆盖前自动再备份一次当前存档；false 则恢复成新的存档文件夹。\", \"parameters\": {\"type\": \"object\", \"properties\": {\"backup_name\": {\"type\": \"string\", \"description\": \"list_save_backups 返回的备份文件名\"}, \"overwrite\": {\"type\": \"boolean\"}, \"instance\": {\"type\": \"string\"}, \"version\": {\"type\": \"string\"}}, \"required\": [\"backup_name\"]}}}, " +
		"{\"type\": \"function\", \"function\": {\"name\": \"multiplayer\", \"description\": \"陶瓦联机（不用公网 IP 和朋友一起玩）。action：status 查看状态；host 开房（先在游戏里打开单人世界并对局域网开放）；join 用房间号加入；stop 退出联机。\", \"parameters\": {\"type\": \"object\", \"properties\": {\"action\": {\"type\": \"string\", \"description\": \"status / host / join / stop\"}, \"room\": {\"type\": \"string\", \"description\": \"join 时填朋友给的房间号\"}}, \"required\": [\"action\"]}}}, " +
		"{\"type\": \"function\", \"function\": {\"name\": \"list_accounts\", \"description\": \"列出启动器里的游戏账号（正版 / 离线 / 外置），以及当前用哪个、皮肤模型\", \"parameters\": {\"type\": \"object\", \"properties\": {}}}}, " +
		"{\"type\": \"function\", \"function\": {\"name\": \"add_offline_account\", \"description\": \"添加离线账号\", \"parameters\": {\"type\": \"object\", \"properties\": {\"username\": {\"type\": \"string\", \"description\": \"3-16 位英文、数字或下划线\"}}, \"required\": [\"username\"]}}}, " +
		"{\"type\": \"function\", \"function\": {\"name\": \"switch_account\", \"description\": \"切换当前使用的账号\", \"parameters\": {\"type\": \"object\", \"properties\": {\"name\": {\"type\": \"string\"}}, \"required\": [\"name\"]}}}, " +
		"{\"type\": \"function\", \"function\": {\"name\": \"set_skin\", \"description\": \"给账号换皮肤：传本地 png 皮肤文件路径（可以是用户刚附的图片），model 为 classic（粗手臂）或 slim（细手臂）\", \"parameters\": {\"type\": \"object\", \"properties\": {\"name\": {\"type\": \"string\", \"description\": \"账号名\"}, \"path\": {\"type\": \"string\", \"description\": \"皮肤 png 路径\"}, \"model\": {\"type\": \"string\", \"description\": \"classic / slim\"}}, \"required\": [\"name\", \"path\"]}}}, " +
		"{\"type\": \"function\", \"function\": {\"name\": \"open_folder\", \"description\": \"在资源管理器里打开实例的文件夹，方便用户自己放文件\", \"parameters\": {\"type\": \"object\", \"properties\": {\"kind\": {\"type\": \"string\", \"description\": \"game / mods / saves / resourcepacks / shaderpacks / config / logs / backups\"}, \"instance\": {\"type\": \"string\"}}}}}, " +
		"{\"type\": \"function\", \"function\": {\"name\": \"phone_sync\", \"description\": \"和手机启动器互传 Java 版存档。手机上要先打开：启动器 → 存档管理 → 从 zip 导入 → 启动同步服务，屏幕会显示地址和 6 位配对码。action：status 查看连接；connect 用 address+code 连接（局域网）；usb 用 code 通过 USB 数据线连接（需手机开 USB 调试）；scan 扫描局域网里的手机；compare 对比两边世界；push 把电脑上的 world 传到手机；pull 把手机上的 world 传到电脑；sync 一键同步（两边都改过的世界会跳过，需要问玩家后再用 push/pull）。被覆盖的一边都会先自动备份。\", \"parameters\": {\"type\": \"object\", \"properties\": {\"action\": {\"type\": \"string\", \"description\": \"status / connect / usb / scan / compare / push / pull / sync\"}, \"address\": {\"type\": \"string\", \"description\": \"手机 IP，可带端口\"}, \"code\": {\"type\": \"string\", \"description\": \"手机上显示的 6 位配对码\"}, \"instance\": {\"type\": \"string\"}, \"version\": {\"type\": \"string\", \"description\": \"开了版本隔离时的版本名\"}, \"root\": {\"type\": \"string\", \"description\": \"手机上的位置 id，默认自动对应（global 或 v:版本名）\"}, \"world\": {\"type\": \"string\", \"description\": \"世界文件夹名\"}}, \"required\": [\"action\"]}}}" +
		"]";

	public static List<object> Schemas()
	{
		return (List<object>)PyJson.Loads(SchemasJson);
	}

	public static bool Handles(string name)
	{
		return Names.Contains(name);
	}

	public static object Run(BackendApi backend, string name, PyDict args)
	{
		args = args ?? new PyDict();
		switch (name)
		{
		case "get_system_info":
			return Sysinfo.FormatText();
		case "diagnose_lag":
			return DiagnoseLag(backend, Tools._inst(backend, args));
		case "tune_jvm":
			return TuneJvm(backend, args);
		case "list_saves":
			return Saves.ListSaves(Tools._inst(backend, args), Ver(args)).OfType<PyDict>().Select((PyDict s) => PyDict.Of("name", s.Get("name"), "size_mb", Math.Round(Py.Int(Py.Or(s.Get("bytes"), 0L)) / 1048576.0, 1), "modified", Stamp(s.Get("mtime")))).Cast<object>().ToList();
		case "backup_save":
		{
			PyDict r = Saves.BackupSave(Tools._inst(backend, args), Py.S(args.Get("name")), Ver(args));
			return "已备份：" + Py.S(r.Get("name"));
		}
		case "list_save_backups":
			return Saves.ListBackups(Tools._inst(backend, args), Py.Truthy(args.Get("name")) ? args.Get("name") : null, Ver(args));
		case "restore_save_backup":
			return Restore(backend, args);
		case "multiplayer":
			return Multiplayer(backend, args);
		case "list_accounts":
			return ((IEnumerable<object>)backend.get_account_rows()).OfType<PyDict>().Select((PyDict a) => PyDict.Of("name", a.Get("name"), "type", a.Get("type"), "active", a.Get("active"), "skin_model", a.Get("skin_model"), "skin_file", a.Get("skin_file"))).Cast<object>().ToList();
		case "add_offline_account":
		{
			string text = Py.Strip(Py.S(args.Get("username")));
			if (!Regex.IsMatch(text, "^[A-Za-z0-9_]{3,16}$"))
			{
				return "用户名只能是 3-16 位英文、数字或下划线";
			}
			backend.add_offline_account(text);
			return "已添加离线账号 " + text;
		}
		case "switch_account":
			return "当前账号：" + Py.S(backend.set_active_account(Py.S(args.Get("name"))));
		case "set_skin":
		{
			string path = Py.S(args.Get("path"));
			if (!File.Exists(path) || !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
			{
				return "找不到 png 皮肤文件：" + path;
			}
			string model = Py.S(args.Get("model")) == "slim" ? "slim" : "classic";
			backend.set_account_skin(Py.S(args.Get("name")), path, model);
			return "已给 " + Py.S(args.Get("name")) + " 换上皮肤（" + model + "）。离线账号进游戏即生效；正版账号的皮肤需要到 Minecraft 官网更换。";
		}
		case "phone_sync":
			return PhoneSync(args);
		case "open_folder":
			return OpenFolder(backend, args);
		default:
			return null;
		}
	}

	private static object PhoneSync(PyDict args)
	{
		string action = Py.S(Py.Or(args.Get("action"), "status")).Trim().ToLowerInvariant();
		string code = Py.S(Py.Or(args.Get("code"), Sync.PhoneLink.LastToken));
		switch (action)
		{
		case "connect":
		{
			Sync.PhoneClient.Info info = Sync.PhoneLink.Connect(Py.S(Py.Or(args.Get("address"), Sync.PhoneLink.LastAddress)), code);
			return PyDict.Of("connected", true, "device", info.Device, "android", info.Android);
		}
		case "usb":
		{
			Sync.PhoneClient.Info info = Sync.PhoneLink.ConnectUsb(code);
			return PyDict.Of("connected", true, "device", info.Device, "android", info.Android, "via", "usb");
		}
		case "scan":
			return PyDict.Of("found", Sync.PhoneClient.ScanLan().Cast<object>().ToList());
		case "status":
			return Sync.PhoneLink.Current == null
				? PyDict.Of("connected", false, "last_address", Sync.PhoneLink.LastAddress, "hint", "让玩家在手机启动器里打开 存档管理 → 从 zip 导入 → 启动同步服务，把地址和配对码告诉你，再用 connect；插着数据线可以用 usb")
				: PyDict.Of("connected", true, "device", Sync.PhoneLink.CurrentInfo?.Device ?? "", "roots", Sync.PhoneLink.Roots().Select((Sync.PhoneClient.Root r) => (object)PyDict.Of("id", r.Id, "label", r.Label)).ToList());
		}
		if (Sync.PhoneLink.Current == null)
		{
			return "还没连接手机：先用 action=connect（address + code）或 action=usb（code）";
		}
		Sync.SaveSync.Target t = Sync.SaveSync.FindTarget(Py.S(args.Get("instance")), Py.S(args.Get("version")));
		if (t == null)
		{
			return "找不到这个实例或版本";
		}
		string root = Py.S(args.Get("root"));
		if (root.Length == 0)
		{
			root = Sync.PhoneLink.DefaultRoot(t, Sync.PhoneLink.Roots());
		}
		string world = Py.S(args.Get("world"));
		switch (action)
		{
		case "compare":
			return PyDict.Of("pc", t.Label, "phone_root", root, "worlds", Sync.PhoneLink.Compare(t, root).Select((Sync.PhoneLink.Row r) => (object)PyDict.Of("world", r.Name, "state", r.Decision.ToString(), "pc_modified", r.HasLocal ? Stamp(r.LocalMtime / 1000) : "", "phone_modified", r.HasRemote ? Stamp(r.Remote.LastModified / 1000) : "", "in_game", r.LocalLocked)).ToList(), "hint", "Push=电脑较新 Pull=手机较新 Conflict=两边都改过（要问玩家） OnlyLocal/OnlyRemote=只在一边");
		case "push":
			Sync.PhoneLink.Push(t, root, world);
			return "已把「" + world + "」传到手机（手机上原有的版本已备份到电脑）";
		case "pull":
			Sync.PhoneLink.Pull(t, root, world);
			return "已把「" + world + "」从手机传到电脑（电脑上原有的版本已自动备份）";
		case "sync":
		{
			List<string> skipped = new List<string>();
			List<string> done = Sync.PhoneLink.SyncAll(t, root, delegate(Sync.PhoneLink.Row r)
			{
				skipped.Add(r.Name);
				return Sync.SyncDecision.Same;
			});
			return PyDict.Of("done", done.Cast<object>().ToList(), "conflicts_skipped", skipped.Cast<object>().ToList());
		}
		default:
			return "不认识的 action：" + action;
		}
	}

	private static object Ver(PyDict args)
	{
		return Py.Truthy(args.Get("version")) ? args.Get("version") : null;
	}

	private static string Stamp(object mtime)
	{
		try
		{
			return DateTimeOffset.FromUnixTimeSeconds(Py.Int(mtime)).ToLocalTime().ToString("yyyy-MM-dd HH:mm");
		}
		catch (Exception)
		{
			return "";
		}
	}

	// ---------------- 卡顿诊断 ----------------

	private static readonly (string label, string[] keys)[] PerfMods = new(string, string[])[5]
	{
		("渲染优化（Sodium / Embeddium / OptiFine 之一）", new string[5] { "sodium", "embeddium", "rubidium", "optifine", "nvidium" }),
		("逻辑优化（Lithium / Canary / Radium）", new string[3] { "lithium", "canary", "radium" }),
		("内存优化（FerriteCore）", new string[1] { "ferritecore" }),
		("启动和杂项优化（ModernFix）", new string[1] { "modernfix" }),
		("实体剔除（Entity Culling）", new string[1] { "entityculling" })
	};

	public static string DiagnoseLag(BackendApi backend, Instance instance)
	{
		StringBuilder sb = new StringBuilder();
		PyDict mem = Sysinfo.MemoryInfo();
		long totalMb = Py.Int(Py.Or(mem.Get("total_mb"), 0L));
		long availMb = Py.Int(Py.Or(mem.Get("avail_mb"), 0L));
		long alloc = Py.Int(Py.Or(Config.CONFIG.Get("memory_mb"), 4096L));
		string gc = Py.S(Py.Or(Config.CONFIG.Get("gc_preset"), "auto"));
		List<string> mods = ModFiles(instance);
		sb.AppendLine($"电脑内存 {totalMb} MB（当前可用 {availMb} MB），游戏分配 {alloc} MB，GC 方案 {gc}，模组 {mods.Count} 个。");
		List<string> issues = new List<string>();
		if (totalMb > 0 && alloc > totalMb * 3 / 4)
		{
			issues.Add("分配的内存超过电脑内存的 3/4，系统会频繁换页导致卡顿，建议调小。");
		}
		long need = RecommendMemory(totalMb, mods.Count);
		if (alloc + 512 < need)
		{
			issues.Add($"模组较多，分配偏少，建议约 {need} MB。");
		}
		else if (alloc > need + 4096 && mods.Count < 60)
		{
			issues.Add($"分配过多也会让 GC 停顿变长，建议约 {need} MB。");
		}
		string log = "";
		try
		{
			log = Py.S(Diagnose.CollectLogs(instance).Get("latest"));
		}
		catch (Exception)
		{
		}
		if (log.Length > 0)
		{
			MatchCollection behind = Regex.Matches(log, "Can't keep up!.*?Running (\\d+)ms or (\\d+) ticks behind");
			if (behind.Count > 0)
			{
				long worst = behind.Cast<Match>().Max((Match m) => long.Parse(m.Groups[1].Value));
				issues.Add($"服务端/世界逻辑跟不上 {behind.Count} 次（最严重落后 {worst} ms），多半是实体、红石机器或区块加载太多，或者内存不足。");
			}
			if (log.Contains("OutOfMemoryError"))
			{
				issues.Add("日志里有内存溢出（OutOfMemoryError），需要加内存或减少模组/光影。");
			}
			if (log.Contains("GC overhead limit") || Regex.IsMatch(log, "Pause Full|Full GC"))
			{
				issues.Add("出现过 Full GC，内存紧张，建议调大内存或换成调优 G1。");
			}
			int warn = Regex.Matches(log, "/WARN\\]").Count;
			if (warn > 500)
			{
				issues.Add($"日志里有 {warn} 条警告，刷屏的模组可能拖慢游戏，可以看看是哪个模组在报。");
			}
		}
		else
		{
			sb.AppendLine("没有找到 latest.log（还没启动过，或日志在别的版本目录）。");
		}
		if (mods.Count > 0)
		{
			List<string> missing = (from p in PerfMods
				where !mods.Any((string f) => p.keys.Any((string k) => f.Contains(k)))
				select p.label).ToList();
			if (missing.Count > 0)
			{
				issues.Add("没装这些常见优化模组（要和加载器、版本匹配）：" + string.Join("、", missing) + "。");
			}
			if (mods.Any((string f) => f.Contains("optifine")) && mods.Any((string f) => f.Contains("sodium") || f.Contains("embeddium") || f.Contains("rubidium")))
			{
				issues.Add("OptiFine 和 Sodium/Embeddium 同时装了，会冲突，只留一个。");
			}
		}
		if (issues.Count == 0)
		{
			sb.AppendLine("没发现明显问题。若仍卡顿，多半是显卡驱动、光影太重、渲染距离太高，或笔记本没用独显。");
		}
		else
		{
			sb.AppendLine("发现：");
			foreach (string issue in issues)
			{
				sb.AppendLine("- " + issue);
			}
		}
		return sb.ToString().TrimEnd();
	}

	private static List<string> ModFiles(Instance instance)
	{
		try
		{
			string dir = Path.Combine(instance.path, "mods");
			if (!Directory.Exists(dir))
			{
				return new List<string>();
			}
			return (from f in Directory.GetFiles(dir)
				where f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
				select Path.GetFileName(f).ToLowerInvariant()).ToList();
		}
		catch (Exception)
		{
			return new List<string>();
		}
	}

	// ---------------- JVM 调优 ----------------

	/// <summary>按电脑内存和模组数推荐内存（MB），不超过电脑内存的 70%，也不超过 16 GB。</summary>
	public static long RecommendMemory(long totalMb, int modCount)
	{
		long want = modCount >= 200 ? 10240 : (modCount >= 120 ? 8192 : (modCount >= 60 ? 6144 : (modCount >= 1 ? 4096 : 3072)));
		if (totalMb > 0)
		{
			long cap = Math.Max(1024L, totalMb * 7 / 10);
			want = Math.Min(want, cap);
		}
		return Math.Min(16384L, want / 256 * 256);
	}

	public static string RecommendGc(long memoryMb, int javaMajor)
	{
		if (javaMajor >= 21 && memoryMb >= 12288)
		{
			return "zgc";
		}
		return memoryMb >= 4096 ? "g1_tuned" : "auto";
	}

	private static object TuneJvm(BackendApi backend, PyDict args)
	{
		Instance instance = Tools._inst(backend, args);
		long totalMb = Py.Int(Py.Or(Sysinfo.MemoryInfo().Get("total_mb"), 0L));
		int mods = ModFiles(instance).Count;
		long cur = Py.Int(Py.Or(Config.CONFIG.Get("memory_mb"), 4096L));
		string curGc = Py.S(Py.Or(Config.CONFIG.Get("gc_preset"), "auto"));
		long mem = Py.Truthy(args.Get("memory_mb")) ? Py.Int(args.Get("memory_mb")) : RecommendMemory(totalMb, mods);
		if (totalMb > 0 && mem > totalMb - 1024)
		{
			return $"电脑只有 {totalMb} MB 内存，分配 {mem} MB 会让系统卡死，最多建议 {Math.Max(1024L, totalMb * 7 / 10)} MB。";
		}
		string gc = Py.Truthy(args.Get("gc")) ? Py.S(args.Get("gc")) : RecommendGc(mem, 17);
		if (!Py.In(gc, Gc.LABELS.Keys.Cast<object>().ToList()))
		{
			return "gc 只能是 auto / g1 / g1_tuned / zgc / none";
		}
		string head = $"电脑内存 {totalMb} MB，模组 {mods} 个。当前：{cur} MB，{Py.S(Gc.LABELS.Get(curGc))}。建议：{mem} MB，{Py.S(Gc.LABELS.Get(gc))}。";
		if (!Py.Truthy(args.Get("apply")))
		{
			return head + "（还没改，确认后用 apply=true 写入）";
		}
		backend.save_settings(PyDict.Of("default_memory_mb", mem, "gc_preset", gc));
		return head + "已写入设置，下次启动生效。若某个版本单独设置过内存，那个版本仍以它自己的设置为准。";
	}

	// ---------------- 存档回滚 ----------------

	private static object Restore(BackendApi backend, PyDict args)
	{
		Instance instance = Tools._inst(backend, args);
		object ver = Ver(args);
		string backup = Py.S(args.Get("backup_name"));
		bool overwrite = Py.Truthy(args.Get("overwrite"));
		string extra = "";
		if (overwrite)
		{
			string world = Saves.StripStamp(Path.GetFileNameWithoutExtension(backup));
			if (Saves.ListSaves(instance, ver).OfType<PyDict>().Any((PyDict s) => Py.S(s.Get("name")) == world))
			{
				PyDict r = Saves.BackupSave(instance, world, ver);
				extra = "覆盖前已把当前存档备份为 " + Py.S(r.Get("name")) + "。";
			}
		}
		PyDict res = Saves.RestoreBackup(instance, backup, ver, "", overwrite);
		return "已从 " + backup + " 恢复到存档「" + Py.S(Py.Or(res.Get("name"), res.Get("target"), "")) + "」。" + extra;
	}

	// ---------------- 联机 ----------------

	private static object Multiplayer(BackendApi backend, PyDict args)
	{
		string action = Py.S(args.Get("action")).ToLowerInvariant();
		PyDict snap = (PyDict)backend.terracotta_snapshot();
		if (action != "status" && !Py.Truthy(snap.Get("supported")))
		{
			return "当前系统不支持陶瓦联机";
		}
		if ((action == "host" || action == "join") && !Py.Truthy(snap.Get("installed")))
		{
			backend.terracotta_prepare();
			return "陶瓦联机组件还没装，已开始下载（看左侧「下载任务」），装好后再试一次。";
		}
		switch (action)
		{
		case "host":
			backend.terracotta_host();
			return "已开始开房：请先在游戏里打开单人世界，按 Esc →「对局域网开放」。几秒后用 multiplayer status 拿房间号发给朋友。";
		case "join":
		{
			string room = Py.Strip(Py.S(args.Get("room")));
			if (room.Length == 0)
			{
				return "要填朋友给的房间号";
			}
			backend.terracotta_join(room);
			return "正在加入房间 " + room + "。连上后进游戏「多人游戏」，双击「陶瓦联机大厅」。";
		}
		case "stop":
			backend.terracotta_idle();
			return "已退出联机";
		default:
			return PyDict.Of("installed", snap.Get("installed"), "running", snap.Get("running"), "state", snap.Get("label"), "room", snap.Get("room"), "error", snap.Get("error"), "game_running", snap.Get("game_running"));
		}
	}

	// ---------------- 文件夹 ----------------

	private static object OpenFolder(BackendApi backend, PyDict args)
	{
		Instance instance = Tools._inst(backend, args);
		string kind = Py.S(Py.Or(args.Get("kind"), "game")).ToLowerInvariant();
		string[] allowed = new string[7] { "mods", "saves", "resourcepacks", "shaderpacks", "config", "logs", "backups" };
		string dir = instance.path;
		if (kind == "backups")
		{
			dir = Saves.BackupsDir(instance);
		}
		else if (allowed.Contains(kind))
		{
			dir = Path.Combine(instance.path, kind);
		}
		Directory.CreateDirectory(dir);
		try
		{
			System.Diagnostics.Process.Start("explorer.exe", "\"" + dir + "\"");
		}
		catch (Exception ex)
		{
			return "打不开：" + ex.Message;
		}
		return "已打开 " + dir;
	}
}
