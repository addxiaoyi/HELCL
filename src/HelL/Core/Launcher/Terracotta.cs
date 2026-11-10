using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace HelL.Core.Launcher;

public static class Terracotta
{
	private sealed class Pkg
	{
		public string hash;

		public string exe;

		public (string name, string sha)[] files;
	}

	private sealed class Buf
	{
		private readonly byte[] _b;

		private int _pos;

		public Buf(byte[] b)
		{
			_b = b;
		}

		public byte[] Read(int n)
		{
			if (n < 0)
			{
				n = _b.Length - _pos;
			}
			int num = Math.Max(0, Math.Min(n, _b.Length - _pos));
			byte[] array = new byte[num];
			Array.Copy(_b, _pos, array, 0, num);
			_pos += num;
			return array;
		}
	}

	public class StructError : PyException
	{
		public StructError(string m)
			: base("error", m)
		{
		}
	}

	public const string VERSION = "0.4.2";

	public const string NODE_LIST_URL = "https://terracotta.glavo.site/nodes";

	public const string HOME = "https://github.com/burningtnt/Terracotta";

	public const string COPYRIGHT = "Terracotta | 陶瓦联机  © burningtnt  ·  基于 EasyTier";

	private const string _ROOM_CHARS = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ";

	private static readonly int _ROOM_WIDTH = "U/XXXX-XXXX-XXXX-XXXX".Length;

	private const string _ROOM_BODY = "XXXX-XXXX-XXXX-XXXX";

	private static readonly Dictionary<string, Pkg> _PACKAGES = new Dictionary<string, Pkg>
	{
		["windows-x86_64"] = new Pkg
		{
			hash = "6a98f524d4f00373696517306af8aa50d01d55ce4eadb27e9e4bc2f882707a0b5f20d5d4c33371d1459dcf5bf144ffed9beb414202d9ccf32b11dbbfcf19d650",
			files = new(string, string)[2]
			{
				("VCRUNTIME140.DLL", "3d4b24061f72c0e957c7b04a0c4098c94c8f1afb4a7e159850b9939c7210d73398be6f27b5ab85073b4e8c999816e7804fef0f6115c39cd061f4aaeb4dcda8cf"),
				("terracotta-0.4.2-windows-x86_64.exe", "6e98d1f2380ed22fb5a2dd4aafce6c773e9cf69100c8bb8e49e7d6983756bdb9a31f80e06bcfbe5a2742144fe806d3d687dec54d8f09d87c659341f99dd9fd80")
			},
			exe = "terracotta-0.4.2-windows-x86_64.exe"
		},
		["windows-arm64"] = new Pkg
		{
			hash = "fc1077247014ac0c712469498bde2ef7f6d881d5fcb7bdd5e11ebe20218fed365be19afdb8d453a79d77b729f866058522b910741767f4df947faa891434b463",
			files = new(string, string)[2]
			{
				("VCRUNTIME140.DLL", "5cb5ce114614101d260f4754c09e8a0dd57e4da885ebb96b91e274326f3e1dd95ed0ade9f542f1922fad0ed025e88a1f368e791e1d01fae69718f0ec3c7b98c8"),
				("terracotta-0.4.2-windows-arm64.exe", "30a15c5c53e5817c5a3634532172559327474741d3b2c7ef4e8a30acc6f59cdcf3570bf5f583e3cbe9e2abc8253e977c1abda1e9f36c88c4e99240da257347d0")
			},
			exe = "terracotta-0.4.2-windows-arm64.exe"
		},
		["linux-x86_64"] = new Pkg
		{
			hash = "d326ad95815d04568d485b5038e40ffc47ca54292fa0925eee6f5cea014024f901d661708aac2a743037b990882ad82b4d0b7bb03dc3b2fe720dbf0f3efe1c98",
			files = new(string, string)[1] { ("terracotta-0.4.2-linux-x86_64", "fac328ba8957a711b03557bb913940f22d61b76608cd203fdf51024b6f94b19f5bc91c9b8a9fa80baf6968e1e6873c1880fd4cf54a2f8e3c6cf1e6ac161f8d0c") },
			exe = "terracotta-0.4.2-linux-x86_64"
		},
		["linux-arm64"] = new Pkg
		{
			hash = "57c08f48d9535e93ad547d2dfc852d267992cc164a7208b42a2da0a6cbc2f21862f610e02a746b4b67150f4dec26b86a4f96eb9bd2f58d124d5b40ba50c6d55e",
			files = new(string, string)[1] { ("terracotta-0.4.2-linux-arm64", "d807744c2041c98686e4b505324713badea7a0f31e8810be49ae053a63fb6dfc474ac58d678fb93eea0dd5cccff7372d9ec6135a1046f4b306cad35cd90ecacd") },
			exe = "terracotta-0.4.2-linux-arm64"
		},
		["macos-x86_64"] = new Pkg
		{
			hash = "a762e4b2d6f84e899292b9e3856d009411a516d3c47f54575f843ce082f63dff2baa68ba0faa844b8b64fb12e91017386f15f5e7f975f8ee605bf8d4217cb091",
			files = new(string, string)[1] { ("terracotta-0.4.2-macos-x86_64", "24efb85390eff88a538ed7e503fb1488e5e622730ca30c741a0e8b4c8f4e8d4868a2f9f38da8de540aeb535af2fd1e41c7081dae9c700e8a1a03b6c540218164") },
			exe = "terracotta-0.4.2-macos-x86_64"
		},
		["macos-arm64"] = new Pkg
		{
			hash = "09e444fea2d9fd19f3e5cb62e29055228345be163924cbd408d947646fafed1012cf48508ee6a155ede3d571e2ffaa72d09ceeb1493c8a60feb05e0699f19ba3",
			files = new(string, string)[1] { ("terracotta-0.4.2-macos-arm64", "8e59a9d78acd57702dc044d6f2799c6af586b075a262f7d4dbbf0876e1af8d8271e04783c24ff820b801e3b14cd0190ab8403d097f3f2d98b6d911f95ed1e972") },
			exe = "terracotta-0.4.2-macos-arm64"
		}
	};

	private static readonly string[] _EXC = new string[6] { "加入房间失败：找不到房主。房间已关闭，或尚未连上公共中继", "房间连接断开：房间已关闭或网络不稳定", "加入房间失败：EasyTier 已崩溃，请向开发者反馈该问题", "创建房间失败：EasyTier 已崩溃，请向开发者反馈该问题", "房间已关闭：您已退出游戏世界，房间已自动关闭", "协议错误：房主发送了错误的响应数据，请向开发者反馈该问题" };

	private const string _PING_HOST_HINT = "陶瓦是 EasyTier P2P 打洞，不是 FRP 隧道。官方公共节点连不上时，必须和 HMCL 用同一条自定义会合节点。请完全退出后重试；已带上本机 HMCL 成功加入时用的那条 terracotta.glavo.site 节点。";

