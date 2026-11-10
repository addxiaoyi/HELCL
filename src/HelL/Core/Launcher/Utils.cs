using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace HelL.Core.Launcher;

public static class Utils
{
	private struct OSVERSIONINFOEXW
	{
		public int dwOSVersionInfoSize;

		public int dwMajorVersion;

		public int dwMinorVersion;

		public int dwBuildNumber;

		public int dwPlatformId;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
		public string szCSDVersion;

		public ushort wServicePackMajor;

		public ushort wServicePackMinor;

		public ushort wSuiteMask;

		public byte wProductType;

		public byte wReserved;
	}

	internal sealed class TarMember
	{
		public string Name;

		public char Type;

		public int Mode;

		public long MTime;

		public byte[] Data;

		public string Target;
	}

	internal static class TarReader
	{
		private static void ReadExact(Stream s, byte[] buf, int n)
		{
			int num;
			for (int i = 0; i < n; i += num)
			{
				num = s.Read(buf, i, n - i);
				if (num <= 0)
				{
					throw new EndOfStreamException();
				}
			}
		}

		private static long Octal(byte[] b, int off, int len)
		{
			if ((b[off] & 0x80) != 0)
			{
				long num = b[off] & 0x7F;
				for (int i = 1; i < len; i++)
				{
					num = (num << 8) | b[off + i];
				}
				return num;
			}
			long num2 = 0L;
			for (int j = off; j < off + len; j++)
			{
				byte b2 = b[j];
				switch (b2)
				{
				case 0:
				case 32:
					if ((num2 <= 0 && b2 != 0) || b2 != 0)
					{
						continue;
					}
					break;
				case 48:
				case 49:
				case 50:
				case 51:
				case 52:
				case 53:
				case 54:
				case 55:
					num2 = num2 * 8 + (b2 - 48);
					continue;
				}
				break;
			}
			return num2;
		}

		private static string Str(byte[] b, int off, int len)
		{
			int i;
			for (i = off; i < off + len && b[i] != 0; i++)
			{
			}
			return Encoding.UTF8.GetString(b, off, i - off);
		}

		public static IEnumerable<TarMember> Read(Stream s)
		{
			byte[] hdr = new byte[512];
			string longName = null;
			string paxPath = null;
			while (true)
			{
				try
				{
					ReadExact(s, hdr, 512);
				}
				catch (EndOfStreamException)
				{
					break;
				}
				bool flag = true;
				for (int i = 0; i < 512; i++)
				{
					if (hdr[i] != 0)
					{
						flag = false;
						break;
					}
				}
				if (flag)
				{
					break;
				}
				string text = Str(hdr, 0, 100);
				int mode = (int)Octal(hdr, 100, 8);
				long num = Octal(hdr, 124, 12);
				long mTime = Octal(hdr, 136, 12);
				char c = (char)hdr[156];
				string text2 = Str(hdr, 257, 6);
				if (text2.StartsWith("ustar"))
				{
					string text3 = Str(hdr, 345, 155);
					if (text3.Length > 0 && text2 == "ustar")
					{
						text = text3 + "/" + text;
					}
				}
				byte[] array = new byte[num];
				if (num > 0)
				{
					ReadExact(s, array, (int)num);
				}
				long num2 = (512 - num % 512) % 512;
				if (num2 > 0)
				{
					ReadExact(s, new byte[num2], (int)num2);
				}
				switch (c)
				{
				case 'g':
					continue;
				case 'L':
					longName = Str(array, 0, array.Length);
					continue;
				case 'x':
				{
					string text4 = Encoding.UTF8.GetString(array);
					int num3 = 0;
					while (num3 < text4.Length)
					{
						int num4 = text4.IndexOf(' ', num3);
						if (num4 < 0)
						{
							break;
						}
						int num5 = text4.IndexOf('\n', num4);
						string text5 = text4.Substring(num4 + 1, ((num5 < 0) ? text4.Length : num5) - num4 - 1);
						int num6 = text5.IndexOf('=');
						if (num6 > 0 && text5.Substring(0, num6) == "path")
						{
							paxPath = text5.Substring(num6 + 1);
						}
						num3 = ((num5 < 0) ? text4.Length : (num5 + 1));
					}
					continue;
				}
				}
				if (longName != null)
				{
					text = longName;
					longName = null;
				}
				if (paxPath != null)
				{
					text = paxPath;
					paxPath = null;
				}
				if (c == '0' && text.EndsWith("/"))
				{
					c = '5';
				}
				yield return new TarMember
				{
					Name = text.TrimEnd('/'),
					Type = c,
					Mode = mode,
					MTime = mTime,
					Data = array
				};
			}
		}
	}

	public static readonly PyLog log = new PyLog("HelL");

	private static string _root;

	private static readonly object _rootLock = new object();

	private static readonly Dictionary<string, object> _JSON_FILE_LOCKS = new Dictionary<string, object>();

	private static readonly object _JSON_FILE_LOCKS_GUARD = new object();

	public static readonly string OS_NAME = OsName();

