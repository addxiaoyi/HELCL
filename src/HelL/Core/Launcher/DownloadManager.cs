using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using System.Threading;

namespace HelL.Core.Launcher;

public class DownloadManager
{
	public readonly int threads;

	public Action<string, long, long> on_progress;

	public Func<bool> cancel;

	public DownloadTracker tracker;

	public CancellationToken Token;

	private readonly object _lock = new object();

	private int _done;

	private double _last_notify;

	private readonly object _pace_lock = new object();

	private long _paced;

	private readonly double _pace_t0;

	public DownloadManager(object threads = null, Action<string, long, long> on_progress = null, Func<bool> cancel = null, DownloadTracker tracker = null, CancellationToken token = default(CancellationToken))
	{
		this.threads = (int)Math.Max(1L, Py.Int(threads ?? ((object)8L)));
		this.on_progress = on_progress;
		this.cancel = cancel ?? ((Func<bool>)(() => false));
		this.tracker = tracker ?? new DownloadTracker();
		Token = token;
		_pace_t0 = Py.Monotonic();
	}

	private void Pace(long n)
	{
		long num;
		try
		{
			num = Py.Int(Py.Or(Config.CONFIG.Get("download_limit_kbps"), 0L));
		}
		catch (Exception)
		{
			return;
		}
		if (num <= 0 || n <= 0)
		{
			return;
		}
		double num2 = (double)num * 1024.0;
		lock (_pace_lock)
		{
			_paced += n;
			double num3 = Py.Monotonic() - _pace_t0;
			double num4 = (double)_paced / num2 - num3;
			if (num4 > 0.002)
			{
				Thread.Sleep(TimeSpan.FromSeconds(Math.Min(num4, 1.5)));
			}
		}
	}

	internal void NotifyProgress(bool force = false)
	{
		if (on_progress == null)
		{
			return;
		}
		lock (_lock)
		{
			double num = Py.Monotonic();
			if (!force && num - _last_notify < 0.15)
			{
				return;
			}
			_last_notify = num;
		}
		DownloadSnapshot downloadSnapshot = tracker.Snapshot();
		long arg = ((downloadSnapshot.bytes_total != 0L) ? downloadSnapshot.bytes_done : downloadSnapshot.files_done);
		long arg2 = ((downloadSnapshot.bytes_total != 0L) ? downloadSnapshot.bytes_total : downloadSnapshot.files_total);
		string text = downloadSnapshot.status_line ?? "";
		string meta_line = downloadSnapshot.meta_line;
		if (!string.IsNullOrEmpty(meta_line))
		{
			text = ((text.Length > 0) ? (text + "  |  " + meta_line) : meta_line);
		}
		try
		{
			on_progress(text, arg, arg2);
		}
		catch (TaskCancelled)
		{
			throw;
		}
		catch (Exception)
		{
		}
	}

	private void RaiseIfCancel()
	{
		if (Token.IsCancellationRequested || cancel())
		{
			throw new DownloadError("用户取消");
		}
	}

	public Dictionary<string, string> ExtraHeaders(string url)
	{
		Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (!string.IsNullOrEmpty(url) && (url.Contains("api.curseforge.com") || url.Contains("/curseforge/v1/")))
		{
			string value = Config.CurseforgeKey();
			if (!string.IsNullOrEmpty(value))
			{
				dictionary["x-api-key"] = value;
			}
		}
		return dictionary;
	}

	private static IEnumerable<object> AsSeq(object urls)
	{
		if (urls == null)
		{
			yield break;
		}
		if (urls is string text)
		{
			yield return text;
		}
		else if (urls is IEnumerable enumerable)
		{
			foreach (object item in enumerable)
			{
				yield return item;
			}
		}
		else
		{
			yield return urls;
		}
	}

	public List<string> IterUrls(object url, object urls = null, bool expand = true)
	{
		List<object> list = new List<object>();
		if (Py.Truthy(url))
		{
			list.Add(url);
		}
		if (Py.Truthy(urls) || (urls is IEnumerable && !(urls is string)))
		{
			list.AddRange(AsSeq(urls));
		}
		List<string> list2 = new List<string>();
		HashSet<string> hashSet = new HashSet<string>();
		foreach (object item in list)
		{
			foreach (string item2 in expand ? Mirrors.ExpandDownloadUrls(item) : (Py.Truthy(item) ? new List<string> { Py.Str(item) } : new List<string>()))
			{
				if (!string.IsNullOrEmpty(item2) && hashSet.Add(item2))
				{
					list2.Add(item2);
				}
			}
		}
		return list2;
	}

	private List<string> GetUrls(string url, bool expand)
	{
		if (!expand)
		{
			if (!string.IsNullOrEmpty(url))
			{
				return new List<string> { url };
			}
			return new List<string>();
		}
		return IterUrls(url);
	}

