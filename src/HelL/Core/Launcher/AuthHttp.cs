using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace HelL.Core.Launcher;

public static class AuthHttp
{
	private static HttpClient _proxied;

	private static HttpClient _direct;

	private static readonly object _gate = new object();

	private static bool ForceDirect()
	{
		try
		{
			return !Py.Truthy(Config.CONFIG.Get("use_system_proxy", true));
		}
		catch
		{
			return false;
		}
	}

	private static HttpClient Client()
	{
		bool flag = ForceDirect();
		lock (_gate)
		{
			if (flag && _direct != null)
			{
				return _direct;
			}
			if (!flag && _proxied != null)
			{
				return _proxied;
			}
			HttpClient httpClient = new HttpClient(new HttpClientHandler
			{
				AllowAutoRedirect = true,
				AutomaticDecompression = (DecompressionMethods.GZip | DecompressionMethods.Deflate),
				UseProxy = !flag
			})
			{
				Timeout = Timeout.InfiniteTimeSpan
			};
			if (flag)
			{
				_direct = httpClient;
			}
			else
			{
				_proxied = httpClient;
			}
			return httpClient;
		}
	}

	private static readonly Dictionary<string, DateTime> _warmed = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

	/// <summary>后台预热：提前完成代理探测、DNS、TLS 握手，让真正的登录请求走现成连接。失败静默。</summary>
	public static void Warmup(params string[] urls)
	{
		if (ReplayBase().Length > 0 || Environment.GetEnvironmentVariable("TIPCL_NO_WARMUP") == "1")
		{
			return;
		}
		foreach (string url in urls)
		{
			lock (_warmed)
			{
				if (_warmed.TryGetValue(url, out var last) && (DateTime.UtcNow - last).TotalSeconds < 60.0)
				{
					continue;
				}
				_warmed[url] = DateTime.UtcNow;
			}
			string u = url;
			ThreadPool.QueueUserWorkItem(delegate
			{
				try
				{
					using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(8.0));
					using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Head, u);
					req.Headers.TryAddWithoutValidation("User-Agent", "HelL/1.0");
					using HttpResponseMessage resp = Client().SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token).GetAwaiter().GetResult();
				}
				catch
				{
				}
			});
		}
	}

	private static string ReplayBase()
	{
		return (Environment.GetEnvironmentVariable("TIPCL_HTTP_REPLAY") ?? "").Trim();
	}

	public static AuthResponse Send(string method, string url, PyDict form = null, object json = null, bool hasJson = false, IDictionary<string, string> headers = null, double timeout = 20.0, string userAgent = null, CancellationToken token = default(CancellationToken))
	{
		string requestUri = url;
		string text = ReplayBase();
		if (text.Length > 0)
		{
			requestUri = text.TrimEnd('/');
		}
		using HttpRequestMessage httpRequestMessage = new HttpRequestMessage(new HttpMethod(method), requestUri);
		if (text.Length > 0)
		{
			httpRequestMessage.Headers.TryAddWithoutValidation("X-HelL-Replay-Url", url);
		}
		httpRequestMessage.Headers.TryAddWithoutValidation("User-Agent", userAgent ?? "python-requests/2.32.3");
		httpRequestMessage.Headers.TryAddWithoutValidation("Accept", "*/*");
		if (headers != null)
		{
			foreach (KeyValuePair<string, string> header in headers)
			{
				httpRequestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value);
			}
		}
		if (form != null)
		{
			List<KeyValuePair<string, string>> list = new List<KeyValuePair<string, string>>();
			foreach (KeyValuePair<string, object> item in form)
			{
				list.Add(new KeyValuePair<string, string>(item.Key, Py.Str(item.Value)));
			}
			httpRequestMessage.Content = new FormUrlEncodedContent(list);
		}
		else if (hasJson)
		{
			ByteArrayContent byteArrayContent = new ByteArrayContent(Encoding.UTF8.GetBytes(PyJson.Dumps(json)));
			byteArrayContent.Headers.TryAddWithoutValidation("Content-Type", "application/json");
			httpRequestMessage.Content = byteArrayContent;
		}
		using CancellationTokenSource cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(token);
		cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(Math.Max(0.1, timeout)));
		try
		{
			using HttpResponseMessage httpResponseMessage = Client().SendAsync(httpRequestMessage, HttpCompletionOption.ResponseContentRead, cancellationTokenSource.Token).GetAwaiter().GetResult();
			byte[] result = httpResponseMessage.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
			return new AuthResponse
			{
				StatusCode = (int)httpResponseMessage.StatusCode,
				RetryAfterSeconds = httpResponseMessage.Headers.RetryAfter?.Delta?.TotalSeconds
					?? (httpResponseMessage.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow)?.TotalSeconds,
				Content = result
			};
		}
		catch (OperationCanceledException) when (token.IsCancellationRequested)
		{
			throw;
		}
		catch (OperationCanceledException inner)
		{
			throw new AuthHttpException("Read timed out. (read timeout=" + Py.Str(timeout) + ")", inner);
		}
		catch (HttpRequestException ex2)
		{
			throw new AuthHttpException(Py.ExcStr(ex2.InnerException ?? ex2), ex2);
		}
		catch (IOException ex3)
		{
			throw new AuthHttpException(ex3.Message, ex3);
		}
		catch (SocketException ex4)
		{
			throw new AuthHttpException(ex4.Message, ex4);
		}
	}

	public static AuthResponse PostJson(string url, object payload, double timeout = 20.0, string userAgent = null, CancellationToken token = default(CancellationToken))
	{
		return Send("POST", url, null, payload, hasJson: true, null, timeout, userAgent, token);
	}

	public static object FetchJson(string url, double timeout = 20.0)
	{
		AuthResponse authResponse = Send("GET", url, null, null, hasJson: false, null, timeout);
		if (authResponse.StatusCode >= 400)
		{
			throw new AuthHttpException($"{authResponse.StatusCode} Client Error for url: {url}");
		}
		return authResponse.Json();
	}

	public static void Download(string url, string dest, double timeout = 60.0, CancellationToken token = default(CancellationToken))
	{
		AuthResponse authResponse = Send("GET", url, null, null, hasJson: false, null, timeout, null, token);
		if (authResponse.StatusCode >= 400)
		{
			throw new AuthHttpException($"{authResponse.StatusCode} Client Error for url: {url}");
		}
		string text = PPath.Abs(dest);
		string directoryName = Path.GetDirectoryName(text);
		if (!string.IsNullOrEmpty(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		string text2 = text + ".part";
		File.WriteAllBytes(text2, authResponse.Content);
		Utils.OsReplace(text2, text);
	}
}
