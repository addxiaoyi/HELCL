using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace HelL.Core.Launcher;

public static class Thumbnails
{
	private static readonly object _THUMB_LOCK = new object();

	private const double _CACHE_TTL = 604800.0;

	private const double _FAIL_TTL = 600.0;

	private const int _FAIL_CAP = 512;

	private static readonly Dictionary<string, double> _recent_failures = new Dictionary<string, double>();

	private static readonly double[] _THUMB_TIMEOUT = new double[2] { 4.0, 10.0 };

	private const string _MCIM = "https://mod.mcimirror.top";

	private static readonly string[][] _IMAGE_MIRRORS = new string[4][]
	{
		new string[2] { "https://cdn.modrinth.com/", "https://mod.mcimirror.top/" },
		new string[2] { "https://media.forgecdn.net/", "https://mod.mcimirror.top/" },
		new string[2] { "https://mediafilez.forgecdn.net/", "https://mod.mcimirror.top/" },
		new string[2] { "https://edge.forgecdn.net/", "https://mod.mcimirror.top/" }
	};

	private static DownloadManager _DM;

	private static readonly object _DM_LOCK = new object();

	private static readonly HashSet<string> _refreshing = new HashSet<string>();

	private static string _thumb_dir()
	{
		string text = PPath.Join(Config.CONFIG.cache_dir, "thumbs");
		Directory.CreateDirectory(PPath.Abs(text));
		return text;
	}

	private static string _hash_url(string url)
	{
		using SHA1 sHA = SHA1.Create();
		byte[] array = sHA.ComputeHash(Encoding.UTF8.GetBytes(url ?? ""));
		StringBuilder stringBuilder = new StringBuilder();
		byte[] array2 = array;
		foreach (byte b in array2)
		{
			stringBuilder.Append(b.ToString("x2"));
		}
		return stringBuilder.ToString().Substring(0, 24);
	}

	private static string _ext_from_url(string url)
	{
		string text = UrlPath(url);
		string text2 = Py.Lower(new PPath(false, text).Suffix);
		switch (text2)
		{
		case ".png":
		case ".jpg":
		case ".jpeg":
		case ".webp":
		case ".gif":
		case ".ico":
			return text2;
		default:
			return ".png";
		}
	}

	private static string UrlPath(string url)
	{
		string text = url;
		int num = text.IndexOf('#');
		if (num >= 0)
		{
			text = text.Substring(0, num);
		}
		int num2 = text.IndexOf('?');
		if (num2 >= 0)
		{
			text = text.Substring(0, num2);
		}
		int num3 = text.IndexOf(':');
		if (num3 > 0)
		{
			string text2 = text.Substring(0, num3);
			bool flag = char.IsLetter(text2[0]);
			string text3 = text2;
			foreach (char c in text3)
			{
				if (c > '\u007f' || (!char.IsLetterOrDigit(c) && c != '+' && c != '-' && c != '.'))
				{
					flag = false;
				}
			}
			if (flag)
			{
				text = text.Substring(num3 + 1);
			}
		}
		if (text.StartsWith("//"))
		{
			int num4 = text.IndexOf('/', 2);
			text = ((num4 >= 0) ? text.Substring(num4) : "");
		}
		int num5 = text.LastIndexOf(';');
		if (num5 >= 0 && text.IndexOf('/', num5) < 0)
		{
			text = text.Substring(0, num5);
		}
		return text;
	}

	public static string ThumbPath(object url)
	{
		if (!Py.Truthy(url))
		{
			return "";
		}
		string url2 = Py.Str(url);
		return PPath.Join(_thumb_dir(), _hash_url(url2) + _ext_from_url(url2));
	}

	private static DownloadManager _shared_dm()
	{
		lock (_DM_LOCK)
		{
			return _DM ?? (_DM = new DownloadManager(8L));
		}
	}

	private static string _community_mode()
	{
		try
		{
			return Source.CommunityMode();
		}
		catch (Exception)
		{
			return "auto";
		}
	}