	private static readonly Dictionary<string, string> _DIFF = new Dictionary<string, string>
	{
		["EASIEST"] = "当前网络状态极好：稍等一下就成功！",
		["SIMPLE"] = "当前网络状态较好：建立连接需要一段时间……",
		["MEDIUM"] = "当前网络状态中等：已启用抗干扰备用线路，连接可能失败",
		["TOUGH"] = "当前网络状态极差：已启用抗干扰备用线路，连接可能失败"
	};

	private static readonly Dictionary<string, string> _STATE_LABEL = new Dictionary<string, string>
	{
		["missing"] = "未下载联机核心",
		["unsupported"] = "当前系统架构暂不支持陶瓦联机",
		["installing"] = "正在下载联机核心…",
		["launching"] = "正在初始化联机核心",
		["unknown"] = "正在初始化联机核心",
		["waiting"] = "联机核心已就绪",
		["host-scanning"] = "正在扫描局域网世界",
		["host-starting"] = "正在启动房间",
		["host-ok"] = "已启动房间",
		["guest-connecting"] = "正在加入房间",
		["guest-starting"] = "正在加入房间",
		["guest-ok"] = "已加入房间",
		["exception"] = "联机出错",
		["fatal"] = "联机内核已停止"
	};

	private static string _last_lobby = "";

	private static readonly object _lock = new object();

	private static Process _proc;

	private static long _port;

	private static List<object> _nodes;

	private static HashSet<string> _allowed_fw = new HashSet<string>();

	public const string HMCL_CUSTOM_NODE = "https://terracotta.glavo.site/acebc7d8-1208-47fd-b212-d03ac49e36e0";

	public const string LOBBY_NAME = "陶瓦联机大厅";

	private static readonly Encoding Utf8Replace = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

	private static uint[] _crcTable;

	private static int? _lookup_room_char(char c)
	{
		switch (c)
		{
		case 'I':
			c = '1';
			break;
		case 'O':
			c = '0';
			break;
		}
		int num = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ".IndexOf(c);
		if (num < 0)
		{
			return null;
		}
		return num;
	}

	private static string _room_from_value(BigInteger value)
	{
		StringBuilder stringBuilder = new StringBuilder("U/");
		for (int i = 0; i < 16; i++)
		{
			char value2 = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ"[(int)(value % 34)];
			value /= (BigInteger)34;
			if (i == 4 || i == 8 || i == 12)
			{
				stringBuilder.Append('-');
			}
			stringBuilder.Append(value2);
		}
		return stringBuilder.ToString();
	}

	private static List<string> Chars(string text)
	{
		string text2 = Py.Upper(text ?? "");
		List<string> list = new List<string>();
		for (int i = 0; i < text2.Length; i++)
		{
			if (char.IsHighSurrogate(text2[i]) && i + 1 < text2.Length && char.IsLowSurrogate(text2[i + 1]))
			{
				list.Add(text2.Substring(i, 2));
				i++;
			}
			else
			{
				list.Add(text2[i].ToString());
			}
		}
		return list;
	}

	private static int? Lookup(string s)
	{
		if (s.Length != 1)
		{
			return null;
		}
		return _lookup_room_char(s[0]);
	}

	public static string ParseRoom(string text)
	{
		List<string> list = Chars(text);
		if (list.Count < _ROOM_WIDTH)
		{
			return null;
		}
		int length = "XXXX-XXXX-XXXX-XXXX".Length;
		for (int i = 0; i < list.Count - _ROOM_WIDTH + 1; i++)
		{
			List<string> range = list.GetRange(i, _ROOM_WIDTH);
			if (range[0] != "U" || range[1] != "/")
			{
				continue;
			}
			List<string> list2 = range.Skip(2).ToList();
			BigInteger bigInteger = 0;
			bool flag = true;
			for (int num = length - 1; num >= 0; num--)
			{
				if (num == 4 || num == 9 || num == 14)
				{
					if (list2[num] != "-")
					{
						flag = false;
						break;
					}
				}
				else
				{
					int? num2 = Lookup(list2[num]);
					if (!num2.HasValue)
					{
						flag = false;
						break;
					}
					bigInteger = bigInteger * 34 + num2.Value;
				}
			}
			if (flag && bigInteger % 7 == 0L)
			{
				return _room_from_value(bigInteger);
			}
		}
		return null;
	}

	public static bool LooksLikeLegacyRoom(string text)
	{
		List<string> list = Chars(text);
		if (list.Count < 29)
		{
			return false;
		}
		for (int i = 0; i < list.Count - 28; i++)
		{
			List<string> range = list.GetRange(i, 29);
			List<int> list2 = new List<int>();
			bool flag = true;
			for (int j = 0; j < 5; j++)
			{
				for (int k = 0; k < 5; k++)
				{
					int? num = Lookup(range[j * 6 + k]);
					if (!num.HasValue)
					{
						flag = false;
						break;
					}
					list2.Add(num.Value);
				}
				if (!flag)
				{
					break;
				}
				if (j != 4 && range[j * 6 + 5] != "-")
				{
					flag = false;
					break;
				}
			}
			if (flag && list2.Count == 25)
			{
				int num2 = 0;
				for (int l = 0; l < 24; l++)
				{
					num2 = (num2 + list2[l]) % 34;
				}
				if (num2 == list2[24])
				{
					return true;
				}
			}
		}
		return false;
	}

	public static bool LooksLikePcl2ceRoom(string text)
	{
		List<string> list = Chars(Py.Strip(text ?? ""));
		if (list.Count == 0 || list.Count > 10)
		{
			return false;
		}
		BigInteger bigInteger = 0;
		foreach (string item in list)
		{
			if (item.Length != 1)
			{
				return false;
			}
			char c = item[0];
			if (c >= '2' && c <= '9')
			{
				bigInteger = bigInteger * 32 + (c - 50);
				continue;
			}
			if (c >= 'A' && c <= 'H')
			{
				bigInteger = bigInteger * 32 + (c - 65 + 8);
				continue;
			}
			if (c >= 'J' && c <= 'N')
			{
				bigInteger = bigInteger * 32 + (c - 74 + 16);
				continue;
			}
			if (c >= 'P' && c <= 'Z')
			{
				bigInteger = bigInteger * 32 + (c - 80 + 21);
				continue;
			}
			return false;
		}
		if (bigInteger >= BigInteger.Parse("9999999999965536"))
		{
			return false;
		}
		string text2 = bigInteger.ToString();
		if (text2.Length == 14)
		{
			return true;
		}
		if (text2.Length == 15)
		{
			return bigInteger % 100000 < 65536L;
		}
		return false;
	}