	private NetResponse Get(string url, double[] to, IDictionary<string, string> headers, bool allowRedirects = true)
	{
		var started = Stopwatch.StartNew();
		string network = MirrorHealth.NetworkKey;
		var response = Net.Request(url, new Net.RequestOptions
		{
			Headers = headers,
			ConnectTimeout = to[0],
			ReadTimeout = to[1],
			AllowRedirects = allowRedirects,
			Retries = 1,
			Token = Token,
			Tracker = tracker
		});
		tracker.ResponseSource(response.Url);
		response.ResponseMilliseconds = started.Elapsed.TotalMilliseconds;
		response.MirrorNetwork = network;
		return response;
	}

	public object FetchJson(string url, object timeout = null, bool expand = true, IDictionary<string, string> headers = null)
	{
		double[] to = Downloader.RequestTimeout(timeout ?? Py.L(4, 15));
		Exception ex = null;
		foreach (string url2 in GetUrls(url, expand))
		{
			RaiseIfCancel();
			try
			{
				using NetResponse netResponse = Get(url2, to, headers);
				netResponse.RaiseForStatus();
				object document = netResponse.Json();
				MirrorHealth.ReportSuccess(url2, url, netResponse.ResponseMilliseconds, netResponse.MirrorNetwork);
				return document;
			}
			catch (Exception ex2)
			{
				if (ex2 is OperationCanceledException && Token.IsCancellationRequested)
				{
					throw new DownloadError("用户取消", ex2);
				}
				ex = ex2;
				MirrorHealth.ReportFailure(url2, url, ex2);
				if (Downloader.IsCancel(ex2))
				{
					throw new DownloadError("用户取消", ex2);
				}
				if (url2 == url || (url2 ?? "").Contains("github"))
				{
					Utils.log.Warning("fetch_json 失败 %s: %s", url2, Py.ExcStr(ex2));
				}
			}
		}
		if (ex != null)
		{
			Rethrow(ex);
		}
		throw new DownloadError("fetch_json 失败: " + url);
	}

	public string FetchText(string url, object timeout = null, bool expand = true, IDictionary<string, string> headers = null)
	{
		double[] to = Downloader.RequestTimeout(timeout ?? Py.L(4, 15));
		Exception ex = null;
		foreach (string url2 in GetUrls(url, expand))
		{
			RaiseIfCancel();
			try
			{
				using NetResponse netResponse = Get(url2, to, headers);
				netResponse.RaiseForStatus();
				string body = netResponse.Text();
				MirrorHealth.ReportSuccess(url2, url, netResponse.ResponseMilliseconds, netResponse.MirrorNetwork);
				return body;
			}
			catch (Exception ex2)
			{
				if (ex2 is OperationCanceledException && Token.IsCancellationRequested)
				{
					throw new DownloadError("用户取消", ex2);
				}
				ex = ex2;
				MirrorHealth.ReportFailure(url2, url, ex2);
				if (Downloader.IsCancel(ex2))
				{
					throw new DownloadError("用户取消", ex2);
				}
			}
		}
		if (ex != null)
		{
			Rethrow(ex);
		}
		throw new DownloadError("fetch_text 失败: " + url);
	}

	private static void Rethrow(Exception e)
	{
		ExceptionDispatchInfo.Capture(e).Throw();
	}

	public string Download(object url, string dest, string sha1 = null, long? size = null, bool force = false, object timeout = null, string sha512 = null, string sha256 = null, object urls = null, bool expand = true)
	{
		RaiseIfCancel();
		timeout = timeout ?? ((object)300L);
		sha1 = (string.IsNullOrEmpty(sha1) ? null : sha1);
		sha256 = (string.IsNullOrEmpty(sha256) ? null : sha256);
		sha512 = (string.IsNullOrEmpty(sha512) ? null : sha512);
		string text = PPath.Norm(dest);
		string text2 = Utils.AbsPath(text);
		Directory.CreateDirectory(Path.GetDirectoryName(text2));
		string key = text;
		string name = new PPath(text).Name;
		List<string> list = IterUrls(url, urls, expand);
		if (list.Count == 0)
		{
			throw new DownloadError("没有可下载的地址: " + name);
		}
		if (size.HasValue && size.Value != 0L)
		{
			long? num = Downloader.FreeBytes(text);
			if (num.HasValue && num.Value < size.Value + 8388608)
			{
				throw new DownloadError("磁盘空间不足：需要 " + Utils.FormatSize(size.Value) + "，剩余 " + Utils.FormatSize(num.Value));
			}
		}
		tracker.StartFile(key, name, list[0], size);
		NotifyProgress(force: true);
		try
		{
			bool skipped;
			lock (Downloader.PathLock(text))
			{
				skipped = DownloadLocked(list, text, text2, name, sha1, size, force, timeout, sha512, sha256, key);
			}
			long? size2 = ((size.GetValueOrDefault() != 0L) ? size : (File.Exists(text2) ? new long?(new FileInfo(text2).Length) : ((long?)null)));
			tracker.FinishFile(key, size2, skipped);
			NotifyProgress(force: true);
			return text;
		}
		catch (Exception err)
		{
			tracker.FailFile(key, err);
			try
			{
				NotifyProgress(force: true);
			}
			catch (TaskCancelled)
			{
			}
			throw;
		}
	}