	public static List<object> ThumbCandidates(string url)
	{
		if (string.IsNullOrEmpty(url))
		{
			return new List<object>();
		}
		if (url.StartsWith("http://", StringComparison.Ordinal))
		{
			url = "https://" + url.Substring(7);
		}
		string text = null;
		string[][] iMAGE_MIRRORS = _IMAGE_MIRRORS;
		foreach (string[] array in iMAGE_MIRRORS)
		{
			if (url.StartsWith(array[0], StringComparison.Ordinal))
			{
				text = array[1] + url.Substring(array[0].Length);
				break;
			}
		}
		if (text == null || _community_mode() == "official")
		{
			return new List<object> { url };
		}
		return new List<object> { text, url };
	}

	public static bool RecentlyFailed(string url)
	{
		if (string.IsNullOrEmpty(url))
		{
			return false;
		}
		lock (_THUMB_LOCK)
		{
			if (!_recent_failures.TryGetValue(url, out var value))
			{
				return false;
			}
			if (Py.Time() - value >= 600.0)
			{
				_recent_failures.Remove(url);
				return false;
			}
			return true;
		}
	}

	private static void _note_failure(string url)
	{
		lock (_THUMB_LOCK)
		{
			_recent_failures.Remove(url);
			if (_recent_failures.Count >= 512)
			{
				List<KeyValuePair<string, double>> list = _recent_failures.OrderBy((KeyValuePair<string, double> kv) => kv.Value).Skip(_recent_failures.Count - 256).ToList();
				_recent_failures.Clear();
				foreach (KeyValuePair<string, double> item in list)
				{
					_recent_failures[item.Key] = item.Value;
				}
			}
			_recent_failures[url] = Py.Time();
		}
	}

	private static void _forget_failure(string url)
	{
		lock (_THUMB_LOCK)
		{
			_recent_failures.Remove(url);
		}
	}

	private static bool _fetch(string url, string local, DownloadManager dm)
	{
		List<object> list = ThumbCandidates(url);
		try
		{
			DownloadManager obj = dm ?? _shared_dm();
			object url2 = list[0];
			object urls = list.Skip(1).ToList();
			object tHUMB_TIMEOUT = _THUMB_TIMEOUT;
			obj.Download(url2, local, null, null, force: true, tHUMB_TIMEOUT, null, null, urls, expand: false);
		}
		catch (Exception)
		{
			return false;
		}
		return true;
	}

	private static void _refresh_in_background(string url, string local)
	{
		lock (_THUMB_LOCK)
		{
			if (!_refreshing.Add(url))
			{
				return;
			}
		}
		Thread thread = new Thread((ThreadStart)delegate
		{
			try
			{
				if (_fetch(url, local, null))
				{
					return;
				}
				try
				{
					File.SetLastWriteTimeUtc(PPath.Abs(local), DateTime.UtcNow);
				}
				catch (Exception)
				{
				}
			}
			finally
			{
				lock (_THUMB_LOCK)
				{
					_refreshing.Remove(url);
				}
			}
		});
		thread.Name = "tipcl-thumb-refresh";
		thread.IsBackground = true;
		thread.Start();
	}

	public static string EnsureThumb(object urlObj, DownloadManager dm = null)
	{
		if (!Py.Truthy(urlObj))
		{
			return "";
		}
		string url = Py.Str(urlObj);
		string text = ThumbPath(url);
		FileInfo fileInfo = new FileInfo(PPath.Abs(text));
		if (fileInfo.Exists && fileInfo.Length > 0)
		{
			if (Py.Time() - LocalFs.MTime(text) >= 604800.0)
			{
				_refresh_in_background(url, text);
			}
			return text;
		}
		if (RecentlyFailed(url))
		{
			return "";
		}
		if (!_fetch(url, text, dm))
		{
			_note_failure(url);
			return "";
		}
		_forget_failure(url);
		return text;
	}
}
