using System;
using System.Collections.Generic;
using System.Threading;

namespace HelL.Core.Launcher;

public sealed class DownloadTracker
{
	public static readonly AsyncLocal<DownloadTracker> Current = new AsyncLocal<DownloadTracker>();

	private readonly object _lock = new object();

	private readonly SpeedMeter _speed = new SpeedMeter();

	private int _active;

	private bool _in_batch;

	private readonly Dictionary<string, long> _inflight = new Dictionary<string, long>();

	private Dictionary<string, long> _file_expected = new Dictionary<string, long>();

	private long _completed_bytes;

	private readonly ThreadLocal<bool> _tls = new ThreadLocal<bool>();

	public string phase;

	public string detail;

	public string filename;

	public string url;

	public string host;

	public string ip;

	public string tls_version;

	public string handshake;

	public string error;

	public long port;

	public long http_status;

	public long bytes_done;

	public long bytes_total;

	public long files_done;

	public long files_total;

	public bool saw_connect;

	public DownloadTracker()
	{
		ResetLocked();
	}

	private void ResetLocked()
	{
		phase = "idle";
		detail = "就绪";
		filename = "";
		url = "";
		host = "";
		ip = "";
		port = 0L;
		http_status = 0L;
		tls_version = "";
		handshake = "";
		bytes_done = 0L;
		bytes_total = 0L;
		files_done = 0L;
		files_total = 0L;
		error = "";
		saw_connect = false;
		_inflight.Clear();
		_file_expected = new Dictionary<string, long>();
		_completed_bytes = 0L;
		_speed.Reset();
	}

	public void ResetConnect()
	{
		_tls.Value = false;
	}