	private bool DownloadLocked(List<string> urls, string dest, string abs, string name, string sha1, long? size, bool force, object timeout, string sha512, string sha256, string key)
	{
		RaiseIfCancel();
		if (!force)
		{
			if (sha1 != null || sha256 != null || size.HasValue)
			{
				if (Utils.FileMatches(abs, sha1, size, sha256) && (sha512 == null || Utils.Sha512File(abs).ToLowerInvariant() == sha512.ToLowerInvariant()))
				{
					return true;
				}
			}
			else if (sha512 != null)
			{
				if (File.Exists(abs) && Utils.Sha512File(abs).ToLowerInvariant() == sha512.ToLowerInvariant())
				{
					return true;
				}
			}
			else if (File.Exists(abs) && Downloader.LooksComplete(abs))
			{
				return true;
			}
		}
		string text = abs + ".part";
		bool flag = sha1 != null || sha256 != null || sha512 != null;
		// A size match alone cannot prove that bytes from an earlier source belong to this file.
		bool flag2 = flag;
		if (flag && size.HasValue && File.Exists(text) && new FileInfo(text).Length == size.Value)
		{
			using var completed = new MultiHasher(sha1, sha256, sha512);
			completed.UpdateFile(text);
			bool verified = false;
			try { Verify(urls[0], size.Value, size, sha1, sha256, sha512, completed); verified = true; }
			catch (DownloadError) { Downloader.SafeUnlink(text); }
			if (verified) { RaiseIfCancel(); Utils.OsReplace(text, abs); return false; }
		}
		double[] array = Downloader.RequestTimeout(timeout);
		Exception ex = null;
		LinkedList<KeyValuePair<string, int>> linkedList = new LinkedList<KeyValuePair<string, int>>();
		foreach (string url in urls)
		{
			if (!string.IsNullOrEmpty(url))
			{
				linkedList.AddLast(new KeyValuePair<string, int>(url, 0));
			}
		}
		HashSet<string> hashSet = new HashSet<string>();
		HashSet<string> hashSet2 = new HashSet<string>();
		while (linkedList.Count > 0)
		{
			RaiseIfCancel();
			KeyValuePair<string, int> value = linkedList.First.Value;
			linkedList.RemoveFirst();
			string key2 = value.Key;
			int value2 = value.Value;
			if (hashSet.Contains(key2))
			{
				continue;
			}
			if (value2 > 8)
			{
				ex = new DownloadError("重定向过多: " + key2);
				continue;
			}
			hashSet.Add(key2);
			bool flag3 = false;
			bool flag4 = false;
			for (int i = 0; i < 2; i++)
			{
				RaiseIfCancel();
				string requestNetwork = MirrorHealth.NetworkKey;
				var transferTime = Stopwatch.StartNew();
				double responseTime = 0;
				try
				{
					tracker.ResetConnect();
					Dictionary<string, string> dictionary = ExtraHeaders(key2);
					dictionary["Accept-Encoding"] = "identity";
					long num = 0L;
					if (flag2 && File.Exists(text))
					{
						try
						{
							num = new FileInfo(text).Length;
						}
						catch (Exception)
						{
							num = 0L;
						}
					}
					if (num > 0)
					{
						dictionary["Range"] = "bytes=" + num + "-";
						goto IL_03bf;
					}
					if (hashSet2.Contains(key2) || (size.HasValue && size.Value < 8388608))
					{
						goto IL_03bf;
					}
					string text2;
					try
					{
						text2 = DownloadSegmented(key2, text, size, key, array);
					}
					catch (NoSegment)
					{
						hashSet2.Add(key2);
						text2 = null;
					}
					catch (DownloadError downloadError)
					{
						if (Downloader.IsCancel(downloadError) || Downloader.ShouldSwitchSource(downloadError))
						{
							throw;
						}
						Utils.log.Warning("分片下载失败，改单连接: %s", downloadError.Message);
						hashSet2.Add(key2);
						text2 = null;
					}
					if (text2 == "")
					{
						Utils.OsReplace(text + ".seg", text);
						long length = new FileInfo(text).Length;
						using (MultiHasher multiHasher = new MultiHasher(sha1, sha256, sha512))
						{
							multiHasher.UpdateFile(text);
							tracker.Verify(name);
							NotifyProgress(force: true);
							Verify(key2, length, size, sha1, sha256, sha512, multiHasher);
						}
						if (!flag && !Downloader.LooksComplete(text, dest))
						{
							throw new DownloadError("下载内容无效: " + key2);
						}
						RaiseIfCancel();
						Utils.OsReplace(text, abs);
						MirrorHealth.ReportSuccess(key2, urls[0], transferTime.Elapsed.TotalMilliseconds / Math.Max(1, length / 65536.0), requestNetwork);
						return false;
					}
					if (string.IsNullOrEmpty(text2))
					{
						goto IL_03bf;
					}
					List<string> list = Downloader.EnqueueRedirect(linkedList, hashSet, Downloader.UrlJoin(key2, text2), value2);
					Utils.log.Info("下载重定向 %s -> %s（候选 %d）", key2, text2, list.Count);
					flag4 = true;
					goto end_IL_018d;
					IL_0391:
					if (!flag && !Downloader.LooksComplete(text, dest))
					{
						throw new DownloadError("下载内容无效: " + key2);
					}
					RaiseIfCancel();
					Utils.OsReplace(text, abs);
					MirrorHealth.ReportSuccess(key2, urls[0], responseTime, requestNetwork);
					return false;
					IL_03bf:
					MultiHasher multiHasher2 = new MultiHasher(sha1, sha256, sha512);
					try
					{
						long num6;
						using (NetResponse netResponse = Get(key2, array, dictionary, allowRedirects: false))
						{
							responseTime = netResponse.ResponseMilliseconds;
							if (Downloader.IsRedirect(netResponse))
							{
								string text3 = Downloader.UrlJoin(key2, netResponse.Header("Location") ?? "");
								if (string.IsNullOrEmpty(text3))
								{
									throw new DownloadError("重定向无 Location: " + key2);
								}
								List<string> list2 = Downloader.EnqueueRedirect(linkedList, hashSet, text3, value2);
								Utils.log.Info("下载重定向 %s -> %s（候选 %d）", key2, text3, list2.Count);
								flag4 = true;
								break;
							}
							if (!tracker.DidConnect())
							{
								tracker.Reuse();
							}
							long statusCode = netResponse.StatusCode;
							if (statusCode == 416)
							{
								Downloader.SafeUnlink(text);
								throw new DownloadError("HTTP 416: " + key2);
							}
							if (statusCode == 403 || statusCode == 404 || statusCode == 408 || statusCode == 409 || statusCode == 410 || statusCode == 429 || statusCode >= 500)
							{
								throw new DownloadError("HTTP " + statusCode + ": " + key2);
							}
							bool flag5 = statusCode == 206 && num > 0;
							switch (statusCode)
							{
							case 200L:
								num = 0L;
								break;
							default:
								netResponse.RaiseForStatus();
								break;
							case 206L:
								break;
							}
							if (!long.TryParse(netResponse.Header("Content-Length") ?? "", out var result))
							{
								result = 0L;
							}
							long num5;
							if (statusCode == 206)
							{
								string text4 = netResponse.Header("Content-Range") ?? "";
								Match match = Regex.Match(Py.Strip(text4), "^bytes (\\d+)-(\\d+)/(\\d+)$", RegexOptions.IgnoreCase);
								if (!flag5 || !match.Success)
								{
									throw new DownloadError("无效 Content-Range: " + key2 + " (" + text4 + ")");
								}
								long num2 = long.Parse(match.Groups[1].Value);
								long num3 = long.Parse(match.Groups[2].Value);
								long num4 = long.Parse(match.Groups[3].Value);
								if (num2 != num || num3 < num2 || num3 + 1 != num4 || (size.HasValue && num4 != size.Value) || (netResponse.HasHeader("Content-Length") && result != num3 - num2 + 1))
								{
									throw new DownloadError("无效 Content-Range: " + key2 + " (" + text4 + ")");
								}
								num5 = num4;
							}
							else
							{
								num5 = ((result != 0L) ? result : size.GetValueOrDefault());
							}
							tracker.HttpOk(netResponse.StatusCode, num5, key);
							NotifyProgress(force: true);
							if (flag5 && multiHasher2.Any)
							{
								multiHasher2.UpdateFile(text);
							}
							using (FileStream fileStream = new FileStream(text, flag5 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read))
							{
								num6 = num;
								byte[] array2 = new byte[65536];
								int num7;
								while ((num7 = netResponse.Read(array2, 0, array2.Length)) > 0)
								{
									RaiseIfCancel();
									fileStream.Write(array2, 0, num7);
									multiHasher2.Update(array2, 0, num7);
									num6 += num7;
									tracker.Transfer(key, num6, num5);
									NotifyProgress();
									Pace(num7);
								}
							}
							if (num5 != 0L && num6 != num5 && (statusCode == 206 || !flag))
							{
								throw new DownloadError("下载不完整 " + key2 + " (" + num6 + "/" + num5 + ")");
							}
						}
						tracker.Verify(name);
						NotifyProgress(force: true);
						Verify(key2, num6, size, sha1, sha256, sha512, multiHasher2);
						goto IL_0391;
					}
					finally
					{
						multiHasher2.Dispose();
					}
					end_IL_018d:;
				}
				catch (DownloadError downloadError2)
				{
					ex = downloadError2;
					MirrorHealth.ReportFailure(key2, urls[0], downloadError2, requestNetwork);
					if (!KeepPartOf(downloadError2) && !(flag && Downloader.ShouldSwitchSource(downloadError2)))
					{
						Downloader.SafeUnlink(text);
					}
					if (Downloader.IsCancel(downloadError2) || Downloader.ShouldSwitchSource(downloadError2) ||
						(MirrorHealth.IsKnown(key2) && linkedList.Count > 0 && (downloadError2.Message.Contains("校验失败") || downloadError2.Message.Contains("下载内容无效"))))
					{
						flag3 = true;
						break;
					}
					SleepAttempt(i);
					continue;
				}
				catch (TaskCancelled)
				{
					throw;
				}
				catch (Exception ex3)
				{
					if (ex3 is OperationCanceledException && Token.IsCancellationRequested)
					{
						ex = new DownloadError("用户取消", ex3);
						flag3 = true;
						break;
					}
					ex = ex3;
					MirrorHealth.ReportFailure(key2, urls[0], ex3, requestNetwork);
					if (Downloader.IsCancel(ex3))
					{
						throw new DownloadError("用户取消", ex3);
					}
					if (!flag2)
					{
						Downloader.SafeUnlink(text);
					}
					if (Downloader.TransportDead(ex3))
					{
						flag3 = true;
						break;
					}
					SleepAttempt(i);
					continue;
				}
				break;
			}
			if (flag4)
			{
				continue;
			}
			if (flag3 && ex != null && Downloader.IsCancel(ex))
			{
				if (ex is DownloadError)
				{
					Rethrow(ex);
				}
				throw new DownloadError("用户取消");
			}
			if (ex != null)
			{
				Utils.log.Warning("下载源失败，换下一个: %s", Downloader.ErrStr(ex));
			}
		}
		throw new DownloadError("下载失败 " + name + ": " + Downloader.ErrStr(ex), ex);
	}

