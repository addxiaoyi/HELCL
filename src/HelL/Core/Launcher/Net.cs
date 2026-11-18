using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading;

namespace HelL.Core.Launcher;

public static class Net
{
	public sealed class RequestOptions
	{
		public string Method = "GET";

		public IDictionary<string, string> Headers;

		public double ConnectTimeout = 8.0;

		public double ReadTimeout = 90.0;

		public bool AllowRedirects = true;

		public int Retries;

		public byte[] Body;

		public string ContentType;

		public CancellationToken Token;

		public DownloadTracker Tracker;
	}

	public static readonly string[] _PROXY_KEYS;

	public static readonly string USER_AGENT;

	public const string SHORT_USER_AGENT = "HelL/1.0";

	private static volatile bool _direct;

	private static readonly object _gate;

	private static HttpClient _httpProxy;

	private static HttpClient _httpDirect;

	private static HttpClient _rawProxy;

	private static HttpClient _rawDirect;

	public static string ReplayBase => (Environment.GetEnvironmentVariable("TIPCL_HTTP_REPLAY") ?? "").Trim();

	public static HttpClient Http => Client(raw: false);

	public static HttpClient HttpRaw => Client(raw: true);

	static Net()
	{
		_PROXY_KEYS = new string[8] { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "SOCKS_PROXY", "http_proxy", "https_proxy", "all_proxy", "socks_proxy" };
		USER_AGENT = AppInfo.CLIENT_HEADER + " (.NET; +minecraft launcher)";
		_gate = new object();
		try
		{
			ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;
		}
		catch (NotSupportedException)
		{
			ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
		}
		if (ServicePointManager.DefaultConnectionLimit < 64)
		{
			ServicePointManager.DefaultConnectionLimit = 64;
		}
		ServicePointManager.Expect100Continue = false;
		try
		{
			ApplyProxyPolicy();
		}
		catch
		{
		}
	}

	public static bool UseSystemProxy()
	{
		try
		{
			return Py.Truthy(Config.CONFIG.Get("use_system_proxy", true));
		}
		catch
		{
			return true;
		}
	}

	public static bool ForceDirect()
	{
		if (UseSystemProxy())
		{
			return _direct;
		}
		return true;
	}

	public static void ApplyProxyPolicy()
	{
		if (UseSystemProxy())
		{
			_direct = false;
		}
		else
		{
			InstallDirect();
		}
	}

	private static void InstallDirect()
	{
		// UseProxy=false isolates this client; changing process variables would break switching back.
		_direct = true;
	}

	private static HttpClient Client(bool raw)
	{
		bool flag = ForceDirect();
		lock (_gate)
		{
			ref HttpClient reference = ref raw ? ref flag ? ref _rawDirect : ref _rawProxy : ref flag ? ref _httpDirect : ref _httpProxy;
			if (reference == null)
			{
				reference = Build(flag, raw);
			}
			return reference;
		}
	}

	private static HttpClient Build(bool direct, bool raw)
	{
		HttpClientHandler httpClientHandler = new HttpClientHandler();
		httpClientHandler.UseProxy = !direct;
		httpClientHandler.AllowAutoRedirect = !raw;
		if (!raw)
		{
			httpClientHandler.MaxAutomaticRedirections = 30;
		}
		httpClientHandler.AutomaticDecompression = ((!raw) ? (DecompressionMethods.GZip | DecompressionMethods.Deflate) : DecompressionMethods.None);
		httpClientHandler.UseCookies = true;
		return new HttpClient(httpClientHandler, disposeHandler: true)
		{
			Timeout = Timeout.InfiniteTimeSpan
		};
	}

	public static void ResetClients()
	{
		lock (_gate)
		{
			_httpProxy = (_httpDirect = (_rawProxy = (_rawDirect = null)));
		}
	}

