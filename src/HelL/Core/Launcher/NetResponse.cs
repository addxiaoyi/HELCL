using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HelL.Core.Launcher;

public sealed class NetResponse : IDisposable
{
	private readonly HttpResponseMessage _r;

	private readonly double _readTimeout;

	private readonly CancellationToken _token;

	private Stream _stream;

	private byte[] _content;

	public string Url { get; }
    public double ResponseMilliseconds { get; set; }
    public string MirrorNetwork { get; set; }

	public long StatusCode => (long)_r.StatusCode;

	public string Reason => _r.ReasonPhrase ?? "";

	public HttpResponseMessage Raw => _r;

	public bool IsRedirect
	{
		get
		{
			long statusCode = StatusCode;
			if (statusCode == 301 || statusCode == 302 || statusCode == 303 || statusCode == 307 || statusCode == 308)
			{
				return Header("Location") != null;
			}
			return false;
		}
	}

	internal NetResponse(HttpResponseMessage r, string url, double readTimeout, CancellationToken token)
	{
		_r = r;
		Url = ((r.RequestMessage?.RequestUri != null && Net.ReplayBase.Length == 0) ? r.RequestMessage.RequestUri.ToString() : url);
		_readTimeout = readTimeout;
		_token = token;
	}

	public string Header(string name)
	{
		if (_r.Headers.TryGetValues(name, out var values))
		{
			return string.Join(", ", values);
		}
		if (_r.Content != null && _r.Content.Headers.TryGetValues(name, out values))
		{
			return string.Join(", ", values);
		}
		return null;
	}

	public bool HasHeader(string name)
	{
		return Header(name) != null;
	}

	public void RaiseForStatus()
	{
		long statusCode = StatusCode;
		string text = ((statusCode >= 400 && statusCode < 500) ? "Client Error" : ((statusCode >= 500 && statusCode < 600) ? "Server Error" : null));
		if (text != null)
		{
			throw new HTTPError(statusCode + " " + text + ": " + Reason + " for url: " + Url, statusCode);
		}
	}

	public int Read(byte[] buf, int off, int count)
	{
		if (_stream == null)
		{
			_stream = _r.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
		}
		CancellationTokenSource cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(_token);
		try
		{
			Task<int> task = _stream.ReadAsync(buf, off, count, cancellationTokenSource.Token);
			if (!task.Wait(TimeSpan.FromSeconds(Math.Max(0.5, _readTimeout))))
			{
				cancellationTokenSource.Cancel();
				try
				{
					_r.Dispose();
				}
				catch
				{
				}
				Uri uri = new Uri(Url);
				throw new NetError("ConnectionError", ((uri.Scheme == "https") ? "HTTPSConnectionPool" : "HTTPConnectionPool") + "(host=" + Py.ReprStr(uri.Host) + ", port=" + uri.Port + "): Read timed out.");
			}
			return task.Result;
		}
		catch (AggregateException ex)
		{
			Exception ex2 = ex.InnerException ?? ex;
			if (ex2 is OperationCanceledException && _token.IsCancellationRequested)
			{
				throw new OperationCanceledException(_token);
			}
			throw new NetError("ChunkedEncodingError", "('Connection broken: " + ex2.GetType().Name + "(" + Py.ReprStr(ex2.Message) + ")', " + Py.ReprStr(ex2.Message) + ")", ex2);
		}
		finally
		{
			cancellationTokenSource.Dispose();
		}
	}

	public IEnumerable<byte[]> IterContent(int chunkSize = 65536)
	{
		byte[] buf = new byte[chunkSize];
		while (true)
		{
			int num = Read(buf, 0, chunkSize);
			if (num > 0)
			{
				byte[] array = new byte[num];
				Buffer.BlockCopy(buf, 0, array, 0, num);
				yield return array;
				continue;
			}
			break;
		}
	}

	public byte[] Content()
	{
		if (_content != null)
		{
			return _content;
		}
		MemoryStream memoryStream = new MemoryStream();
		byte[] array = new byte[65536];
		int count;
		while ((count = Read(array, 0, array.Length)) > 0)
		{
			memoryStream.Write(array, 0, count);
		}
		return _content = memoryStream.ToArray();
	}

	public string Encoding()
	{
		string text = Header("Content-Type");
		if (string.IsNullOrEmpty(text))
		{
			return null;
		}
		string[] array = text.Split(';');
		for (int i = 0; i < array.Length; i++)
		{
			string text2 = array[i].Trim();
			if (text2.StartsWith("charset=", StringComparison.OrdinalIgnoreCase))
			{
				return text2.Substring(8).Trim('"', '\'', ' ');
			}
		}
		if (text.Trim().StartsWith("text", StringComparison.OrdinalIgnoreCase))
		{
			return "ISO-8859-1";
		}
		if (text.IndexOf("application/json", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return "utf-8";
		}
		return null;
	}

	public string Text()
	{
		byte[] array = Content();
		string text = Encoding();
		Encoding encoding;
		try
		{
			encoding = ((text == null) ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false) : System.Text.Encoding.GetEncoding(text));
		}
		catch
		{
			encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
		}
		if (text == null && array.Length >= 3 && array[0] == 239 && array[1] == 187 && array[2] == 191)
		{
			return encoding.GetString(array, 3, array.Length - 3);
		}
		return encoding.GetString(array);
	}

	public object Json()
	{
		byte[] array = Content();
		string text = Encoding();
		string s;
		if (text == null || text.Equals("utf-8", StringComparison.OrdinalIgnoreCase) || text.Equals("utf8", StringComparison.OrdinalIgnoreCase))
		{
			s = Utils.DecodeUtf8Sig(array);
		}
		else
		{
			try
			{
				s = System.Text.Encoding.GetEncoding(text).GetString(array);
			}
			catch
			{
				s = Utils.DecodeUtf8Sig(array);
			}
		}
		return PyJson.Loads(s);
	}

	public void Dispose()
	{
		try
		{
			_stream?.Dispose();
		}
		catch
		{
		}
		try
		{
			_r.Dispose();
		}
		catch
		{
		}
	}
}