	private static bool KeepPartOf(Exception e)
	{
		string text = Downloader.ErrStr(e);
		if (!text.Contains("用户取消"))
		{
			return text.Contains("下载不完整");
		}
		return true;
	}

	private void SleepAttempt(int attempt)
	{
		TimeSpan timeout = TimeSpan.FromSeconds(1.0 * (double)(attempt + 1));
		if (Token.CanBeCanceled)
		{
			Token.WaitHandle.WaitOne(timeout);
		}
		else
		{
			Thread.Sleep(timeout);
		}
	}

	private static void Verify(string url, long got, long? size, string sha1, string sha256, string sha512, MultiHasher mh)
	{
		if (size.HasValue && got != size.Value)
		{
			throw new DownloadError("大小校验失败: " + url + " (期望 size=" + size.Value + ", 实际 " + got + ")");
		}
		if (mh.h1 != null && mh.Sha1 != sha1.ToLowerInvariant())
		{
			throw new DownloadError("sha1 校验失败: " + url);
		}
		if (mh.h256 != null && mh.Sha256 != sha256.ToLowerInvariant())
		{
			throw new DownloadError("sha256 校验失败: " + url);
		}
		if (mh.h512 != null && mh.Sha512 != sha512.ToLowerInvariant())
		{
			throw new DownloadError("sha512 校验失败: " + url);
		}
	}