	public static NetResponse Request(string url, RequestOptions o)
	{
		o = o ?? new RequestOptions();
		int num = 0;
		NetResponse netResponse;
		while (true)
		{
			try
			{
				netResponse = SendOnce(url, o);
			}
			catch (NetError netError) when (num < o.Retries && o.Method == "GET" && netError.PyType != "ReadTimeout")
			{
				num++;
				continue;
			}
			catch (NetError) when (num < o.Retries && o.Method == "GET")
			{
				num++;
				continue;
			}
			long statusCode = netResponse.StatusCode;
			if (num >= o.Retries || !(o.Method == "GET") || (statusCode != 429 && statusCode != 500 && statusCode != 502 && statusCode != 503 && statusCode != 504))
			{
				break;
			}
			num++;
			double num2 = 0.0;
			if ((statusCode == 429 || statusCode == 503) && double.TryParse(netResponse.Header("Retry-After"), NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
			{
				num2 = Math.Min(Math.Max(0.0, result), 10.0);
			}
			netResponse.Dispose();
			if (num2 > 0.0)
			{
				Sleep(num2, o.Token);
			}
		}
		return netResponse;
	}

	private static void Sleep(double seconds, CancellationToken tok)
	{
		if (tok.CanBeCanceled)
		{
			tok.WaitHandle.WaitOne(TimeSpan.FromSeconds(seconds));
		}
		else
		{
			Thread.Sleep(TimeSpan.FromSeconds(seconds));
		}
		tok.ThrowIfCancellationRequested();
	}

	private static NetResponse SendOnce(string url, RequestOptions o)
	{
		string uriString = url;
		HttpRequestMessage httpRequestMessage = new HttpRequestMessage(new HttpMethod(o.Method), (Uri)null);
		string replayBase = ReplayBase;
		if (replayBase.Length > 0)
		{
			uriString = replayBase.TrimEnd('/');
			httpRequestMessage.Headers.TryAddWithoutValidation("X-HelL-Replay-Url", url);
		}
		if (!Uri.TryCreate(uriString, UriKind.Absolute, out var result) || (result.Scheme != "http" && result.Scheme != "https"))
		{
			throw new NetError("MissingSchema", Py.Fmt("Invalid URL {0}: No scheme supplied. Perhaps you meant https://{0}?", Py.ReprStr(url)));
		}
		httpRequestMessage.RequestUri = result;
		bool flag = false;
		if (o.Headers != null)
		{
			foreach (KeyValuePair<string, string> header in o.Headers)
			{
				if (header.Value == null)
				{
					continue;
				}
				if (string.Equals(header.Key, "User-Agent", StringComparison.OrdinalIgnoreCase))
				{
					flag = true;
				}
				if (!httpRequestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value))
				{
					if (httpRequestMessage.Content == null)
					{
						httpRequestMessage.Content = new ByteArrayContent(o.Body ?? new byte[0]);
					}
					httpRequestMessage.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
				}
			}
		}
		if (!flag)
		{
			httpRequestMessage.Headers.TryAddWithoutValidation("User-Agent", USER_AGENT);
		}
		if (o.Body != null)
		{
			if (httpRequestMessage.Content == null)
			{
				httpRequestMessage.Content = new ByteArrayContent(o.Body);
			}
			if (o.ContentType != null)
			{
				httpRequestMessage.Content.Headers.TryAddWithoutValidation("Content-Type", o.ContentType);
			}
		}
		HttpClient httpClient = (o.AllowRedirects ? Http : HttpRaw);
		CancellationTokenSource cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(o.Token);
		cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(Math.Max(0.5, o.ConnectTimeout + o.ReadTimeout)));
		HttpResponseMessage result2;
		try
		{
			result2 = httpClient.SendAsync(httpRequestMessage, HttpCompletionOption.ResponseHeadersRead, cancellationTokenSource.Token).GetAwaiter().GetResult();
		}
		catch (OperationCanceledException) when (o.Token.IsCancellationRequested)
		{
			cancellationTokenSource.Dispose();
			throw;
		}
		catch (Exception e)
		{
			cancellationTokenSource.Dispose();
			throw Classify(e, result, o, cancellationTokenSource.IsCancellationRequested);
		}
		cancellationTokenSource.Dispose();
		return new NetResponse(result2, url, o.ReadTimeout, o.Token);
	}