	public static readonly bool IS_WINDOWS = OS_NAME == "windows";

	public static readonly bool IS_MAC = OS_NAME == "osx";

	public static readonly string ARCH = ArchName();

	private static string _osVersion;

	private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

	private const int _REPLACE_RETRIES = 12;

	private static readonly Random _tmpRng = new Random();

	private const string TmpChars = "abcdefghijklmnopqrstuvwxyz0123456789_";

	private static Encoding _cp437;

	private static readonly Regex _PLACEHOLDER = new Regex("\\$\\{([^}]+)\\}", RegexOptions.CultureInvariant);

	public static string ROOT
	{
		get
		{
			string root = _root;
			if (root != null)
			{
				return root;
			}
			lock (_rootLock)
			{
				return _root ?? (_root = ResolveRoot());
			}
		}
	}

	public static string OS_VERSION => _osVersion ?? (_osVersion = ComputeOsVersion());

	public static Encoding Cp437
	{
		get
		{
			if (_cp437 != null)
			{
				return _cp437;
			}
			try
			{
				_cp437 = Encoding.GetEncoding(437);
			}
			catch (Exception)
			{
				_cp437 = Encoding.UTF8;
			}
			return _cp437;
		}
	}

	public static void SetRoot(string root)
	{
		lock (_rootLock)
		{
			_root = ((root == null) ? null : new PPath(root).ToString());
		}
	}