	public static string RoomError(string text)
	{
		string text2 = Py.Strip(text ?? "");
		if (text2.Length == 0)
		{
			return "请输入房间号。";
		}
		if (LooksLikeLegacyRoom(text2))
		{
			return "这是陶瓦旧版房间号。官方 0.4.2 已移除 TerracottaLegacy 格式，请让房主用当前陶瓦 / HMCL 重新开房（房间号以 U/ 开头）。";
		}
		if (LooksLikePcl2ceRoom(text2))
		{
			return "这是 PCL CE 旧房间号。官方 0.4.2 已移除 PCL2CE 格式，请双方都用当前陶瓦 / HMCL 开房，房间号形如 U/XXXX-XXXX-XXXX-XXXX。";
		}
		string text3 = Py.Upper(text2);
		if (text3.Contains("U/") || text3.StartsWith("U"))
		{
			return "房间号校验失败。请向房主重新复制完整的 U/XXXX-XXXX-XXXX-XXXX。";
		}
		return "房间号须为陶瓦 0.4.2 格式 U/XXXX-XXXX-XXXX-XXXX。官方内核已不再接受旧版 PCL / 旧陶瓦房间号。";
	}

	public static string Classifier()
	{
		string oS_NAME = Utils.OS_NAME;
		string aRCH = Utils.ARCH;
		switch (oS_NAME)
		{
		case "windows":
			if (!(aRCH != "arm64"))
			{
				return "windows-arm64";
			}
			return "windows-x86_64";
		case "osx":
			if (!(aRCH == "arm64"))
			{
				return "macos-x86_64";
			}
			return "macos-arm64";
		case "linux":
			if (!(aRCH == "arm64"))
			{
				return "linux-x86_64";
			}
			return "linux-arm64";
		default:
			return "";
		}
	}

	private static Pkg PackageMeta()
	{
		if (!_PACKAGES.TryGetValue(Classifier(), out Pkg value))
		{
			return null;
		}
		return value;
	}

	public static string InstallDir()
	{
		return PPath.Join(Utils.ROOT, "terracotta", "0.4.2");
	}

	public static string Executable()
	{
		Pkg pkg = PackageMeta();
		if (pkg == null)
		{
			throw new TerracottaError("当前系统架构暂不支持陶瓦联机。");
		}
		return PPath.Join(InstallDir(), pkg.exe);
	}

	private static bool IsFile(string p)
	{
		return File.Exists(Utils.AbsPath(p));
	}

	private static string Resolve(string p)
	{
		try
		{
			return Path.GetFullPath(Utils.AbsPath(p));
		}
		catch (Exception)
		{
			return p;
		}
	}

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
	private static extern uint GetShortPathNameW(string longPath, StringBuilder shortPath, uint len);

	private static string _short_path(string path)
	{
		string text = Resolve(path);
		if (!Utils.IS_WINDOWS)
		{
			return text;
		}
		try
		{
			StringBuilder stringBuilder = new StringBuilder(520);
			return (GetShortPathNameW(text, stringBuilder, 520u) != 0) ? stringBuilder.ToString() : text;
		}
		catch (Exception)
		{
			return text;
		}
	}

	public static List<string> FirewallPrograms()
	{
		List<string> found = new List<string>();
		HashSet<string> seen = new HashSet<string>();
		try
		{
			add(Executable());
		}
		catch (TerracottaError)
		{
		}
		string path = Path.Combine(Path.GetTempPath(), "terracotta");
		if (Directory.Exists(path))
		{
			try
			{
				foreach (string item2 in Directory.EnumerateFiles(path, "*.exe", SearchOption.AllDirectories))
				{
					string text = Path.GetFileName(item2).ToLowerInvariant();
					if (text.Contains("easytier") || text.Contains("terracotta"))
					{
						add(item2);
					}
				}
			}
			catch (Exception)
			{
			}
		}
		return found;
		void add(string text2)
		{
			try
			{
				text2 = Resolve(text2);
			}
			catch (Exception)
			{
				return;
			}
			if (File.Exists(text2))
			{
				string item = text2.ToLowerInvariant();
				if (!seen.Contains(item))
				{
					seen.Add(item);
					found.Add(text2);
				}
			}
		}
	}

	public static void OpenFirewallSettings()
	{
		if (!Utils.IS_WINDOWS)
		{
			return;
		}
		try
		{
			Process.Start(new ProcessStartInfo("control", "firewall.cpl")
			{
				UseShellExecute = false
			})?.Dispose();
		}
		catch (Exception)
		{
		}
	}