	private string DownloadSegmented(string url, string part, long? size, string key, double[] req_timeout)
	{
		int num = Downloader.SegmentCount();
		if (num <= 1)
		{
			throw new NoSegment();
		}
		Dictionary<string, string> dictionary = ExtraHeaders(url);
		dictionary["Accept-Encoding"] = "identity";
		dictionary["Range"] = "bytes=0-0";
		long total;
		using (NetResponse netResponse = Get(url, req_timeout, dictionary, allowRedirects: false))
		{
			if (Downloader.IsRedirect(netResponse))
			{
				string obj = netResponse.Header("Location") ?? "";
				if (obj.Length == 0)
				{
					throw new DownloadError("重定向无 Location: " + url);
				}
				return obj;
			}
			if (!tracker.DidConnect())
			{
				tracker.Reuse();
			}
			long statusCode = netResponse.StatusCode;
			if (statusCode == 403 || statusCode == 404 || statusCode == 408 || statusCode == 409 || statusCode == 410 || statusCode == 429 || statusCode >= 500)
			{
				throw new DownloadError("HTTP " + statusCode + ": " + url);
			}
			if (statusCode != 206)
			{
				throw new NoSegment();
			}
			Match match = Regex.Match(Py.Strip(netResponse.Header("Content-Range") ?? ""), "^bytes 0-0/(\\d+)$", RegexOptions.IgnoreCase);
			if (!match.Success)
			{
				throw new NoSegment();
			}
			total = long.Parse(match.Groups[1].Value);
		}
		if (size.HasValue && total != size.Value)
		{
			throw new DownloadError("大小校验失败: " + url + " (期望 size=" + size.Value + ", 服务器 " + total + ")");
		}
		if (total < 8388608)
		{
			throw new NoSegment();
		}
		List<long[]> list = Downloader.SplitRanges(total, num);
		if (list.Count < 2)
		{
			throw new NoSegment();
		}
		string seg = part + ".seg";
		Downloader.SafeUnlink(seg);
		using (FileStream fileStream = new FileStream(seg, FileMode.Create, FileAccess.Write))
		{
			fileStream.SetLength(total);
		}
		tracker.HttpOk(206L, total, key);
		NotifyProgress(force: true);
		string fileName = Path.GetFileName(part);
		Utils.log.Info("分片下载 %s：%d 段，共 %s", fileName.Substring(0, fileName.Length - 5), list.Count, Utils.FormatSize(total));
		long done = 0L;
		object dlock = new object();
		ManualResetEventSlim failed = new ManualResetEventSlim(initialState: false);
		List<Exception> errors = new List<Exception>();
		try
		{
			List<Thread> list2 = new List<Thread>();
			foreach (long[] item2 in list)
			{
				long a = item2[0];
				long b = item2[1];
				Thread thread = new Thread((ThreadStart)delegate
				{
					try
					{
						Worker(a, b);
					}
					catch (Exception item)
					{
						failed.Set();
						lock (errors)
						{
							errors.Add(item);
						}
					}
				})
				{
					IsBackground = true,
					Name = "seg"
				};
				list2.Add(thread);
				thread.Start();
			}
			foreach (Thread item3 in list2)
			{
				item3.Join();
			}
			Exception ex = null;
			foreach (Exception item4 in errors)
			{
				if (ex == null || Downloader.IsCancel(item4))
				{
					ex = item4;
				}
			}
			if (ex != null)
			{
				Rethrow(ex);
			}
			if (done != total)
			{
				throw new DownloadError("下载不完整 " + url + " (" + done + "/" + total + ")");
			}
		}
		catch (Exception)
		{
			tracker.Transfer(key, 0L, total);
			Downloader.SafeUnlink(seg);
			throw;
		}
		return "";
		void Add(long n)
		{
			long got;
			lock (dlock)
			{
				done += n;
				got = done;
			}
			tracker.Transfer(key, got, total);
			NotifyProgress();
			Pace(n);
		}
		void Worker(long start, long end)
		{
			long num2 = start;
			Exception ex3 = null;
			for (int num3 = 0; num3 < 3; num3++)
			{
				if (!failed.IsSet)
				{
					try
					{
						Dictionary<string, string> dictionary2 = ExtraHeaders(url);
						dictionary2["Accept-Encoding"] = "identity";
						dictionary2["Range"] = "bytes=" + num2 + "-" + end;
						using (NetResponse netResponse2 = Get(url, req_timeout, dictionary2, allowRedirects: false))
						{
							if (netResponse2.StatusCode != 206)
							{
								throw new DownloadError("HTTP " + netResponse2.StatusCode + ": " + url);
							}
							string text = Py.Strip(netResponse2.Header("Content-Range") ?? "");
							Match match2 = Regex.Match(text, "^bytes (\\d+)-(\\d+)/(\\d+)$", RegexOptions.IgnoreCase);
							if (!match2.Success || !long.TryParse(match2.Groups[1].Value, out long rangeStart) || !long.TryParse(match2.Groups[2].Value, out long rangeEnd) || !long.TryParse(match2.Groups[3].Value, out long rangeTotal) || rangeStart != num2 || rangeEnd != end || rangeTotal != total)
							{
								throw new DownloadError("无效 Content-Range: " + url + " (" + text + ")");
							}
							using FileStream fileStream2 = new FileStream(seg, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
							fileStream2.Seek(num2, SeekOrigin.Begin);
							byte[] array = new byte[65536];
							int num4;
							while ((num4 = netResponse2.Read(array, 0, array.Length)) > 0)
							{
								RaiseIfCancel();
								if (failed.IsSet)
								{
									return;
								}
								long num5 = end + 1 - num2;
								if (num4 > num5)
								{
									num4 = (int)num5;
								}
								fileStream2.Write(array, 0, num4);
								num2 += num4;
								Add(num4);
								if (num2 > end)
								{
									break;
								}
							}
						}
						if (num2 > end)
						{
							return;
						}
						throw new DownloadError("下载不完整 " + url + " (分片 " + start + "-" + end + " 停在 " + num2 + ")");
					}
					catch (DownloadError downloadError)
					{
						ex3 = downloadError;
						if (!Downloader.IsCancel(downloadError) && !Downloader.ShouldSwitchSource(downloadError))
						{
							goto IL_02f9;
						}
					}
					catch (TaskCancelled inner)
					{
						ex3 = new DownloadError("用户取消", inner);
					}
					catch (Exception ex4)
					{
						if (ex4 is OperationCanceledException && Token.IsCancellationRequested)
						{
							ex3 = new DownloadError("用户取消", ex4);
						}
						else
						{
							ex3 = ex4;
							if (!Downloader.TransportDead(ex4))
							{
								goto IL_02f9;
							}
						}
					}
					break;
				}
				return;
				IL_02f9:
				TimeSpan timeout = TimeSpan.FromSeconds(0.5 * (double)(num3 + 1));
				if (Token.CanBeCanceled)
				{
					Token.WaitHandle.WaitOne(timeout);
				}
				else
				{
					Thread.Sleep(timeout);
				}
			}
			failed.Set();
			throw (ex3 is DownloadError) ? ex3 : new DownloadError("分片失败 " + url + ": " + Downloader.ErrStr(ex3), ex3);
		}
	}

	public bool DownloadAll(IEnumerable<DownloadTask> tasksIn, string message = "下载中")
	{
		Dictionary<string, DownloadTask> merged = new Dictionary<string, DownloadTask>();
		Dictionary<string, List<object>> mergedUrls = new Dictionary<string, List<object>>();
		List<string> list = new List<string>();
		foreach (DownloadTask item in tasksIn)
		{
			string text = Utils.AbsPath(item.Dest);
			List<object> list2 = ((item.Url is string || item.Url == null) ? new List<object> { item.Url } : AsSeq(item.Url).ToList());
			if (!merged.ContainsKey(text))
			{
				merged[text] = new DownloadTask(null, item.Dest, item.Sha1, item.Size, item.Sha512);
				mergedUrls[text] = new List<object>(list2);
				list.Add(text);
				continue;
			}
			List<object> list3 = mergedUrls[text];
			HashSet<string> hashSet = new HashSet<string>(list3.Select((object x) => Py.Str(x)));
			foreach (object item2 in list2)
			{
				if (Py.Truthy(item2) && hashSet.Add(Py.Str(item2)))
				{
					list3.Add(item2);
				}
			}
			DownloadTask downloadTask = merged[text];
			if (downloadTask.Sha1 == null)
			{
				downloadTask.Sha1 = item.Sha1;
			}
			if (!downloadTask.Size.HasValue)
			{
				downloadTask.Size = item.Size;
			}
			if (downloadTask.Sha512 == null)
			{
				downloadTask.Sha512 = item.Sha512;
			}
		}
		List<DownloadTask> list4 = list.Select(delegate(string k)
		{
			DownloadTask downloadTask2 = merged[k];
			downloadTask2.Url = mergedUrls[k];
			return downloadTask2;
		}).ToList();
		int count = list4.Count;
		List<string> list5 = new List<string>();
		_done = 0;
		long num = list4.Where((DownloadTask t) => t.Size.GetValueOrDefault() != 0).Sum((DownloadTask t) => t.Size.Value);
		if (num != 0L)
		{
			long? num2 = ((list4.Count > 0) ? Downloader.FreeBytes(list4[0].Dest) : ((long?)null));
			if (num2.HasValue && num2.Value < num + 67108864)
			{
				throw new DownloadError("磁盘空间不足：需要 " + Utils.FormatSize(num) + "，剩余 " + Utils.FormatSize(num2.Value));
			}
		}
		tracker.BeginBatch(message, count, num);
		NotifyProgress(force: true);
		bool flag = false;
		try
		{
			Queue<DownloadTask> queue = new Queue<DownloadTask>(list4);
			BlockingCollection<KeyValuePair<DownloadTask, Exception>> results = new BlockingCollection<KeyValuePair<DownloadTask, Exception>>();
			bool stop = false;
			int num3 = Math.Min(threads, Math.Max(1, count));
			List<Thread> list6 = new List<Thread>();
			for (int num4 = 0; num4 < num3; num4++)
			{
				if (count <= 0)
				{
					break;
				}
				Thread thread = new Thread((ThreadStart)delegate
				{
					while (true)
					{
						DownloadTask downloadTask2;
						bool flag2;
						lock (queue)
						{
							if (queue.Count == 0)
							{
								break;
							}
							downloadTask2 = queue.Dequeue();
							flag2 = stop;
						}
						if (flag2)
						{
							results.Add(new KeyValuePair<DownloadTask, Exception>(downloadTask2, new OperationCanceledException()));
						}
						else
						{
							Exception value2 = null;
							try
							{
								TaskDownload(downloadTask2);
							}
							catch (Exception ex3)
							{
								value2 = ex3;
							}
							results.Add(new KeyValuePair<DownloadTask, Exception>(downloadTask2, value2));
						}
					}
				})
				{
					IsBackground = true,
					Name = "dl"
				};
				list6.Add(thread);
				thread.Start();
			}
			Exception ex = null;
			for (int num5 = 0; num5 < count; num5++)
			{
				KeyValuePair<DownloadTask, Exception> keyValuePair = results.Take();
				Exception value = keyValuePair.Value;
				if (value != null)
				{
					if (Downloader.IsCancel(value) || value is OperationCanceledException)
					{
						flag = true;
					}
					else
					{
						list5.Add(new PPath(keyValuePair.Key.Dest).Name + ": " + Downloader.ErrStr(value));
					}
				}
				if (flag || cancel())
				{
					flag = true;
					lock (queue)
					{
						stop = true;
					}
				}
				lock (_lock)
				{
					_done++;
				}
				if (ex != null)
				{
					continue;
				}
				try
				{
					NotifyProgress(force: true);
				}
				catch (TaskCancelled taskCancelled)
				{
					ex = taskCancelled;
					flag = true;
					lock (queue)
					{
						stop = true;
					}
				}
			}
			foreach (Thread item3 in list6)
			{
				item3.Join();
			}
			if (ex != null)
			{
				tracker.EndBatch(ok: false, "已取消");
				Rethrow(ex);
			}
			if (flag)
			{
				tracker.EndBatch(ok: false, "已取消");
				throw new DownloadError("用户取消");
			}
			if (list5.Count > 0)
			{
				tracker.EndBatch(ok: false, message + "失败 " + list5.Count + "/" + count);
				throw new DownloadError(message + "失败（" + list5.Count + "/" + count + " 个文件）: " + string.Join("; ", list5.Take(8)));
			}
			tracker.EndBatch(ok: true, message + "完成");
			NotifyProgress(force: true);
			return true;
		}
		catch (DownloadError)
		{
			throw;
		}
		catch (TaskCancelled)
		{
			throw;
		}
		catch (Exception)
		{
			tracker.EndBatch(ok: false);
			throw;
		}
	}

	private string TaskDownload(DownloadTask task)
	{
		if (task.Url is IEnumerable && !(task.Url is string))
		{
			List<object> list = AsSeq(task.Url).ToList();
			object url = ((list.Count > 0) ? list[0] : null);
			return Download(url, task.Dest, task.Sha1, task.Size, force: false, null, task.Sha512, null, list);
		}
		return Download(task.Url, task.Dest, task.Sha1, task.Size, force: false, null, task.Sha512);
	}

	public static void ExtractZip(string zip_path, string dest)
	{
		Utils.EnsureDir(dest);
		Utils.SafeExtractZip(zip_path, dest);
	}

	public static void ExtractTargz(string path, string dest)
	{
		Utils.EnsureDir(dest);
		Utils.SafeExtractTargz(path, dest);
	}

	public static void ExtractArchive(string path, string dest)
	{
		PPath pPath = new PPath(path);
		string text = pPath.Name.ToLowerInvariant();
		if (text.EndsWith(".zip") || text.EndsWith(".jar"))
		{
			ExtractZip(path, dest);
			return;
		}
		if (text.EndsWith(".tar.gz") || text.EndsWith(".tgz"))
		{
			ExtractTargz(path, dest);
			return;
		}
		throw new DownloadError("不支持的压缩格式: " + pPath.Name);
	}

	public static void ExtractJarNatives(string jar_path, string dest, IList<string> exclude = null)
	{
		dest = Utils.EnsureDir(dest);
		string fullPath = Path.GetFullPath(Utils.AbsPath(dest));
		exclude = exclude ?? new List<string>();
		using ZipArchive zipArchive = Utils.OpenZip(jar_path);
		foreach (ZipArchiveEntry entry in zipArchive.Entries)
		{
			string name = Utils.ZipEntryName(entry);
			if (name.EndsWith("/") || exclude.Any((string prefix) => name.StartsWith(prefix)))
			{
				continue;
			}
			string fullPath2 = Path.GetFullPath(Path.Combine(fullPath, name));
			if (!fullPath2.StartsWith(fullPath + Path.DirectorySeparatorChar))
			{
				throw new DownloadError("压缩包包含非法路径: " + name);
			}
			Directory.CreateDirectory(Path.GetDirectoryName(fullPath2));
			using Stream stream = entry.Open();
			using FileStream destination = new FileStream(fullPath2, FileMode.Create, FileAccess.Write);
			stream.CopyTo(destination, 262144);
		}
	}
}