	public void ResponseSource(string address)
	{
		if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)) return;
		lock (_lock)
		{
			if (!string.Equals(host, uri.Host, StringComparison.OrdinalIgnoreCase)) { ip = ""; tls_version = ""; handshake = ""; }
			url = address; host = uri.Host; port = uri.Port;
		}
	}

	public void MarkConnect()
	{
		_tls.Value = true;
		saw_connect = true;
	}

	public bool DidConnect()
	{
		return _tls.Value;
	}

	public bool Watching()
	{
		return _active > 0;
	}

	public void BeginBatch(string message, long file_count, long total_bytes = 0L)
	{
		lock (_lock)
		{
			_in_batch = true;
			_active++;
			_inflight.Clear();
			_file_expected.Clear();
			_completed_bytes = 0L;
			_speed.Reset();
			phase = "prepare";
			detail = (string.IsNullOrEmpty(message) ? "准备下载" : message);
			filename = "";
			files_done = 0L;
			files_total = file_count;
			bytes_done = 0L;
			bytes_total = total_bytes;
			error = "";
			http_status = 0L;
			ResetConnect();
		}
	}

	public void EndBatch(bool ok = true, string message = null)
	{
		lock (_lock)
		{
			_in_batch = false;
			_active = Math.Max(0, _active - 1);
			if (ok)
			{
				phase = "done";
				detail = (string.IsNullOrEmpty(message) ? "下载完成" : message);
				if (bytes_total != 0L)
				{
					bytes_done = bytes_total;
				}
				if (files_total != 0L)
				{
					files_done = files_total;
				}
			}
			else
			{
				phase = "fail";
				detail = (string.IsNullOrEmpty(message) ? "下载失败" : message);
			}
		}
	}

	public void StartFile(string key, string filename, string url, long? size = null)
	{
		string text = "";
		try
		{
			text = new Uri(url ?? "").Host ?? "";
		}
		catch (Exception)
		{
			text = "";
		}
		long valueOrDefault = size.GetValueOrDefault();
		lock (_lock)
		{
			if (!_in_batch)
			{
				if (_active == 0)
				{
					_inflight.Clear();
					_file_expected.Clear();
					_completed_bytes = 0L;
					_speed.Reset();
					files_done = 0L;
					files_total = 1L;
					bytes_done = 0L;
					bytes_total = valueOrDefault;
				}
				_active++;
			}
			_inflight[key] = 0L;
			if (valueOrDefault != 0L)
			{
				_file_expected[key] = valueOrDefault;
			}
			this.filename = filename ?? "";
			this.url = url ?? "";
			host = text;
			http_status = 0L;
			error = "";
			ResetConnect();
			phase = "prepare";
			detail = ((this.filename.Length > 0) ? ("准备下载 " + this.filename) : "准备下载");
			if (valueOrDefault != 0L && !_in_batch)
			{
				bytes_total = Math.Max(bytes_total, valueOrDefault);
			}
			RecomputeLocked();
		}
	}

	public void FinishFile(string key, long? size = null, bool skipped = false)
	{
		lock (_lock)
		{
			if (_inflight.TryGetValue(key, out var value))
			{
				_inflight.Remove(key);
			}
			else
			{
				value = 0L;
			}
			long num = ((size.GetValueOrDefault() != 0L) ? size.Value : value);
			_completed_bytes += num;
			files_done++;
			if (!_in_batch)
			{
				_active = Math.Max(0, _active - 1);
			}
			if (skipped)
			{
				phase = "skip";
				detail = "已存在，跳过 " + filename;
			}
			else if (_active == 0 && !_in_batch)
			{
				phase = "done";
				detail = "下载完成 " + filename;
			}
			RecomputeLocked();
		}
	}

	public void FailFile(string key, Exception err)
	{
		lock (_lock)
		{
			_inflight.Remove(key);
			files_done++;
			if (!_in_batch)
			{
				_active = Math.Max(0, _active - 1);
			}
			phase = "fail";
			error = Py.ExcStr(err);
			detail = "下载失败: " + error;
			RecomputeLocked();
		}
	}

	public void Dns(string h)
	{
		if (!Watching())
		{
			return;
		}
		lock (_lock)
		{
			MarkConnect();
			host = (string.IsNullOrEmpty(h) ? host : h);
			phase = "dns";
			detail = "正在解析 DNS " + h;
			handshake = detail;
		}
	}

	public void DnsOk(string h, string ipv, long? ms = null)
	{
		if (!Watching())
		{
			return;
		}
		lock (_lock)
		{
			host = (string.IsNullOrEmpty(h) ? host : h);
			ip = ipv ?? "";
			phase = "dns_ok";
			string[] array = new string[5] { "DNS 解析成功 ", h, " → ", ipv, null };
			object obj;
			if (!ms.HasValue)
			{
				obj = "";
			}
			else
			{
				long? num = ms;
				long? num2 = num;
				obj = "（" + num2 + " ms）";
			}
			array[4] = (string)obj;
			detail = string.Concat(array);
			handshake = detail;
		}
	}

	public void DnsFail(object err)
	{
		if (!Watching())
		{
			return;
		}
		lock (_lock)
		{
			phase = "dns_fail";
			error = Py.Str(err);
			detail = "DNS 解析失败: " + error;
			handshake = detail;
		}
	}

	public void Tcp(string ipv, long p)
	{
		if (!Watching())
		{
			return;
		}
		lock (_lock)
		{
			ip = (string.IsNullOrEmpty(ipv) ? ip : ipv);
			port = p;
			phase = "tcp";
			detail = "正在 TCP 握手 " + ipv + ":" + p;
			handshake = detail;
		}
	}

	public void TcpOk(string ipv, long p, long? ms = null)
	{
		if (!Watching())
		{
			return;
		}
		lock (_lock)
		{
			ip = (string.IsNullOrEmpty(ipv) ? ip : ipv);
			port = p;
			phase = "tcp_ok";
			string[] array = new string[5]
			{
				"TCP 握手成功 ",
				ipv,
				":",
				p.ToString(),
				null
			};
			object obj;
			if (!ms.HasValue)
			{
				obj = "";
			}
			else
			{
				long? num = ms;
				long? num2 = num;
				obj = "（" + num2 + " ms）";
			}
			array[4] = (string)obj;
			detail = string.Concat(array);
			handshake = detail;
		}
	}

	public void TcpFail(object err)
	{
		if (!Watching())
		{
			return;
		}
		lock (_lock)
		{
			phase = "tcp_fail";
			error = Py.Str(err);
			detail = "TCP 握手失败: " + error;
			handshake = detail;
		}
	}

	public void TlsBegin(string h)
	{
		if (!Watching())
		{
			return;
		}
		lock (_lock)
		{
			phase = "tls";
			detail = "正在 TLS 握手 " + h;
		}
	}

	public void TlsOk(string version = "")
	{
		if (!Watching())
		{
			return;
		}
		lock (_lock)
		{
			string text = Py.Strip(version ?? "");
			if (text.ToUpperInvariant().StartsWith("TLS"))
			{
				tls_version = text;
			}
			else if (text.Length > 0)
			{
				tls_version = "TLS " + text;
			}
			else
			{
				tls_version = "TLS 握手成功";
			}
			phase = "tls_ok";
			detail = tls_version;
		}
	}

	public void TlsFail(object err)
	{
		if (!Watching())
		{
			return;
		}
		lock (_lock)
		{
			phase = "tls_fail";
			error = Py.Str(err);
			detail = "TLS 握手失败: " + error;
		}
	}

	public void Reuse()
	{
		if (!Watching())
		{
			return;
		}
		lock (_lock)
		{
			phase = "reuse";
			detail = "复用已有 TCP 连接";
			if (string.IsNullOrEmpty(handshake))
			{
				handshake = "复用已有 TCP 连接";
			}
		}
	}

	private void NoteExpectedLocked(string key, long exp)
	{
		if (exp <= 0)
		{
			return;
		}
		long value = 0L;
		if (key != null)
		{
			_file_expected.TryGetValue(key, out value);
		}
		if (key != null && exp > value)
		{
			if (_in_batch)
			{
				bytes_total += exp - value;
			}
			_file_expected[key] = exp;
		}
		if (!_in_batch)
		{
			bytes_total = Math.Max(bytes_total, exp);
		}
		else if (bytes_total <= 0)
		{
			bytes_total = exp;
		}
	}

	public void HttpOk(long status, long content_length = 0L, string key = null)
	{
		if (!Watching())
		{
			return;
		}
		lock (_lock)
		{
			http_status = status;
			if (content_length != 0L)
			{
				NoteExpectedLocked(key, content_length);
			}
			phase = "http_ok";
			detail = "已收到 HTTP " + http_status;
		}
	}

	public void Transfer(string key, long got, long expected = 0L)
	{
		if (!Watching())
		{
			return;
		}
		lock (_lock)
		{
			_inflight.TryGetValue(key, out var value);
			_inflight[key] = got;
			long num = got - value;
			if (num > 0)
			{
				_speed.Add(num);
			}
			if (expected != 0L)
			{
				NoteExpectedLocked(key, expected);
			}
			phase = "transfer";
			int count = _inflight.Count;
			if (count > 1)
			{
				detail = "下载中（" + count + " 个并发）" + filename;
			}
			else
			{
				detail = ((filename.Length > 0) ? ("下载中 " + filename) : "下载中");
			}
			RecomputeLocked();
		}
	}

	public void Verify(string fname = "")
	{
		if (!Watching())
		{
			return;
		}
		lock (_lock)
		{
			phase = "verify";
			detail = "正在校验 " + (string.IsNullOrEmpty(fname) ? filename : fname);
		}
	}

	public DownloadSnapshot Snapshot()
	{
		lock (_lock)
		{
			return new DownloadSnapshot
			{
				phase = phase,
				phase_label = Py.S(DownloadStatus.PHASE_LABELS[phase] ?? phase),
				detail = detail,
				filename = filename,
				host = host,
				ip = ip,
				port = port,
				http_status = http_status,
				tls_version = tls_version,
				handshake = handshake,
				bytes_done = bytes_done,
				bytes_total = bytes_total,
				files_done = files_done,
				files_total = files_total,
				speed_bps = _speed.Speed(),
				error = error,
				active = (_active > 0)
			};
		}
	}

	private void RecomputeLocked()
	{
		long num = 0L;
		foreach (long value in _inflight.Values)
		{
			num += value;
		}
		bytes_done = _completed_bytes + num;
	}
}