	public static string ResolveRoot()
	{
		string environmentVariable = Environment.GetEnvironmentVariable("TIPCL_HOME");
		if (!string.IsNullOrEmpty(environmentVariable))
		{
			return new PPath(environmentVariable).ToString();
		}
		string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
		try
		{
			string path = Path.Combine(baseDirectory, ".tipcl_wtest");
			File.WriteAllText(path, "1");
			File.Delete(path);
			return new PPath(Path.GetFullPath(baseDirectory)).ToString();
		}
		catch (Exception)
		{
		}
		if (IS_WINDOWS)
		{
			string text = Environment.GetEnvironmentVariable("APPDATA");
			if (string.IsNullOrEmpty(text))
			{
				text = PPath.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Roaming");
			}
			return PPath.Join(text, "HelL");
		}
		return PPath.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".tipcl");
	}

	public static string AbsPath(string path)
	{
		return PPath.Abs(path);
	}

	public static object JsonFileLock(string path)
	{
		string text = Path.GetFullPath(AbsPath(path));
		if (IS_WINDOWS)
		{
			text = text.ToLowerInvariant().Replace('/', '\\');
		}
		lock (_JSON_FILE_LOCKS_GUARD)
		{
			if (!_JSON_FILE_LOCKS.TryGetValue(text, out object value))
			{
				value = new object();
				_JSON_FILE_LOCKS[text] = value;
			}
			return value;
		}
	}

	public static string OsName()
	{
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			return "windows";
		}
		if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
		{
			return "osx";
		}
		return "linux";
	}

	public static string ArchName()
	{
		return RuntimeInformation.OSArchitecture switch
		{
			Architecture.X64 => "x64",
			Architecture.X86 => "x86",
			Architecture.Arm64 => "arm64",
			_ => "x64",
		};
	}

	[DllImport("ntdll.dll")]
	private static extern int RtlGetVersion(ref OSVERSIONINFOEXW info);

	private static string ComputeOsVersion()
	{
		try
		{
			if (IS_WINDOWS)
			{
				OSVERSIONINFOEXW info = new OSVERSIONINFOEXW
				{
					dwOSVersionInfoSize = Marshal.SizeOf(typeof(OSVERSIONINFOEXW))
				};
				if (RtlGetVersion(ref info) == 0)
				{
					return $"{info.dwMajorVersion}.{info.dwMinorVersion}.{info.dwBuildNumber}";
				}
				Version version = Environment.OSVersion.Version;
				return $"{version.Major}.{version.Minor}.{version.Build}";
			}
			if (File.Exists("/proc/sys/kernel/version"))
			{
				return File.ReadAllText("/proc/sys/kernel/version").Trim();
			}
			return Environment.OSVersion.Version.ToString();
		}
		catch (Exception)
		{
			return "";
		}
	}

	public static string JavaExecutableName()
	{
		if (!IS_WINDOWS)
		{
			return "java";
		}
		return "javaw.exe";
	}

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool GetVolumePathNameW(string lpszFileName, StringBuilder lpszVolumePathName, int cchBufferLength);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool GetVolumeInformationW(string root, IntPtr name, int nameSize, IntPtr serial, IntPtr maxLen, IntPtr flags, IntPtr fsName, int fsNameSize);

	public static bool VolumeLookupBroken(string path)
	{
		if (!IS_WINDOWS)
		{
			return false;
		}
		try
		{
			StringBuilder stringBuilder = new StringBuilder(261);
			if (!GetVolumePathNameW(path, stringBuilder, 261))
			{
				return true;
			}
			string text = stringBuilder.ToString();
			if (!text.EndsWith("\\"))
			{
				text += "\\";
			}
			return !GetVolumeInformationW(text, IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0);
		}
		catch (Exception)
		{
			return false;
		}
	}

	public static string UncTwin(string path)
	{
		if (!IS_WINDOWS)
		{
			return "";
		}
		string fullPath = Path.GetFullPath(AbsPath(path));
		if (fullPath.Length < 2 || fullPath[1] != ':')
		{
			return "";
		}
		string text = fullPath.Substring(2);
		string text2 = "\\\\localhost\\" + fullPath[0] + "$\\" + text.TrimStart('\\', '/');
		try
		{
			return Directory.Exists(text2) ? text2 : "";
		}
		catch (Exception)
		{
			return "";
		}
	}

	public static string SafeVersionId(object raw, string what = "版本 ID")
	{
		string text = Py.Strip(Py.S(raw));
		if (text.Length == 0 || text == "." || text == ".." || text.IndexOfAny(new char[2] { '/', '\\' }) >= 0 || text.Contains(":"))
		{
			throw new PathEscapeError("非法" + what + ": " + Py.Repr(raw));
		}
		return text;
	}

	public static string SafeChildPath(string @base, object name, string what = "路径")
	{
		PPath pPath = new PPath(@base);
		string text;
		string text2;
		try
		{
			text = Path.GetFullPath(pPath.Abs()).TrimEnd(Path.DirectorySeparatorChar);
			string fullPath = Path.GetFullPath((pPath / Py.Str(name)).Abs());
			fullPath = fullPath.TrimEnd(Path.DirectorySeparatorChar);
			text2 = Path.GetDirectoryName(fullPath);
			if (IS_WINDOWS && Path.GetFileName(fullPath).Length == 0)
			{
				text2 = null;
			}
		}
		catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException || ex is IOException)
		{
			throw new PathEscapeError("非法" + what + ": " + Py.Repr(name));
		}
		if (text2 != null)
		{
			bool flag;
			if (!IS_WINDOWS)
			{
				if (text2.TrimEnd('/') == text)
				{
					goto IL_012f;
				}
				if (!(text2 == "/"))
				{
					goto IL_0141;
				}
				flag = text == "";
			}
			else
			{
				flag = string.Equals(text2.TrimEnd('\\'), text, StringComparison.OrdinalIgnoreCase);
			}
			if (flag)
			{
				goto IL_012f;
			}
		}
		goto IL_0141;
		IL_0141:
		throw new PathEscapeError("非法" + what + ": " + Py.Repr(name));
		IL_012f:
		return (pPath / Py.Str(name)).ToString();
	}

	public static bool LooksLikePathSpelling(object text)
	{
		string text2 = Py.S(text);
		if (!text2.Contains("/") && !text2.Contains("\\") && !text2.StartsWith("./") && !text2.StartsWith(".\\") && !text2.Contains(".."))
		{
			return text2 != text2.TrimEnd(' ', '.');
		}
		return true;
	}

	public static string PosixNormPath(string path)
	{
		if (path == "")
		{
			return ".";
		}
		int num = (path.StartsWith("/") ? 1 : 0);
		if (num == 1 && path.StartsWith("//") && !path.StartsWith("///"))
		{
			num = 2;
		}
		string[] array = path.Split('/');
		List<string> list = new List<string>();
		string[] array2 = array;
		foreach (string text in array2)
		{
			if (!(text == "") && !(text == "."))
			{
				if (text != ".." || (num == 0 && list.Count == 0) || (list.Count > 0 && list[list.Count - 1] == ".."))
				{
					list.Add(text);
				}
				else if (list.Count > 0)
				{
					list.RemoveAt(list.Count - 1);
				}
			}
		}
		string text2 = new string('/', num) + string.Join("/", list);
		if (text2.Length <= 0)
		{
			return ".";
		}
		return text2;
	}

	public static string NormPathSpelling(object text)
	{
		string text2 = Py.Casefold(string.Join(" ", Py.SplitWs(Py.S(text))));
		if (text2.Length == 0)
		{
			return "";
		}
		if (!LooksLikePathSpelling(text2))
		{
			return text2;
		}
		List<string> list = new List<string>();
		string[] array = text2.Replace("\\", "/").Split('/');
		foreach (string text3 in array)
		{
			list.Add((text3 == "." || text3 == "..") ? text3 : text3.TrimEnd(' ', '.'));
		}
		string text4 = PosixNormPath(string.Join("/", list));
		if (text4 == "." || text4 == "")
		{
			return "";
		}
		return text4;
	}

	public static string EnsureDir(string path)
	{
		Directory.CreateDirectory(AbsPath(path));
		return new PPath(path).ToString();
	}

	public static string DecodeUtf8Strict(byte[] raw, int offset = 0)
	{
		try
		{
			return StrictUtf8.GetString(raw, offset, raw.Length - offset);
		}
		catch (DecoderFallbackException)
		{
			int num = FindBadUtf8(raw, offset);
			byte b = raw[num];
			int num2 = ((b >= 240 && b <= 244) ? 4 : ((b >= 224 && b <= 239) ? 3 : ((b >= 194 && b <= 223) ? 2 : 0)));
			string text = ((num2 == 0) ? "invalid start byte" : ((num + 1 < raw.Length && (num + num2 <= raw.Length || !AllCont(raw, num + 1, raw.Length))) ? "invalid continuation byte" : "unexpected end of data"));
			string text2 = ((text == "unexpected end of data" && raw.Length - num > 1) ? $"position {num - offset}-{raw.Length - 1 - offset}" : $"position {num - offset}");
			if (text == "unexpected end of data" && raw.Length - num > 1)
			{
				throw new UnicodeDecodeError("'utf-8' codec can't decode bytes in " + text2 + ": " + text);
			}
			throw new UnicodeDecodeError($"'utf-8' codec can't decode byte 0x{b:x2} in {text2}: {text}");
		}
	}

	private static bool AllCont(byte[] r, int a, int b)
	{
		for (int i = a; i < b; i++)
		{
			if ((r[i] & 0xC0) != 128)
			{
				return false;
			}
		}
		return true;
	}

	private static int FindBadUtf8(byte[] r, int i)
	{
		while (i < r.Length)
		{
			byte b = r[i];
			if (b < 128)
			{
				i++;
				continue;
			}
			int num = ((b >= 240 && b <= 244) ? 3 : ((b >= 224 && b <= 239) ? 2 : ((b >= 194 && b <= 223) ? 1 : (-1))));
			if (num < 0)
			{
				return i;
			}
			for (int j = 1; j <= num; j++)
			{
				if (i + j >= r.Length || (r[i + j] & 0xC0) != 128)
				{
					return i;
				}
				if (j == 1)
				{
					byte b2 = r[i + 1];
					if ((b == 224 && b2 < 160) || (b == 237 && b2 > 159) || (b == 240 && b2 < 144) || (b == 244 && b2 > 143))
					{
						return i;
					}
				}
			}
			i += num + 1;
		}
		return Math.Max(0, r.Length - 1);
	}

	public static string DecodeUtf8Sig(byte[] raw)
	{
		int offset = ((raw.Length >= 3 && raw[0] == 239 && raw[1] == 187 && raw[2] == 191) ? 3 : 0);
		return DecodeUtf8Strict(raw, offset);
	}

	public static object ReadJson(string path, object dflt = null)
	{
		Exception e = null;
		object obj = JsonFileLock(path);
		string path2 = AbsPath(path);
		for (int i = 0; i < 3; i++)
		{
			try
			{
				byte[] raw;
				lock (obj)
				{
					if (Directory.Exists(path2))
					{
						throw new IOException("[Errno 21] Is a directory: " + Py.ReprStr(path));
					}
					raw = File.ReadAllBytes(path2);
				}
				return PyJson.Loads(DecodeUtf8Sig(raw));
			}
			catch (FileNotFoundException)
			{
				return dflt;
			}
			catch (DirectoryNotFoundException)
			{
				return dflt;
			}
			catch (UnicodeDecodeError)
			{
				throw;
			}
			catch (Exception ex3) when (ex3 is JSONDecodeError || ex3 is IOException || ex3 is UnauthorizedAccessException || ex3 is OSError)
			{
				e = ex3;
				if (i < 2)
				{
					Thread.Sleep(25 * (i + 1));
				}
			}
		}
		log.Warning("读取 JSON 失败 %s: %s", path, Py.ExcStr(e));
		return dflt;
	}

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool MoveFileExW(string existing, string newName, int flags);

	public static void OsReplace(string src, string dst)
	{
		src = AbsPath(src);
		dst = AbsPath(dst);
		if (IS_WINDOWS)
		{
			if (!MoveFileExW(src, dst, 1))
			{
				int lastWin32Error = Marshal.GetLastWin32Error();
				if (lastWin32Error == 5 || lastWin32Error == 32 || lastWin32Error == 33)
				{
					throw new PermissionError($"[WinError {lastWin32Error}] 拒绝访问。: {Py.ReprStr(src)} -> {Py.ReprStr(dst)}");
				}
				throw new OSError($"[WinError {lastWin32Error}] {new Win32Exception(lastWin32Error).Message}: {Py.ReprStr(src)} -> {Py.ReprStr(dst)}");
			}
		}
		else
		{
			if (File.Exists(dst))
			{
				File.Delete(dst);
			}
			File.Move(src, dst);
		}
	}

	public static string MkStemp(string prefix, string suffix, string dir)
	{
		string path = AbsPath(dir);
		for (int i = 0; i < 10000; i++)
		{
			// 用 GUID 生成临时名：new Random() 在 .NET Framework 按 TickCount 取种子，
			// 两个进程同一时刻启动会生成同样的名字，旧写法在另一进程刚好把文件移走时会直接抛「文件已经存在」。
			string text = Path.Combine(path, prefix + Guid.NewGuid().ToString("N").Substring(0, 12) + suffix);
			try
			{
				using (new FileStream(text, FileMode.CreateNew, FileAccess.Write))
				{
				}
				return text;
			}
			catch (IOException) when (i < 9999 && Directory.Exists(path))
			{
			}
		}
		throw new PyException("FileExistsError", "No usable temporary file name found");
	}

	public static void WriteJson(string path, object data)
	{
		PPath pPath = new PPath(path);
		EnsureDir(pPath.Parent);
		byte[] bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(PyJson.Dumps(data, 2, ensureAscii: false));
		lock (JsonFileLock(pPath))
		{
			string text = MkStemp("." + pPath.Name + ".", ".tmp", pPath.Parent);
			try
			{
				using (FileStream fileStream = new FileStream(text, FileMode.Create, FileAccess.Write, FileShare.None))
				{
					fileStream.Write(bytes, 0, bytes.Length);
					fileStream.Flush(flushToDisk: true);
				}
				for (int i = 0; i < 12; i++)
				{
					try
					{
						OsReplace(text, pPath);
						break;
					}
					catch (Exception ex) when (ex is PermissionError || ex is UnauthorizedAccessException || (ex is IOException && IS_WINDOWS))
					{
						if (i == 11)
						{
							throw;
						}
						Thread.Sleep((int)(Math.Min(0.2, 0.03 * (double)(i + 1)) * 1000.0));
					}
				}
			}
			finally
			{
				try
				{
					if (File.Exists(text))
					{
						File.Delete(text);
					}
				}
				catch (Exception)
				{
				}
			}
		}
	}

	private static bool IsLink(FileSystemInfo fi)
	{
		return (fi.Attributes & FileAttributes.ReparsePoint) != 0;
	}

	private static void RmTreeIgnore(DirectoryInfo dir)
	{
		try
		{
			foreach (FileSystemInfo item in dir.EnumerateFileSystemInfos())
			{
				try
				{
					if (item is DirectoryInfo directoryInfo && !IsLink(directoryInfo))
					{
						RmTreeIgnore(directoryInfo);
					}
					else if (item is DirectoryInfo directoryInfo2)
					{
						directoryInfo2.Delete(recursive: false);
					}
					else
					{
						item.Delete();
					}
				}
				catch (Exception)
				{
				}
			}
			dir.Delete(recursive: false);
		}
		catch (Exception)
		{
		}
	}

	public static void RemoveTree(string path, bool missing_ok = true)
	{
		string path2 = AbsPath(path);
		DirectoryInfo directoryInfo = new DirectoryInfo(path2);
		if (directoryInfo.Exists)
		{
			if (!IsLink(directoryInfo))
			{
				RmTreeIgnore(directoryInfo);
			}
			return;
		}
		if (File.Exists(path2))
		{
			try
			{
				File.Delete(path2);
				return;
			}
			catch (Exception)
			{
				return;
			}
		}
		if (missing_ok)
		{
			return;
		}
		throw FileNotFoundError.ForPath(path);
	}

	public static string Hex(byte[] h)
	{
		StringBuilder stringBuilder = new StringBuilder(h.Length * 2);
		foreach (byte b in h)
		{
			stringBuilder.Append(b.ToString("x2"));
		}
		return stringBuilder.ToString();
	}

	private static string HashFile(HashAlgorithm alg, string path)
	{
		using (alg)
		{
			using FileStream inputStream = new FileStream(AbsPath(path), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1048576);
			return Hex(alg.ComputeHash(inputStream));
		}
	}

	public static string Sha1File(string path)
	{
		return HashFile(SHA1.Create(), path);
	}

	public static string Sha256File(string path)
	{
		return HashFile(SHA256.Create(), path);
	}

	public static string Sha512File(string path)
	{
		return HashFile(SHA512.Create(), path);
	}

	public static string Sha1Bytes(byte[] data)
	{
		using SHA1 sHA = SHA1.Create();
		return Hex(sHA.ComputeHash(data));
	}

	public static bool FileMatches(string path, object sha1 = null, object size = null, object sha256 = null)
	{
		string text = AbsPath(path);
		if (!File.Exists(text))
		{
			return false;
		}
		if (size != null && new FileInfo(text).Length != Py.Int(size))
		{
			return false;
		}
		if (Py.Truthy(sha1))
		{
			try
			{
				if (Sha1File(text).ToLowerInvariant() != Py.Str(sha1).ToLowerInvariant())
				{
					return false;
				}
			}
			catch (IOException)
			{
				return false;
			}
			catch (UnauthorizedAccessException)
			{
				return false;
			}
		}
		if (Py.Truthy(sha256))
		{
			try
			{
				if (Sha256File(text).ToLowerInvariant() != Py.Str(sha256).ToLowerInvariant())
				{
					return false;
				}
			}
			catch (IOException)
			{
				return false;
			}
			catch (UnauthorizedAccessException)
			{
				return false;
			}
		}
		if (!Py.Truthy(sha1) && !Py.Truthy(sha256))
		{
			return size != null;
		}
		return true;
	}

	public static string DashedUuid(string value)
	{
		string text = Py.Strip(value ?? "");
		string text2 = text.Replace("-", "");
		if (text2.Length != 32)
		{
			return text;
		}
		string text3 = text2;
		foreach (char value2 in text3)
		{
			if ("0123456789abcdefABCDEF".IndexOf(value2) < 0)
			{
				return text;
			}
		}
		text2 = text2.ToLowerInvariant();
		return text2.Substring(0, 8) + "-" + text2.Substring(8, 4) + "-" + text2.Substring(12, 4) + "-" + text2.Substring(16, 4) + "-" + text2.Substring(20, 12);
	}

	public static string OfflineUuid(string username)
	{
		byte[] array;
		using (MD5 mD = MD5.Create())
		{
			array = mD.ComputeHash(Encoding.UTF8.GetBytes("OfflinePlayer:" + username));
		}
		array[6] = (byte)((array[6] & 0xF) | 0x30);
		array[8] = (byte)((array[8] & 0x3F) | 0x80);
		return DashedUuid(Hex(array));
	}

	public static string FormatSize(object n)
	{
		double num;
		try
		{
			num = Py.Float(n);
		}
		catch (ValueError)
		{
			return "?";
		}
		catch (TypeError)
		{
			return "?";
		}
		string[] array = new string[5] { "B", "KB", "MB", "GB", "TB" };
		foreach (string text in array)
		{
			if (num < 1024.0 || text == "TB")
			{
				if (!(text != "B"))
				{
					return Py.Int(num) + " B";
				}
				return Py.RoundHalfEven(num, 1) + " " + text;
			}
			num /= 1024.0;
		}
		return "?";
	}

	public static ZipArchive OpenZip(string path, ZipArchiveMode mode = ZipArchiveMode.Read)
	{
		return new ZipArchive((mode == ZipArchiveMode.Read) ? new FileStream(AbsPath(path), FileMode.Open, FileAccess.Read, FileShare.Read) : new FileStream(AbsPath(path), (mode == ZipArchiveMode.Create) ? FileMode.Create : FileMode.Open, FileAccess.ReadWrite), mode, leaveOpen: false, Cp437);
	}

	public static string ZipEntryName(ZipArchiveEntry e)
	{
		string text = e.FullName;
		int num = text.IndexOf('\0');
		if (num >= 0)
		{
			text = text.Substring(0, num);
		}
		if (IS_WINDOWS)
		{
			text = text.Replace('\\', '/');
		}
		return text;
	}

	private static bool UnderRoot(string target, string root)
	{
		StringComparison comparisonType = (IS_WINDOWS ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
		if (!target.StartsWith(root + Path.DirectorySeparatorChar, comparisonType))
		{
			return string.Equals(target, root, comparisonType);
		}
		return true;
	}

	public static void SafeExtractZip(string zipPath, string dest)
	{
		EnsureDir(dest);
		string text = Path.GetFullPath(AbsPath(dest)).TrimEnd(Path.DirectorySeparatorChar);
		using ZipArchive zipArchive = OpenZip(zipPath);
		foreach (ZipArchiveEntry entry in zipArchive.Entries)
		{
			string text2 = ZipEntryName(entry);
			string text3 = Path.GetFullPath(Path.Combine(text, text2.Replace('/', Path.DirectorySeparatorChar))).TrimEnd(Path.DirectorySeparatorChar);
			if (!UnderRoot(text3, text))
			{
				throw new ValueError("压缩包包含非法路径: " + text2);
			}
			if (((entry.ExternalAttributes >>> 16) & 0xFFFF & 0xF000) == 40960)
			{
				log.Warning("压缩包中的符号链接被跳过: %s", text2);
				continue;
			}
			if (text2.EndsWith("/"))
			{
				Directory.CreateDirectory(text3);
				continue;
			}
			Directory.CreateDirectory(Path.GetDirectoryName(text3));
			using Stream stream = entry.Open();
			using FileStream destination = new FileStream(text3, FileMode.Create, FileAccess.Write);
			stream.CopyTo(destination);
		}
	}

	public static void SafeExtractTargz(string path, string dest)
	{
		EnsureDir(dest);
		string text = Path.GetFullPath(AbsPath(dest)).TrimEnd(Path.DirectorySeparatorChar);
		List<TarMember> list = new List<TarMember>();
		using (FileStream stream = new FileStream(AbsPath(path), FileMode.Open, FileAccess.Read))
		{
			using GZipStream s = new GZipStream(stream, CompressionMode.Decompress);
			foreach (TarMember item in TarReader.Read(s))
			{
				string target = Path.GetFullPath(Path.Combine(text, item.Name.Replace('/', Path.DirectorySeparatorChar))).TrimEnd(Path.DirectorySeparatorChar);
				if (!UnderRoot(target, text))
				{
					throw new ValueError("压缩包包含非法路径: " + item.Name);
				}
				item.Target = target;
				list.Add(item);
			}
		}
		foreach (TarMember item2 in list)
		{
			if (item2.Type == '2' || item2.Type == '1')
			{
				continue;
			}
			if (item2.Type == '5')
			{
				Directory.CreateDirectory(item2.Target);
			}
			else if (item2.Type == '0' || item2.Type == '\0' || item2.Type == '7')
			{
				Directory.CreateDirectory(Path.GetDirectoryName(item2.Target));
				File.WriteAllBytes(item2.Target, item2.Data ?? new byte[0]);
#if HELL_PORTABLE
				if (!OperatingSystem.IsWindows())
					File.SetUnixFileMode(item2.Target, (UnixFileMode)(item2.Mode & 0x1FF));
#endif
				try
				{
					File.SetLastWriteTimeUtc(item2.Target, new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(item2.MTime));
				}
				catch (Exception)
				{
				}
			}
		}
	}

	public static string FindExecutable(string root, string name_hint = null)
	{
		string[] array = ((name_hint != null) ? new string[1] { name_hint } : new string[3] { "java.exe", "java", "javaw.exe" });
		string[] array2 = new string[4] { "bin", "jre/bin", "jre\\bin", "" };
		foreach (string text in array2)
		{
			PPath pPath = ((text.Length > 0) ? (new PPath(root) / text) : new PPath(root));
			string[] array3 = array;
			foreach (string text2 in array3)
			{
				PPath pPath2 = pPath / text2;
				if (pPath2.IsFile())
				{
					return pPath2;
				}
			}
		}
		HashSet<string> skip = new HashSet<string> { "legal", "lib", "include", "man", "sample", "src", "jmods" };
		return Walk(new PPath(root).ToString(), array, skip);
	}

	private static string Walk(string top, string[] names, HashSet<string> skip)
	{
		List<string> list = new List<string>();
		List<string> list2 = new List<string>();
		try
		{
			foreach (FileSystemInfo item in new DirectoryInfo(AbsPath(top)).EnumerateFileSystemInfos())
			{
				if (item is DirectoryInfo)
				{
					list.Add(item.Name);
				}
				else
				{
					list2.Add(item.Name);
				}
			}
		}
		catch (Exception)
		{
			return null;
		}
		foreach (string item2 in list2)
		{
			if (Array.IndexOf<string>(names, item2) >= 0)
			{
				return PPath.Join(top, item2);
			}
		}
		foreach (string item3 in list)
		{
			if (skip.Contains(item3.ToLowerInvariant()) || item3.StartsWith("."))
			{
				continue;
			}
			string text = PPath.Join(top, item3);
			try
			{
				if (IsLink(new DirectoryInfo(AbsPath(text))))
				{
					continue;
				}
			}
			catch (Exception)
			{
				continue;
			}
			string text2 = Walk(text, names, skip);
			if (text2 != null)
			{
				return text2;
			}
		}
		return null;
	}

	public static (string group, string artifact, string version, string classifier) ParseMavenName(string name)
	{
		string[] array = name.Split(':');
		if (array.Length < 3)
		{
			throw new ValueError("非法 Maven 坐标: " + name);
		}
		return (group: array[0], artifact: array[1], version: array[2], classifier: (array.Length > 3) ? array[3] : null);
	}

	public static string MavenArtifactPath(string name, string suffix = "jar")
	{
		(string group, string artifact, string version, string classifier) tuple = ParseMavenName(name);
		string item = tuple.group;
		string item2 = tuple.artifact;
		string item3 = tuple.version;
		string item4 = tuple.classifier;
		string text = item.Replace('.', '/') + "/" + item2 + "/" + item3 + "/" + item2 + "-" + item3;
		if (item4 != null)
		{
			text = text + "-" + item4;
		}
		return text + "." + suffix;
	}

	public static string ReplacePlaceholders(string text, PyDict mapping)
	{
		return _PLACEHOLDER.Replace(text, delegate(Match m)
		{
			string value = m.Groups[1].Value;
			return (!mapping.ContainsKey(value) || mapping[value] == null) ? m.Value : Py.Str(mapping[value]);
		});
	}

	public static bool HasPlaceholder(string text)
	{
		return _PLACEHOLDER.IsMatch(text);
	}

	public static bool CheckRules(object rules, PyDict features = null)
	{
		if (!Py.Truthy(rules))
		{
			return true;
		}
		if (features == null)
		{
			features = new PyDict();
		}
		bool result = false;
		foreach (object item in (IEnumerable)rules)
		{
			PyDict pyDict = (item as PyDict) ?? new PyDict();
			bool flag = true;
			if (pyDict.ContainsKey("os"))
			{
				PyDict pyDict2 = (pyDict["os"] as PyDict) ?? new PyDict();
				if (pyDict2.ContainsKey("name") && !Py.Eq(pyDict2["name"], OS_NAME))
				{
					flag = false;
				}
				if (pyDict2.ContainsKey("arch") && !Py.Eq(pyDict2["arch"], ARCH))
				{
					flag = false;
				}
				if (pyDict2.ContainsKey("version"))
				{
					try
					{
						string text = Py.Str(pyDict2["version"]);
						bool num = new Regex("\\A(?:" + text + ")", RegexOptions.CultureInvariant).IsMatch(OS_VERSION);
						bool flag2 = new Regex("\\A(?:" + text + ")\\z", RegexOptions.CultureInvariant).IsMatch(OS_VERSION);
						if (!(num || flag2))
						{
							flag = false;
						}
					}
					catch (ArgumentException)
					{
						flag = false;
					}
				}
			}
			if (pyDict.ContainsKey("features") && flag)
			{
				foreach (KeyValuePair<string, object> item2 in Py.DictOr(pyDict["features"]))
				{
					if (Py.Truthy(features.Get(item2.Key)) != Py.Truthy(item2.Value))
					{
						flag = false;
					}
				}
			}
			if (flag)
			{
				result = Py.Eq(pyDict.Get("action", "allow"), "allow");
			}
		}
		return result;
	}

	public static string List2CmdLine(IEnumerable<string> seq)
	{
		StringBuilder stringBuilder = new StringBuilder();
		bool flag = true;
		foreach (string item in seq)
		{
			StringBuilder stringBuilder2 = new StringBuilder();
			if (!flag)
			{
				stringBuilder.Append(' ');
			}
			flag = false;
			bool flag2 = item.IndexOf(' ') >= 0 || item.IndexOf('\t') >= 0 || item.Length == 0;
			if (flag2)
			{
				stringBuilder.Append('"');
			}
			string text = item;
			foreach (char c in text)
			{
				switch (c)
				{
				case '\\':
					stringBuilder2.Append(c);
					continue;
				case '"':
					stringBuilder.Append('\\', stringBuilder2.Length * 2);
					stringBuilder2.Clear();
					stringBuilder.Append("\\\"");
					continue;
				}
				if (stringBuilder2.Length > 0)
				{
					stringBuilder.Append(stringBuilder2);
					stringBuilder2.Clear();
				}
				stringBuilder.Append(c);
			}
			if (stringBuilder2.Length > 0)
			{
				stringBuilder.Append(stringBuilder2);
			}
			if (flag2)
			{
				stringBuilder.Append(stringBuilder2);
				stringBuilder.Append('"');
			}
		}
		return stringBuilder.ToString();
	}

	public static ProcessStartInfo MakeStartInfo(IList<string> cmd, string cwd = null, PyDict env = null)
	{
		ProcessStartInfo processStartInfo = new ProcessStartInfo
		{
			FileName = cmd[0],
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			RedirectStandardInput = true,
			StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
			StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
		};
		List<string> list = new List<string>();
		for (int i = 1; i < cmd.Count; i++)
		{
			list.Add(cmd[i]);
		}
#if HELL_PORTABLE
		foreach (string argument in list) processStartInfo.ArgumentList.Add(argument);
#else
		processStartInfo.Arguments = List2CmdLine(list);
#endif
		if (!string.IsNullOrEmpty(cwd))
		{
			processStartInfo.WorkingDirectory = AbsPath(cwd);
		}
		else
		{
			processStartInfo.WorkingDirectory = ROOT;
		}
		if (env != null)
		{
			processStartInfo.EnvironmentVariables.Clear();
			foreach (KeyValuePair<string, object> item in env)
			{
				processStartInfo.EnvironmentVariables[item.Key] = Py.Str(item.Value);
			}
		}
		return processStartInfo;
	}

	public static int RunProcess(IList<string> cmd, string cwd = null, Action<string> on_line = null, double? timeout = null, PyDict env = null)
	{
		log.Debug("执行命令: %s", string.Join(" ", cmd));
		ProcessStartInfo startInfo = MakeStartInfo(cmd, cwd, env);
		Process proc = Process.Start(startInfo);
		object lineLock;
		try
		{
			try
			{
				proc.StandardInput.Close();
			}
			catch (Exception)
			{
			}
			lineLock = new object();
			Thread thread = new Thread((ThreadStart)delegate
			{
				Pump(proc.StandardOutput);
			})
			{
				IsBackground = true
			};
			Thread thread2 = new Thread((ThreadStart)delegate
			{
				Pump(proc.StandardError);
			})
			{
				IsBackground = true
			};
			thread.Start();
			thread2.Start();
			try
			{
				if (timeout.HasValue)
				{
					if (!proc.WaitForExit((int)Math.Min(2147483647.0, timeout.Value * 1000.0)))
					{
						try
						{
							proc.Kill();
						}
						catch (Exception)
						{
						}
						return -1;
					}
				}
				else
				{
					proc.WaitForExit();
				}
				return proc.ExitCode;
			}
			finally
			{
				thread.Join(5000);
				thread2.Join(5000);
			}
		}
		finally
		{
			if (proc != null)
			{
				((IDisposable)proc).Dispose();
			}
		}
		void Pump(StreamReader r)
		{
			try
			{
				string obj;
				while ((obj = r.ReadLine()) != null)
				{
					if (on_line != null)
					{
						lock (lineLock)
						{
							try
							{
								on_line(obj);
							}
							catch (Exception)
							{
							}
						}
					}
				}
			}
			catch (Exception)
			{
			}
		}
	}
}