	private static string PoolName(Uri u)
	{
		return ((u.Scheme == "https") ? "HTTPSConnectionPool" : "HTTPConnectionPool") + "(host=" + Py.ReprStr(u.Host) + ", port=" + u.Port + ")";
	}

	internal static NetError Classify(Exception e, Uri u, RequestOptions o, bool ourTimeout)
	{
		string text = PoolName(u);
		string pathAndQuery = u.PathAndQuery;
		if (ourTimeout && e is OperationCanceledException)
		{
			return new NetError("ReadTimeout", text + ": Read timed out. (read timeout=" + Py.Str(o.ReadTimeout) + ")", e);
		}
		if (e is OperationCanceledException)
		{
			return new NetError("ConnectTimeout", text + ": Max retries exceeded with url: " + pathAndQuery + " (Caused by ConnectTimeoutError('Connection to " + u.Host + " timed out. (connect timeout=" + Py.Str(o.ConnectTimeout) + ")'))", e);
		}
		Exception ex = e;
		SocketException ex2 = null;
		AuthenticationException ex3 = null;
		IOException ex4 = null;
		while (ex != null)
		{
			if (ex is SocketException ex5 && ex2 == null)
			{
				ex2 = ex5;
			}
			if (ex is AuthenticationException ex6 && ex3 == null)
			{
				ex3 = ex6;
			}
			if (ex is IOException ex7 && ex4 == null)
			{
				ex4 = ex7;
			}
			ex = ex.InnerException;
		}
		string message = (ex2 ?? ex3 ?? ex4 ?? e).Message;
		if (ex3 != null)
		{
			return new NetError("SSLError", text + ": Max retries exceeded with url: " + pathAndQuery + " (Caused by SSLError(SSLError(" + Py.ReprStr(message) + ")))", e);
		}
		if (ex2 != null)
		{
			string text2 = ((ex2.SocketErrorCode == SocketError.ConnectionRefused) ? "[Errno 111] Connection refused" : ((ex2.SocketErrorCode == SocketError.HostNotFound || ex2.SocketErrorCode == SocketError.NoData || ex2.SocketErrorCode == SocketError.TryAgain) ? "[Errno -2] Name or service not known" : ((ex2.SocketErrorCode == SocketError.TimedOut) ? "timed out" : message)));
			if (ex2.SocketErrorCode == SocketError.TimedOut)
			{
				return new NetError("ConnectTimeout", text + ": Max retries exceeded with url: " + pathAndQuery + " (Caused by ConnectTimeoutError('Connection to " + u.Host + " timed out.'))", e);
			}
			return new NetError("ConnectionError", text + ": Max retries exceeded with url: " + pathAndQuery + " (Caused by NewConnectionError('Failed to establish a new connection: " + text2 + "'))", e);
		}
		if (message.IndexOf("proxy", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return new NetError("ProxyError", text + ": Max retries exceeded with url: " + pathAndQuery + " (Caused by ProxyError(" + Py.ReprStr(message) + "))", e);
		}
		return new NetError("ConnectionError", "('Connection aborted.', " + Py.ReprStr(message) + ")", e);
	}

	public static NetResponse Get(string url, double connect = 8.0, double read = 90.0, IDictionary<string, string> headers = null, bool allowRedirects = true, int retries = 0, CancellationToken token = default(CancellationToken))
	{
		return Request(url, new RequestOptions
		{
			Headers = headers,
			ConnectTimeout = connect,
			ReadTimeout = read,
			AllowRedirects = allowRedirects,
			Retries = retries,
			Token = token
		});
	}

	public static object GetJson(string url, double connect = 4.0, double read = 15.0, IDictionary<string, string> headers = null, CancellationToken token = default(CancellationToken))
	{
		using NetResponse netResponse = Get(url, connect, read, headers, allowRedirects: true, 0, token);
		netResponse.RaiseForStatus();
		return netResponse.Json();
	}

	public static string GetText(string url, double connect = 4.0, double read = 15.0, IDictionary<string, string> headers = null, CancellationToken token = default(CancellationToken))
	{
		using NetResponse netResponse = Get(url, connect, read, headers, allowRedirects: true, 0, token);
		netResponse.RaiseForStatus();
		return netResponse.Text();
	}
}
