using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace HelL.Core.Launcher;

public static class Downloader
{
	public const long MIN_FREE_PAD = 67108864L;

	public const long SEG_MIN = 8388608L;

	public const long SEG_CHUNK = 2097152L;

	public const int SEG_MAX = 16;

	private static readonly long[] RedirectCodes = new long[5] { 301L, 302L, 303L, 307L, 308L };

	private static readonly string[] DeadTokens = new string[10] { "sslcertverificationerror", "certificate_verify_failed", "unable to get local issuer", "sslerror", "ssleoferror", "proxyerror", "newconnectionerror", "connecttimeouterror", "connection refused", "max retries exceeded" };

	private static readonly Regex SwitchRe = new Regex("HTTP (403|404|408|409|410|429|5\\d{2})\\b", RegexOptions.CultureInvariant);

	private static readonly Dictionary<string, object> _PATH_LOCKS = new Dictionary<string, object>(Utils.IS_WINDOWS ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

	public const double CONNECT_TIMEOUT = 8.0;

	public const double READ_TIMEOUT_DEFAULT = 90.0;

	public static double[] RequestTimeout(object timeout)
	{
		if (timeout is List<object> { Count: >=2 } list)
		{
			return new double[2]
			{
				Math.Max(1.0, Py.Float(list[0])),
				Math.Max(1.0, Py.Float(list[1]))
			};
		}
		if (timeout is double[] array && array.Length >= 2)
		{
			return new double[2]
			{
				Math.Max(1.0, array[0]),
				Math.Max(1.0, array[1])
			};
		}
		if (timeout != null)
		{
			double num = Py.Float(timeout);
			return new double[2]
			{
				Math.Min(8.0, Math.Max(2.0, num)),
				Math.Max(num, 8.0)
			};
		}
		return new double[2] { 8.0, 90.0 };
	}

	public static long? FreeBytes(string path)
	{
		try
		{
			string pathRoot = Path.GetPathRoot(Utils.AbsPath(path));
			if (string.IsNullOrEmpty(pathRoot))
			{
				return null;
			}
			return new DriveInfo(pathRoot).AvailableFreeSpace;
		}
		catch (Exception)
		{
			return null;
		}
	}

	public static bool LooksComplete(string path, string dest = null)
	{
		string text = Utils.AbsPath(path);
		byte[] array;
		try
		{
			FileInfo fileInfo = new FileInfo(text);
			if (!fileInfo.Exists)
			{
				return false;
			}
			if (fileInfo.Length < 16)
			{
				return false;
			}
			using FileStream fileStream = new FileStream(text, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
			array = new byte[32];
			int i;
			int num;
			for (i = 0; i < 32; i += num)
			{
				if ((num = fileStream.Read(array, i, 32 - i)) <= 0)
				{
					break;
				}
			}
			Array.Resize(ref array, i);
		}
		catch (IOException)
		{
			return false;
		}
		catch (UnauthorizedAccessException)
		{
			return false;
		}
		string text2 = new PPath(dest ?? path).Suffix.ToLowerInvariant();
		if (text2 == ".jar" || text2 == ".zip")
		{
			if (array.Length >= 2 && array[0] == 80)
			{
				return array[1] == 75;
			}
			return false;
		}
		int j;
		for (j = 0; j < array.Length && (array[j] == 32 || (array[j] >= 9 && array[j] <= 13)); j++)
		{
		}
		string text3 = Encoding.ASCII.GetString(array, j, array.Length - j).ToLowerInvariant();
		if (!text3.StartsWith("<html") && !text3.StartsWith("<!doctype"))
		{
			return !text3.StartsWith("error");
		}
		return false;
	}

	internal static bool IsRedirect(NetResponse r)
	{
		if (!r.IsRedirect)
		{
			return RedirectCodes.Contains(r.StatusCode);
		}
		return true;
	}

	public static bool TransportDead(Exception err)
	{
		string msg = ErrStr(err).ToLowerInvariant();
		return DeadTokens.Any((string t) => msg.Contains(t));
	}

	public static bool ShouldSwitchSource(Exception err)
	{
		string text = ErrStr(err);
		if (text.Contains("用户取消"))
		{
			return true;
		}
		if (SwitchRe.IsMatch(text))
		{
			return true;
		}
		return TransportDead(err);
	}

	public static int SegmentCount()
	{
		long val;
		try
		{
			val = Py.Int(Py.Or(Config.CONFIG.Get("download_segments", 4L), 1L));
		}
		catch (Exception)
		{
			val = 4L;
		}
		return (int)Math.Max(1L, Math.Min(16L, val));
	}

	public static List<long[]> SplitRanges(long total, int parts)
	{
		long num = total / 2097152;
		if (num == 0L)
		{
			num = 1L;
		}
		long num2 = Math.Max(1L, Math.Min(parts, num));
		long num3 = (total + num2 - 1) / num2;
		List<long[]> list = new List<long[]>();
		for (long num4 = 0L; num4 < total; num4 += num3)
		{
			list.Add(new long[2]
			{
				num4,
				Math.Min(num4 + num3, total) - 1
			});
		}
		return list;
	}

	private static bool KeepPart(Exception err)
	{
		string text = ErrStr(err);
		if (!text.Contains("用户取消"))
		{
			return text.Contains("下载不完整");
		}
		return true;
	}

	internal static bool IsCancel(Exception err)
	{
		if (err == null)
		{
			return false;
		}
		if (err is TaskCancelled)
		{
			return true;
		}
		return ErrStr(err).Contains("用户取消");
	}

	internal static string ErrStr(Exception e)
	{
		if (e != null)
		{
			return Py.ExcStr(e);
		}
		return "None";
	}

	public static string UrlJoin(string baseUrl, string loc)
	{
		if (string.IsNullOrEmpty(loc))
		{
			return baseUrl;
		}
		if (Regex.IsMatch(loc, "^[A-Za-z][A-Za-z0-9+.-]*://"))
		{
			return loc;
		}
		try
		{
			return new Uri(new Uri(baseUrl), loc).AbsoluteUri;
		}
		catch (Exception)
		{
			return loc;
		}
	}

	internal static List<string> EnqueueRedirect(LinkedList<KeyValuePair<string, int>> pending, HashSet<string> tried, string location, int depth)
	{
		List<string> list = (Source.IsGithubUrl(location) ? Mirrors.ExpandDownloadUrls(location) : new List<string> { location });
		for (int num = list.Count - 1; num >= 0; num--)
		{
			string text = list[num];
			if (!string.IsNullOrEmpty(text) && !tried.Contains(text))
			{
				pending.AddFirst(new KeyValuePair<string, int>(text, depth + 1));
			}
		}
		return list;
	}

	internal static object PathLock(string dest)
	{
		string key = Utils.AbsPath(dest);
		lock (_PATH_LOCKS)
		{
			if (!_PATH_LOCKS.TryGetValue(key, out object value))
			{
				value = new object();
				_PATH_LOCKS[key] = value;
			}
			return value;
		}
	}

	internal static void SafeUnlink(string path)
	{
		try
		{
			File.Delete(Utils.AbsPath(path));
		}
		catch (Exception)
		{
		}
	}
}