	public static string AllowFirewall()
	{
		if (!Utils.IS_WINDOWS)
		{
			throw new TerracottaError("当前系统请在系统防火墙里手动放行陶瓦联机。");
		}
		List<string> list = FirewallPrograms();
		if (list.Count == 0)
		{
			throw new TerracottaError("还没找到陶瓦内核，请先点下载/启动，再允许防火墙。");
		}
		List<string> list2 = new List<string> { "@echo off", "chcp 65001 >nul" };
		for (int i = 0; i < list.Count; i++)
		{
			string text = _short_path(list[i]);
			string[] array = new string[2] { "in", "out" };
			foreach (string text2 in array)
			{
				string text3 = $"HelL Terracotta {i} {text2}";
				list2.Add("netsh advfirewall firewall delete rule name=\"" + text3 + "\" >nul 2>nul");
				list2.Add("netsh advfirewall firewall add rule name=\"" + text3 + "\" dir=" + text2 + " action=allow program=\"" + text + "\" enable=yes profile=any");
			}
		}
		list2.Add("netsh advfirewall firewall delete rule name=\"HelL Terracotta ICMPv4\" >nul 2>nul");
		list2.Add("netsh advfirewall firewall add rule name=\"HelL Terracotta ICMPv4\" protocol=icmpv4:8,any dir=in action=allow enable=yes profile=any");
		list2.Add("exit /b 0");
		string text4 = PPath.Join(Utils.ROOT, "terracotta", "allow-firewall.bat");
		Utils.EnsureDir(new PPath(text4).Parent.ToString());
		File.WriteAllBytes(Utils.AbsPath(text4), GbkBytes(string.Join("\r\n", list2) + "\r\n"));
		string item = "Start-Process -FilePath " + Py.Repr(text4) + " -Verb RunAs -Wait -WindowStyle Hidden";
		string text5;
		string result;
		int exitCode;
		using (Process process = Process.Start(new ProcessStartInfo("powershell")
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			Arguments = Utils.List2CmdLine(new List<string> { "-NoProfile", "-WindowStyle", "Hidden", "-Command", item }),
			StandardOutputEncoding = Encoding.UTF8,
			StandardErrorEncoding = Encoding.UTF8
		}))
		{
			Task<string> task = process.StandardError.ReadToEndAsync();
			text5 = process.StandardOutput.ReadToEnd();
			result = task.Result;
			process.WaitForExit();
			exitCode = process.ExitCode;
		}
		if (exitCode != 0)
		{
			string text6 = Py.Strip(Py.Truthy(result) ? result : (text5 ?? ""));
			if (Py.Lower(text6).Contains("canceled") || text6.Contains("1223") || exitCode == 1223)
			{
				throw new TerracottaError("已取消管理员授权，防火墙规则没有写入。");
			}
			throw new TerracottaError((text6.Length > 0) ? text6 : "写入防火墙规则失败，请在 UAC 窗口点「是」。");
		}
		_allowed_fw = new HashSet<string>(list.Select((string p) => Resolve(p).ToLowerInvariant()));
		string text7 = string.Join("、", list.Take(4).Select(Path.GetFileName));
		return "已允许 " + text7 + " 通过防火墙。若装着电脑管家/360/火绒，还要在它们里面放行。";
	}

	private static byte[] GbkBytes(string s)
	{
		try
		{
			return Encoding.GetEncoding(936, new EncoderReplacementFallback("?"), DecoderFallback.ReplacementFallback).GetBytes(s);
		}
		catch (Exception)
		{
		}
		return Encoding.ASCII.GetBytes(new string(s.Select((char c) => (c < '\u0080') ? c : '?').ToArray()));
	}

	public static bool IsInstalled()
	{
		Pkg pkg = PackageMeta();
		if (pkg == null)
		{
			return false;
		}
		string text = InstallDir();
		(string, string)[] files = pkg.files;
		for (int i = 0; i < files.Length; i++)
		{
			(string, string) tuple = files[i];
			if (!IsFile(PPath.Join(text, tuple.Item1)))
			{
				return false;
			}
		}
		return IsFile(Executable());
	}

	public static List<string> DownloadUrls()
	{
		string text = "terracotta-0.4.2-" + Classifier() + "-pkg.tar.gz";
		return new List<string>
		{
			"https://gitee.com/burningtnt/Terracotta/releases/download/v0.4.2/" + text,
			"https://cnb.cool/HMCL-Terracotta/Terracotta/-/releases/download/v0.4.2/" + text,
			"https://alist.8mi.tech/d/mirror/HMCL-Terracotta/Auto/v0.4.2/" + text,
			"https://github.com/burningtnt/Terracotta/releases/download/v0.4.2/" + text
		};
	}

	private static void _extract_named(string archive, string destDir, (string name, string sha)[] files)
	{
		Directory.CreateDirectory(Utils.AbsPath(destDir));
		Dictionary<string, string> dictionary = new Dictionary<string, string>();
		for (int i = 0; i < files.Length; i++)
		{
			(string, string) tuple = files[i];
			dictionary[tuple.Item1.ToLowerInvariant()] = tuple.Item1;
		}
		HashSet<string> found = new HashSet<string>();
		using (FileStream stream = new FileStream(Utils.AbsPath(archive), FileMode.Open, FileAccess.Read))
		{
			using GZipStream s = new GZipStream(stream, CompressionMode.Decompress);
			foreach (Utils.TarMember item in Utils.TarReader.Read(s))
			{
				if (item.Type != '0' && item.Type != 0 && item.Type != '7')
				{
					continue;
				}
				string name = new PPath(item.Name).Name;
				if (dictionary.TryGetValue(name.ToLowerInvariant(), out var real))
				{
					string path = PPath.Join(destDir, real);
					File.WriteAllBytes(Utils.AbsPath(path), item.Data ?? new byte[0]);
					string text = files.First(((string name, string sha) f) => f.name == real).sha.ToLowerInvariant();
					if (Utils.Sha512File(path) != text)
					{
						throw new TerracottaError("内核文件校验失败: " + real);
					}
					found.Add(real);
				}
			}
		}
		List<string> list = (from f in files
			select f.name into n
			where !found.Contains(n)
			select n).ToList();
		if (list.Count > 0)
		{
			throw new TerracottaError("安装包不完整，缺少: " + string.Join("、", Py.Sorted(list, (string x) => x)));
		}
	}

	public static string Install(DownloadManager dm, Action<string> log = null)
	{
		Pkg pkg = PackageMeta();
		if (pkg == null)
		{
			throw new TerracottaError("当前系统架构暂不支持陶瓦联机。");
		}
		if (IsInstalled())
		{
			log?.Invoke("陶瓦联机内核已安装");
			return InstallDir();
		}
		Action<string> action = log ?? ((Action<string>)delegate
		{
		});
		action("下载陶瓦联机 0.4.2（" + Classifier() + "）");
		string text = PPath.Join(Utils.ROOT, "cache", "terracotta");
		Utils.EnsureDir(text);
		string text2 = PPath.Join(text, "terracotta-0.4.2-" + Classifier() + "-pkg.tar.gz");
		List<string> list = DownloadUrls();
		dm.Download(list[0], text2, null, sha512: pkg.hash, urls: list.Skip(1).Cast<object>().ToList(), size: null, force: false, timeout: 600L, sha256: null, expand: false);
		action("正在解压内核…");
		string text3 = InstallDir();
		if (Directory.Exists(Utils.AbsPath(text3)) || File.Exists(Utils.AbsPath(text3)))
		{
			try
			{
				Utils.RemoveTree(text3);
			}
			catch (Exception)
			{
			}
		}
		try
		{
			_extract_named(text2, text3, pkg.files);
		}
		catch (Exception)
		{
			try
			{
				Utils.RemoveTree(text3);
			}
			catch (Exception)
			{
			}
			throw;
		}
		action("已安装到 " + text3);
		return text3;
	}

	private static string _runtime_file()
	{
		return PPath.Join(Utils.ROOT, "terracotta", "runtime.json");
	}

	private static void _save_port(long port)
	{
		_port = port;
		if (_port != 0L)
		{
			Utils.WriteJson(_runtime_file(), PyDict.Of("port", _port));
		}
	}

	private static long _load_port()
	{
		object obj = Utils.ReadJson(_runtime_file(), new PyDict());
		if (!Py.Truthy(obj))
		{
			obj = new PyDict();
		}
		try
		{
			return Py.Int(Py.Or(((PyDict)obj)["port"], 0L));
		}
		catch (Exception)
		{
			return 0L;
		}
	}

	private static AuthResponse Get(string url, double timeout)
	{
		return AuthHttp.Send("GET", url, null, null, hasJson: false, null, timeout);
	}

	private static bool _http_ok(long port, double timeout = 1.5)
	{
		if (port == 0L)
		{
			return false;
		}
		try
		{
			return Get($"http://127.0.0.1:{port}/state", timeout).StatusCode < 500;
		}
		catch (Exception)
		{
			return false;
		}
	}

	public static bool Running()
	{
		if (_http_ok(_port))
		{
			return true;
		}
		long num = _load_port();
		if (num != 0L && num != _port && _http_ok(num))
		{
			_port = num;
			return true;
		}
		return false;
	}

	private static bool _is_china_mainland()
	{
		string text;
		if (Utils.IS_WINDOWS)
		{
			text = CultureInfo.CurrentCulture.Name;
		}
		else
		{
			text = "";
			string[] array = new string[4] { "LC_ALL", "LC_CTYPE", "LANG", "LANGUAGE" };
			foreach (string text2 in array)
			{
				string environmentVariable = Environment.GetEnvironmentVariable(text2);
				if (!string.IsNullOrEmpty(environmentVariable))
				{
					text = ((text2 == "LANGUAGE") ? environmentVariable.Split(':')[0] : environmentVariable);
					break;
				}
			}
			text = text.Split('.')[0].Split('@')[0];
			if (text.Length == 0 || text == "POSIX")
			{
				text = "C";
			}
			if (!Regex.IsMatch(text, "^[a-zA-Z]{2,3}([_-][a-zA-Z]+)?$") || text == "C")
			{
				text = "C";
			}
		}
		text = Py.Lower((text ?? "").Replace("-", "_"));
		if (text.Length == 0)
		{
			return true;
		}
		if (text.StartsWith("zh_tw") || text.StartsWith("zh_hk") || text.StartsWith("zh_mo"))
		{
			return false;
		}
		return text.StartsWith("zh");
	}

	private static bool _valid_node_url(string url)
	{
		Match match = Regex.Match(url, "^([A-Za-z][A-Za-z0-9+.\\-]*):(.*)$", RegexOptions.Singleline);
		if (!match.Success)
		{
			return false;
		}
		string value = match.Groups[2].Value;
		if (!value.StartsWith("//"))
		{
			return false;
		}
		string text = value.Substring(2);
		int num = text.IndexOfAny(new char[3] { '/', '?', '#' });
		if (num >= 0)
		{
			text = text.Substring(0, num);
		}
		return text.Length > 0;
	}

	private static List<string> _extra_nodes()
	{
		List<string> list = new List<string> { "https://terracotta.glavo.site/acebc7d8-1208-47fd-b212-d03ac49e36e0" };
		try
		{
			object obj = Py.Or(Config.CONFIG.Get("terracotta_extra_nodes"), new List<object>());
			if (obj is string item)
			{
				obj = new List<object> { item };
			}
			IEnumerable<object> enumerable = ((obj is List<object> list2) ? list2 : ((obj as PyDict) ?? throw new TypeError("not iterable")).Keys.Cast<object>());
			foreach (object item2 in enumerable)
			{
				string text = Py.Strip(Py.Str(Py.Or(item2, "")));
				if (text.Length > 0)
				{
					list.Add(text);
				}
			}
		}
		catch (Exception)
		{
		}
		return list;
	}

	public static List<object> PublicNodes()
	{
		if (_nodes != null)
		{
			return _nodes;
		}
		List<object> listed = new List<object>();
		HashSet<string> seen = new HashSet<string>();
		foreach (string item in _extra_nodes())
		{
			add(item);
		}
		try
		{
			AuthResponse authResponse = Get("https://terracotta.glavo.site/nodes", 10.0);
			if (authResponse.StatusCode >= 400)
			{
				throw new AuthHttpException("HTTP " + authResponse.StatusCode);
			}
			if (authResponse.Json() is List<object> list)
			{
				bool flag = _is_china_mainland();
				foreach (object item2 in list)
				{
					if (!(item2 is PyDict pyDict))
					{
						continue;
					}
					string text = Py.Strip(Py.AsStr(Py.Or(pyDict["url"], ""), "strip"));
					string text2 = Py.Strip(Py.AsStr(Py.Or(pyDict["region"], ""), "strip"));
					if (text.Length != 0 && _valid_node_url(text))
					{
						bool flag2 = Py.Lower(text2) == "cn";
						if (text2.Length <= 0 || flag == flag2)
						{
							add(text);
						}
					}
				}
			}
		}
		catch (Exception)
		{
		}
		_nodes = listed;
		return listed;
		void add(string url)
		{
			url = Py.Strip(url ?? "");
			if (url.Length > 0 && !seen.Contains(url) && _valid_node_url(url))
			{
				seen.Add(url);
				listed.Add(url);
			}
		}
	}

	private static string HttpErrorText(int code, string url)
	{
		string input;
		try
		{
			HttpStatusCode httpStatusCode = (HttpStatusCode)code;
			input = httpStatusCode.ToString();
		}
		catch (Exception)
		{
			input = "";
		}
		input = Regex.Replace(input, "(?<=[a-z])(?=[A-Z])", " ");
		string text = ((code < 500) ? "Client Error" : "Server Error");
		return $"{code} {text}: {input} for url: {url}";
	}

	private static object _http(string path, List<(string, object)> parameters = null, double timeout = 4.0)
	{
		if (_port == 0L)
		{
			throw new TerracottaError("联机内核尚未就绪。");
		}
		List<string> list = new List<string>();
		if (parameters != null)
		{
			foreach (var (s, obj) in parameters)
			{
				if (obj != null && !(obj is string { Length: 0 }))
				{
					list.Add(UrlQuote(s) + "=" + UrlQuote(Py.Str(obj)));
				}
			}
		}
		string text2 = $"http://127.0.0.1:{_port}{path}";
		if (list.Count > 0)
		{
			text2 = text2 + "?" + string.Join("&", list);
		}
		AuthResponse authResponse = Get(text2, timeout);
		if (authResponse.StatusCode == 400)
		{
			throw new TerracottaError("联机内核拒绝了这次请求。房间号须为 U/XXXX-XXXX-XXXX-XXXX，且内核要处于就绪状态。");
		}
		if (authResponse.StatusCode >= 400)
		{
			throw new PyException("HTTPError", HttpErrorText(authResponse.StatusCode, text2));
		}
		string text3 = authResponse.Text ?? "";
		if (text3.Length == 0 || "{[".IndexOf(text3[0]) >= 0)
		{
			if (text3.Length == 0)
			{
				return text3;
			}
			try
			{
				return authResponse.Json();
			}
			catch (Exception)
			{
				return text3;
			}
		}
		return text3;
	}

	private static string UrlQuote(string s)
	{
		StringBuilder stringBuilder = new StringBuilder();
		byte[] bytes = Encoding.UTF8.GetBytes(s);
		for (int i = 0; i < bytes.Length; i++)
		{
			byte b = bytes[i];
			char c = (char)b;
			if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || "_.-~".IndexOf(c) >= 0)
			{
				stringBuilder.Append(c);
			}
			else if (c == ' ')
			{
				stringBuilder.Append('+');
			}
			else
			{
				stringBuilder.Append('%').Append(b.ToString("X2"));
			}
		}
		return stringBuilder.ToString();
	}

	private static long _read_port(string path)
	{
		if (!File.Exists(path))
		{
			return 0L;
		}
		try
		{
			return Py.Int(Py.Or(((PyDict)PyJson.Loads(Utils.DecodeUtf8Strict(File.ReadAllBytes(path))))["port"], 0L));
		}
		catch (Exception)
		{
			return 0L;
		}
	}

	private static string _kernel_version(long port)
	{
		try
		{
			return Py.Str(Py.Or(((PyDict)Get($"http://127.0.0.1:{port}/meta", 2.0).Json())["version"], ""));
		}
		catch (Exception)
		{
			return "";
		}
	}

	private static void _peaceful_stop(long port)
	{
		try
		{
			Get($"http://127.0.0.1:{port}/panic?peaceful=true", 2.0);
		}
		catch (Exception)
		{
		}
	}

	private static void _recover_waiting()
	{
		if (_port == 0L)
		{
			return;
		}
		try
		{
			if (Py.Str(Py.Or(((PyDict)Get($"http://127.0.0.1:{_port}/state", 2.0).Json())["state"], "")) == "exception")
			{
				Get($"http://127.0.0.1:{_port}/state/ide", 2.0);
			}
		}
		catch (Exception)
		{
		}
	}

	private static bool Exited(Process p, out int code)
	{
		code = 0;
		try
		{
			if (!p.HasExited)
			{
				return false;
			}
			code = p.ExitCode;
			return true;
		}
		catch (Exception)
		{
			return true;
		}
	}

	public static long Start(Action<string> log = null)
	{
		Action<string> action = log ?? ((Action<string>)delegate
		{
		});
		lock (_lock)
		{
			Exception ex = null;
			for (int num = 0; num < 2; num++)
			{
				if (Running())
				{
					string text = _kernel_version(_port);
					if (text.Length > 0 && text != "0.4.2" && num == 0)
					{
						action("发现其它版本陶瓦内核 " + text + "，正在切换到官方 0.4.2");
						_peaceful_stop(_port);
						Thread.Sleep(3000);
						_port = 0L;
						continue;
					}
					_recover_waiting();
					action($"联机内核已在运行（127.0.0.1:{_port}）");
					return _port;
				}
				string text2 = Executable();
				if (!IsFile(text2))
				{
					throw new TerracottaError("请先安装陶瓦联机内核。");
				}
				string text3 = Path.Combine(Path.GetTempPath(), "tipcl-terracotta-" + Guid.NewGuid().ToString("N").Substring(0, 8));
				Directory.CreateDirectory(text3);
				string fullPath = Path.GetFullPath(Path.Combine(text3, "http"));
				action("启动 " + new PPath(text2).Name);
				Process process = Process.Start(new ProcessStartInfo(Utils.AbsPath(text2))
				{
					UseShellExecute = false,
					CreateNoWindow = true,
					WorkingDirectory = Path.GetDirectoryName(Utils.AbsPath(text2)),
					Arguments = Utils.List2CmdLine(new List<string> { "--hmcl", fullPath }),
					RedirectStandardOutput = true,
					RedirectStandardError = true
				});
				process.OutputDataReceived += delegate
				{
				};
				process.ErrorDataReceived += delegate
				{
				};
				process.BeginOutputReadLine();
				process.BeginErrorReadLine();
				_proc = process;
				double num2 = Py.Time() + 40.0;
				long num3 = 0L;
				while (Py.Time() < num2)
				{
					num3 = _read_port(fullPath);
					if (num3 != 0L)
					{
						break;
					}
					if (!Exited(process, out var code))
					{
						Thread.Sleep(150);
						continue;
					}
					if (code == 0)
					{
						for (int num4 = 0; num4 < 10; num4++)
						{
							num3 = _read_port(fullPath);
							if (num3 != 0L)
							{
								break;
							}
							Thread.Sleep(200);
						}
						if (num3 == 0L)
						{
							ex = new TerracottaError("陶瓦联机已退出，但没有写出端口文件。");
						}
					}
					else
					{
						ex = new TerracottaError($"陶瓦联机进程提前退出，代码 {code}。");
					}
					break;
				}
				if (num3 == 0L)
				{
					try
					{
						process.Kill();
					}
					catch (Exception)
					{
					}
					if (ex != null && num == 1)
					{
						throw ex;
					}
					if (ex == null)
					{
						ex = new TerracottaError("等待联机内核端口超时。");
					}
					continue;
				}
				_save_port(num3);
				double num5 = Py.Time() + 20.0;
				while (Py.Time() < num5 && !_http_ok(num3))
				{
					Thread.Sleep(200);
				}
				if (!_http_ok(num3))
				{
					throw new TerracottaError($"端口 {num3} 已写出，但联机接口还没有响应。");
				}
				string text4 = _kernel_version(num3);
				if (text4.Length > 0 && text4 != "0.4.2" && num == 0)
				{
					action("发现其它版本陶瓦内核 " + text4 + "，正在切换到官方 0.4.2");
					_peaceful_stop(num3);
					Thread.Sleep(3000);
					_port = 0L;
					continue;
				}
				_proc = (Exited(process, out var _) ? null : process);
				_recover_waiting();
				action($"联机内核已就绪（127.0.0.1:{num3}）");
				return num3;
			}
			throw ex ?? new TerracottaError("无法启动陶瓦联机内核。");
		}
	}

	public static void Stop()
	{
		long num = ((_port != 0L) ? _port : _load_port());
		if (num != 0L)
		{
			_peaceful_stop(num);
		}
		Process proc = _proc;
		_proc = null;
		_port = 0L;
		if (proc != null && !Exited(proc, out var _))
		{
			try
			{
				proc.Kill();
				proc.WaitForExit(3000);
			}
			catch (Exception)
			{
			}
		}
		try
		{
			string path = Utils.AbsPath(_runtime_file());
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch (Exception)
		{
		}
	}

	private static List<(string, object)> _player_params(string player, string room = null)
	{
		List<(string, object)> list = new List<(string, object)> { ("player", Py.Truthy(player) ? player : "Player") };
		if (Py.Truthy(room))
		{
			list.Add(("room", Py.Strip(room)));
		}
		foreach (object item in PublicNodes())
		{
			list.Add(("public_nodes", item));
		}
		return list;
	}

	public static object SetWaiting()
	{
		return _http("/state/ide");
	}

	public static object SetScanning(string player)
	{
		_recover_waiting();
		return _http("/state/scanning", _player_params(player));
	}

	public static object SetGuesting(object room, string player)
	{
		string text = Py.Strip(Py.AsStr(Py.Truthy(room) ? room : "", "strip"));
		if (text.Length == 0)
		{
			throw new TerracottaError("请输入邀请码。");
		}
		if (ParseRoom(text) == null)
		{
			throw new TerracottaError(RoomError(text));
		}
		_recover_waiting();
		return _http("/state/guesting", _player_params(player, text));
	}

	public static PyDict FetchState()
	{
		if (!Running())
		{
			return new PyDict();
		}
		return (_http("/state") as PyDict) ?? new PyDict();
	}

	public static (string host, long port) SplitJoinUrl(object url)
	{
		string text = Py.Strip(Py.AsStr(Py.Truthy(url) ? url : "", "strip"));
		if (text.Length == 0)
		{
			throw new TerracottaError("还没有联机地址。");
		}
		if (text.Contains("://"))
		{
			text = text.Split(new string[1] { "://" }, 2, StringSplitOptions.None)[1];
		}
		if (text.Contains(":"))
		{
			int num = text.LastIndexOf(':');
			string text2 = text.Substring(0, num);
			string o = text.Substring(num + 1);
			return (host: (text2.Length > 0) ? text2 : "127.0.0.1", port: Py.Int(o));
		}
		return (host: (text.Length > 0) ? text : "127.0.0.1", port: 25565L);
	}

	private static string _default_game_dir()
	{
		throw new PyException("ImportError", "cannot import name 'DEFAULT_INSTANCE_NAME' from 'mclauncher.instances'");
	}

	public static string RememberLobby(string url, string gameDir = null)
	{
		string text = ((gameDir != null) ? PPath.Norm(gameDir) : _default_game_dir());
		string text2 = text + "|" + url;
		if (_last_lobby == text2)
		{
			return text;
		}
		WriteLobbyServer(text, url);
		_last_lobby = text2;
		return text;
	}

	public static string WriteLobbyServer(string gameDir, string url)
	{
		(string host, long port) tuple = SplitJoinUrl(url);
		string item = tuple.host;
		long item2 = tuple.port;
		string text = ((item2 == 25565) ? item : $"{item}:{item2}");
		string text2 = PPath.Join(gameDir, "servers.dat");
		Utils.EnsureDir(new PPath(text2).Parent.ToString());
		List<object> list = _read_servers(text2);
		List<object> list2 = new List<object>();
		list2.Add(PyDict.Of("name", "陶瓦联机大厅", "ip", text, "hidden", 0L));
		List<object> list3 = list2;
		foreach (PyDict item3 in list)
		{
			if (!(Py.Str(Py.Or(item3["name"], "")) == "陶瓦联机大厅") && !(Py.Str(Py.Or(item3["ip"], "")) == text))
			{
				list3.Add(item3);
			}
		}
		_write_servers(text2, list3);
		return text2;
	}

	public static PyDict Snapshot(string player = "Player", bool game_running = false)
	{
		Pkg pkg = PackageMeta();
		PyDict pyDict = PyDict.Of("supported", pkg != null, "installed", IsInstalled(), "running", Running(), "port", _port, "state", "missing", "label", _STATE_LABEL["missing"], "room", "", "url", "", "difficulty", "", "difficulty_hint", "", "profiles", new List<object>(), "error", "", "player", Py.Truthy(player) ? player : "Player", "game_running", game_running, "copyright", "Terracotta | 陶瓦联机  © burningtnt  ·  基于 EasyTier", "home", "https://github.com/burningtnt/Terracotta", "version", "0.4.2", "nodes", new List<object>(_nodes ?? new List<object>()), "firewall_stale", false);
		if (pkg == null)
		{
			pyDict["state"] = "unsupported";
			pyDict["label"] = _STATE_LABEL["unsupported"];
			return pyDict;
		}
		if (!(bool)pyDict["installed"])
		{
			return pyDict;
		}
		if (!Running())
		{
			pyDict["state"] = "idle";
			pyDict["label"] = "内核已安装，打开本页会自动启动";
			return pyDict;
		}
		PyDict pyDict2;
		try
		{
			pyDict2 = FetchState();
		}
		catch (Exception e)
		{
			pyDict["state"] = "fatal";
			pyDict["label"] = _STATE_LABEL["fatal"];
			pyDict["error"] = Py.ExcStr(e);
			return pyDict;
		}
		object obj = (pyDict["state"] = Py.Str(Py.Or(pyDict2["state"], "unknown")));
		string text2 = (string)obj;
		pyDict["label"] = (_STATE_LABEL.TryGetValue(text2, out string value) ? value : text2);
		pyDict["room"] = Py.Str(Py.Or(pyDict2["room"], pyDict2["code"], ""));
		pyDict["url"] = Py.Str(Py.Or(pyDict2["url"], ""));
		obj = (pyDict["difficulty"] = Py.Upper(Py.Str(Py.Or(pyDict2["difficulty"], ""))));
		string key = (string)obj;
		pyDict["difficulty_hint"] = (_DIFF.TryGetValue(key, out string value2) ? value2 : "");
		object obj2 = Py.Or(pyDict2["profiles"], new List<object>());
		List<object> list = new List<object>();
		if (obj2 is List<object> list2)
		{
			foreach (object item in list2)
			{
				if (item is PyDict pyDict3)
				{
					list.Add(PyDict.Of("name", Py.Or(pyDict3["name"], "玩家"), "vendor", Py.Or(pyDict3["vendor"], ""), "kind", Py.Or(pyDict3["kind"], "")));
				}
			}
		}
		pyDict["profiles"] = list;
		if (text2 == "exception")
		{
			long num;
			try
			{
				num = Py.Int(Py.Or(pyDict2["type"], 0L));
			}
			catch (Exception)
			{
				num = 0L;
			}
			pyDict["error"] = ((num >= 0 && num < _EXC.Length) ? _EXC[num] : "联机出错");
			pyDict["label"] = pyDict["error"];
			if (num == 0L)
			{
				pyDict["error_hint"] = "陶瓦是 EasyTier P2P 打洞，不是 FRP 隧道。官方公共节点连不上时，必须和 HMCL 用同一条自定义会合节点。请完全退出后重试；已带上本机 HMCL 成功加入时用的那条 terracotta.glavo.site 节点。";
			}
		}
		if (text2 == "guest-ok" && Py.Truthy(pyDict["url"]))
		{
			try
			{
				RememberLobby((string)pyDict["url"]);
			}
			catch (Exception)
			{
			}
		}
		bool flag = false;
		foreach (string item2 in FirewallPrograms())
		{
			if (Path.GetFileName(item2).ToLowerInvariant().Contains("easytier") && !_allowed_fw.Contains(Resolve(item2).ToLowerInvariant()))
			{
				flag = true;
				break;
			}
		}
		pyDict["firewall_stale"] = flag;
		return pyDict;
	}

	public static List<object> ReadGameServers(string path)
	{
		return _read_servers(path);
	}

	public static string WriteGameServers(string path, List<object> servers)
	{
		string text = new PPath(path).ToString();
		Utils.EnsureDir(new PPath(text).Parent.ToString());
		_write_servers(text, servers);
		return text;
	}

	private static int _read_u8(Buf buf)
	{
		byte[] array = buf.Read(1);
		if (array.Length == 0)
		{
			throw new TerracottaError("servers.dat 已损坏");
		}
		return array[0];
	}

	private static int UnpackU16(byte[] d)
	{
		if (d.Length != 2)
		{
			throw new ValueError("unpack requires a buffer of 2 bytes");
		}
		return (d[0] << 8) | d[1];
	}

	private static int UnpackI32(byte[] d)
	{
		if (d.Length != 4)
		{
			throw new ValueError("unpack requires a buffer of 4 bytes");
		}
		return (d[0] << 24) | (d[1] << 16) | (d[2] << 8) | d[3];
	}

	private static string _read_str(Buf buf)
	{
		int n = UnpackU16(buf.Read(2));
		return Utf8Replace.GetString(buf.Read(n));
	}

	private static void _skip_nbt(Buf buf, int tag)
	{
		switch (tag)
		{
		case 1:
			buf.Read(1);
			break;
		case 2:
			buf.Read(2);
			break;
		case 3:
		case 5:
			buf.Read(4);
			break;
		case 4:
		case 6:
			buf.Read(8);
			break;
		case 7:
			buf.Read(UnpackI32(buf.Read(4)));
			break;
		case 8:
			_read_str(buf);
			break;
		case 9:
		{
			int tag2 = _read_u8(buf);
			int val = UnpackI32(buf.Read(4));
			for (int i = 0; i < Math.Max(val, 0); i++)
			{
				_skip_nbt(buf, tag2);
			}
			break;
		}
		case 10:
			while (true)
			{
				int num = _read_u8(buf);
				if (num != 0)
				{
					_read_str(buf);
					_skip_nbt(buf, num);
					continue;
				}
				break;
			}
			break;
		case 11:
			buf.Read(UnpackI32(buf.Read(4)) * 4);
			break;
		case 12:
			buf.Read(UnpackI32(buf.Read(4)) * 8);
			break;
		default:
			throw new TerracottaError("servers.dat 含未知标签");
		case 0:
			break;
		}
	}

	private static PyDict _read_compound_body(Buf buf)
	{
		PyDict pyDict = new PyDict();
		while (true)
		{
			int num = _read_u8(buf);
			if (num == 0)
			{
				break;
			}
			string key = _read_str(buf);
			switch (num)
			{
			case 1:
			{
				byte[] array = buf.Read(1);
				if (array.Length == 0)
				{
					throw new IndexError("index out of range");
				}
				pyDict[key] = (long)array[0];
				break;
			}
			case 8:
				pyDict[key] = _read_str(buf);
				break;
			case 9:
			{
				int num2 = _read_u8(buf);
				int val = UnpackI32(buf.Read(4));
				List<object> list = new List<object>();
				for (int i = 0; i < Math.Max(val, 0); i++)
				{
					if (num2 == 10)
					{
						list.Add(_read_compound_body(buf));
					}
					else
					{
						_skip_nbt(buf, num2);
					}
				}
				pyDict[key] = list;
				break;
			}
			default:
				_skip_nbt(buf, num);
				break;
			}
		}
		return pyDict;
	}

	private static List<object> _read_servers(string path)
	{
		if (!LocalFs.IsFile(path))
		{
			return new List<object>();
		}
		try
		{
			Buf buf = new Buf(GzipDecompress(File.ReadAllBytes(PPath.Abs(path))));
			if (_read_u8(buf) != 10)
			{
				return new List<object>();
			}
			_read_str(buf);
			PyDict pyDict = _read_compound_body(buf);
			object obj = (Py.Truthy(pyDict["servers"]) ? pyDict["servers"] : new List<object>());
			List<object> list = new List<object>();
			if (obj is List<object> list2)
			{
				foreach (object item in list2)
				{
					if (item is PyDict)
					{
						list.Add(item);
					}
				}
			}
			return list;
		}
		catch (Exception)
		{
			return new List<object>();
		}
	}

	public static byte[] GzipDecompress(byte[] data)
	{
		using MemoryStream stream = new MemoryStream(data);
		using GZipStream gZipStream = new GZipStream(stream, CompressionMode.Decompress);
		using MemoryStream memoryStream = new MemoryStream();
		gZipStream.CopyTo(memoryStream);
		return memoryStream.ToArray();
	}

	private static void _write_str(MemoryStream buf, string text)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(text ?? "");
		if (bytes.Length > 65535)
		{
			throw new StructError("'H' format requires 0 <= number <= 65535");
		}
		buf.WriteByte((byte)(bytes.Length >> 8));
		buf.WriteByte((byte)bytes.Length);
		buf.Write(bytes, 0, bytes.Length);
	}

	public static byte[] ServersNbt(List<object> servers)
	{
		MemoryStream memoryStream = new MemoryStream();
		memoryStream.WriteByte(10);
		_write_str(memoryStream, "");
		memoryStream.WriteByte(9);
		_write_str(memoryStream, "servers");
		memoryStream.WriteByte(10);
		int count = servers.Count;
		memoryStream.WriteByte((byte)(count >> 24));
		memoryStream.WriteByte((byte)(count >> 16));
		memoryStream.WriteByte((byte)(count >> 8));
		memoryStream.WriteByte((byte)count);
		foreach (object server in servers)
		{
			PyDict obj = (server as PyDict) ?? throw new AttributeError("'" + Py.TypeName(server) + "' object has no attribute 'get'");
			string text = Py.Str(Py.Or(obj["name"], "陶瓦联机大厅"));
			string text2 = Py.Str(Py.Or(obj["ip"], ""));
			int num = (Py.Truthy(obj["hidden"]) ? 1 : 0);
			memoryStream.WriteByte(8);
			_write_str(memoryStream, "name");
			_write_str(memoryStream, text);
			memoryStream.WriteByte(8);
			_write_str(memoryStream, "ip");
			_write_str(memoryStream, text2);
			memoryStream.WriteByte(1);
			_write_str(memoryStream, "hidden");
			memoryStream.WriteByte((byte)num);
			memoryStream.WriteByte(0);
		}
		memoryStream.WriteByte(0);
		return memoryStream.ToArray();
	}

	private static void _write_servers(string path, List<object> servers)
	{
		byte[] data = ServersNbt(servers);
		File.WriteAllBytes(PPath.Abs(path), GzipCompress(data));
	}

	public static byte[] GzipCompress(byte[] data, long? mtime = null)
	{
		long num = mtime ?? ((long)Py.Time());
		MemoryStream memoryStream = new MemoryStream();
		memoryStream.Write(new byte[4] { 31, 139, 8, 0 }, 0, 4);
		WriteU32LE(memoryStream, (uint)num);
		memoryStream.WriteByte(2);
		memoryStream.WriteByte(byte.MaxValue);
		CompressionLevel compressionLevel = CompressionLevel.Optimal;
		using (DeflateStream deflateStream = new DeflateStream(memoryStream, compressionLevel, leaveOpen: true))
		{
			deflateStream.Write(data, 0, data.Length);
		}
		WriteU32LE(memoryStream, Crc32(data));
		WriteU32LE(memoryStream, (uint)data.Length);
		return memoryStream.ToArray();
	}

	private static void WriteU32LE(Stream s, uint v)
	{
		s.WriteByte((byte)v);
		s.WriteByte((byte)(v >> 8));
		s.WriteByte((byte)(v >> 16));
		s.WriteByte((byte)(v >> 24));
	}

	public static uint Crc32(byte[] data)
	{
		uint[] array = _crcTable;
		if (array == null)
		{
			array = new uint[256];
			for (uint num = 0u; num < 256; num++)
			{
				uint num2 = num;
				for (int i = 0; i < 8; i++)
				{
					num2 = (((num2 & 1) != 0) ? (0xEDB88320u ^ (num2 >> 1)) : (num2 >> 1));
				}
				array[num] = num2;
			}
			_crcTable = array;
		}
		uint num3 = uint.MaxValue;
		foreach (byte b in data)
		{
			num3 = array[(num3 ^ b) & 0xFF] ^ (num3 >> 8);
		}
		return num3 ^ 0xFFFFFFFFu;
	}
}
