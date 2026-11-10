using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using HelL.Core.Launcher;

namespace HelL.Core;

public sealed class BridgeServer : IDisposable
{
	private sealed class Request
	{
		public string Method;

		public string Target;

		public string Version;

		public string Path;

		public string Query;

		public List<KeyValuePair<string, string>> Headers = new List<KeyValuePair<string, string>>();

		public bool CloseConnection;

		public string Header(string name)
		{
			foreach (KeyValuePair<string, string> header in Headers)
			{
				if (string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase))
				{
					return header.Value;
				}
			}
			return null;
		}
	}

	private sealed class Conn
	{
		public TcpClient Client;

		public Stream Stream;

		public BufferedStream In;

		public IPEndPoint Remote;
	}

	public const string TOKEN_HEADER = "X-HelL-Bridge-Token";

	public const int MAX_REQUEST_BYTES = 4194304;

	public const string LOOPBACK_HOST = "127.0.0.1";

	public const string ServerVersion = "BaseHTTP/0.6 HelL.Core";

	public static Action<string> Log = delegate(string s)
	{
		try
		{
			Console.Error.WriteLine(s);
		}
		catch
		{
		}
	};

	private TcpListener _listener;

	private Thread _acceptThread;

	private volatile bool _stopping;

	private readonly object _connLock = new object();

	private readonly HashSet<TcpClient> _conns = new HashSet<TcpClient>();

	private readonly byte[] _tokenBytes;

	public BackendApi Api { get; }

	public EventBus Bus { get; }

	public string Token { get; }

	public HashSet<string> AllowedOrigins { get; }

	public RpcDispatcher Dispatcher { get; }

	public int Port { get; private set; }

	public event Action<Exception> Crashed;

	public BridgeServer(BackendApi api, EventBus bus, string token = null, IEnumerable<string> allowedOrigins = null)
	{
		token = token ?? TokenUrlSafe(32);
		if (token.Length < 32)
		{
			throw new ValueError("bridge token must contain at least 32 characters");
		}
		Api = api;
		Bus = bus;
		Token = token;
		_tokenBytes = Encoding.UTF8.GetBytes(token);
		AllowedOrigins = new HashSet<string>(StringComparer.Ordinal);
		foreach (string item in allowedOrigins ?? Array.Empty<string>())
		{
			AllowedOrigins.Add(NormalizeOrigin(item));
		}
		Dispatcher = new RpcDispatcher(api);
	}

	public static string TokenUrlSafe(int nbytes)
	{
		byte[] array = new byte[nbytes];
		using (RandomNumberGenerator randomNumberGenerator = RandomNumberGenerator.Create())
		{
			randomNumberGenerator.GetBytes(array);
		}
		return Convert.ToBase64String(array).TrimEnd('=').Replace('+', '-')
			.Replace('/', '_');
	}

	public static string NormalizeOrigin(string origin)
	{
		string m = "allowed origin must be a 127.0.0.1 HTTP origin: " + Py.ReprStr(origin);
		if (!origin.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
		{
			throw new ValueError(m);
		}
		string text = origin.Substring(7);
		int num = text.IndexOfAny(new char[4] { '/', '?', '#', ';' });
		string text2 = ((num >= 0) ? text.Substring(0, num) : text);
		string text3 = ((num >= 0) ? text.Substring(num) : "");
		if (text3 != "" && text3 != "/")
		{
			throw new ValueError(m);
		}
		if (text2.Contains("@"))
		{
			throw new ValueError(m);
		}
		string a = text2;
		string text4 = null;
		int num2 = text2.LastIndexOf(':');
		if (num2 >= 0)
		{
			a = text2.Substring(0, num2);
			text4 = text2.Substring(num2 + 1);
		}
		if (!string.Equals(a, "127.0.0.1", StringComparison.OrdinalIgnoreCase))
		{
			throw new ValueError(m);
		}
		int? num3 = null;
		if (text4 != null && text4.Length > 0)
		{
			if (!int.TryParse(text4, NumberStyles.None, CultureInfo.InvariantCulture, out var result) || result > 65535)
			{
				throw new ValueError("invalid allowed origin port: " + Py.ReprStr(origin));
			}
			if (result < 1)
			{
				throw new ValueError("invalid allowed origin port: " + Py.ReprStr(origin));
			}
			num3 = result;
		}
		return "http://127.0.0.1" + (num3.HasValue ? (":" + num3.Value) : "");
	}

	public bool TokenMatches(string supplied)
	{
		if (string.IsNullOrEmpty(supplied) || _tokenBytes.Length == 0)
		{
			return false;
		}
		return FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), _tokenBytes);
	}

	public static bool FixedTimeEquals(byte[] a, byte[] b)
	{
		int num = a.Length ^ b.Length;
		int num2 = Math.Min(a.Length, b.Length);
		for (int i = 0; i < a.Length; i++)
		{
			num |= a[i] ^ ((i < b.Length) ? b[i] : 0);
		}
		if (num == 0)
		{
			return num2 == b.Length;
		}
		return false;
	}

	public int Start(int port = 0)
	{
		_listener = new TcpListener(IPAddress.Loopback, port);
		_listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, optionValue: true);
		_listener.Start(128);
		Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
		_acceptThread = new Thread(AcceptLoop)
		{
			IsBackground = true,
			Name = "bridge-accept"
		};
		_acceptThread.Start();
		return Port;
	}

	public void Stop()
	{
		_stopping = true;
		try
		{
			_listener?.Stop();
		}
		catch (Exception)
		{
		}
		lock (_connLock)
		{
			foreach (TcpClient conn in _conns)
			{
				try
				{
					conn.Close();
				}
				catch (Exception)
				{
				}
			}
			_conns.Clear();
		}
	}

	public void Dispose()
	{
		Stop();
	}

	private void AcceptLoop()
	{
		try
		{
			while (!_stopping)
			{
				TcpClient client;
				try
				{
					client = _listener.AcceptTcpClient();
				}
				catch (SocketException) when (_stopping)
				{
					break;
				}
				catch (ObjectDisposedException) when (_stopping)
				{
					break;
				}
				catch (SocketException)
				{
					continue;
				}
				if (!(client.Client.RemoteEndPoint is IPEndPoint iPEndPoint) || !IPAddress.IsLoopback(iPEndPoint.Address))
				{
					try
					{
						client.Close();
					}
					catch (Exception)
					{
					}
					continue;
				}
				lock (_connLock)
				{
					_conns.Add(client);
				}
				Thread thread = new Thread((ThreadStart)delegate
				{
					HandleConnection(client);
				});
				thread.IsBackground = true;
				thread.Name = "bridge-conn";
				thread.Start();
			}
		}
		catch (Exception obj)
		{
			if (!_stopping)
			{
				this.Crashed?.Invoke(obj);
			}
		}
	}

	private void HandleConnection(TcpClient client)
	{
		try
		{
			client.NoDelay = true;
			NetworkStream stream = client.GetStream();
			Conn conn = new Conn
			{
				Client = client,
				Stream = stream,
				In = new BufferedStream(stream, 65536),
				Remote = (client.Client.RemoteEndPoint as IPEndPoint)
			};
			while (!_stopping)
			{
				bool eof;
				Request request = ReadRequest(conn, out eof);
				if (!eof && request != null)
				{
					HandleOne(conn, request);
					if (request.CloseConnection)
					{
						break;
					}
					continue;
				}
				break;
			}
		}
		catch (Exception)
		{
		}
		finally
		{
			lock (_connLock)
			{
				_conns.Remove(client);
			}
			try
			{
				client.Close();
			}
			catch (Exception)
			{
			}
		}
	}

	internal static string ReadLine(Stream s, out bool eof, out bool tooLong)
	{
		eof = false;
		tooLong = false;
		MemoryStream memoryStream = new MemoryStream();
		while (true)
		{
			int num = s.ReadByte();
			if (num < 0)
			{
				eof = memoryStream.Length == 0;
				break;
			}
			memoryStream.WriteByte((byte)num);
			if (num == 10)
			{
				break;
			}
			if (memoryStream.Length > 65536)
			{
				tooLong = true;
				return null;
			}
		}
		byte[] array = memoryStream.ToArray();
		int num2 = array.Length;
		while (num2 > 0 && (array[num2 - 1] == 10 || array[num2 - 1] == 13))
		{
			num2--;
		}
		return Latin1(array, 0, num2);
	}

	private static string Latin1(byte[] b, int off, int n)
	{
		char[] array = new char[n];
		for (int i = 0; i < n; i++)
		{
			array[i] = (char)b[off + i];
		}
		return new string(array);
	}

	private Request ReadRequest(Conn conn, out bool eof)
	{
		bool tooLong;
		string text = ReadLine(conn.In, out eof, out tooLong);
		if (eof)
		{
			return null;
		}
		if (tooLong)
		{
			SendError(conn, 414, "Request-URI Too Long", null);
			return null;
		}
		if (text.Length == 0)
		{
			eof = true;
			return null;
		}
		string[] array = text.Split(new char[1] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
		Request request = new Request();
		if (array.Length == 3)
		{
			request.Version = array[2];
			if (!request.Version.StartsWith("HTTP/"))
			{
				SendError(conn, 400, "Bad request version (" + Py.ReprStr(request.Version) + ")", null);
				return null;
			}
			request.CloseConnection = string.CompareOrdinal(request.Version, "HTTP/1.1") < 0;
			if (string.CompareOrdinal(request.Version, "HTTP/2.0") >= 0)
			{
				SendError(conn, 505, "Invalid HTTP version (" + request.Version.Substring(5) + ")", null);
				return null;
			}
			request.Method = array[0];
			request.Target = array[1];
			int num = 0;
			while (true)
			{
				bool eof2;
				bool tooLong2;
				string text2 = ReadLine(conn.In, out eof2, out tooLong2);
				if (tooLong2)
				{
					SendError(conn, 431, "Line too long", null);
					return null;
				}
				if (eof2 || text2.Length == 0)
				{
					break;
				}
				if (++num > 100)
				{
					SendError(conn, 431, "Too many headers", null);
					return null;
				}
				if ((text2[0] == ' ' || text2[0] == '\t') && request.Headers.Count > 0)
				{
					KeyValuePair<string, string> keyValuePair = request.Headers[request.Headers.Count - 1];
					request.Headers[request.Headers.Count - 1] = new KeyValuePair<string, string>(keyValuePair.Key, keyValuePair.Value + " " + text2.Trim());
					continue;
				}
				int num2 = text2.IndexOf(':');
				if (num2 > 0)
				{
					request.Headers.Add(new KeyValuePair<string, string>(text2.Substring(0, num2).Trim(), text2.Substring(num2 + 1).Trim(' ', '\t')));
				}
			}
			string text3 = request.Header("Connection") ?? "";
			if (text3.Equals("close", StringComparison.OrdinalIgnoreCase))
			{
				request.CloseConnection = true;
			}
			else if (text3.Equals("keep-alive", StringComparison.OrdinalIgnoreCase) && string.CompareOrdinal(request.Version, "HTTP/1.1") >= 0)
			{
				request.CloseConnection = false;
			}
			if ((request.Header("Expect") ?? "").Equals("100-continue", StringComparison.OrdinalIgnoreCase) && string.CompareOrdinal(request.Version, "HTTP/1.1") >= 0)
			{
				byte[] bytes = Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n");
				conn.Stream.Write(bytes, 0, bytes.Length);
				conn.Stream.Flush();
			}
			SplitTarget(request.Target, out request.Path, out request.Query);
			return request;
		}
		if (array.Length == 2)
		{
			request.Version = "HTTP/0.9";
			request.CloseConnection = true;
			SendError(conn, 400, "Bad HTTP/0.9 request type (" + Py.ReprStr(array[0]) + ")", null);
			return null;
		}
		SendError(conn, 400, "Bad request syntax (" + Py.ReprStr(text) + ")", null);
		return null;
	}

	internal static void SplitTarget(string target, out string path, out string query)
	{
		string text = target;
		int num = text.IndexOf('#');
		if (num >= 0)
		{
			text = text.Substring(0, num);
		}
		int num2 = text.IndexOf('?');
		query = ((num2 >= 0) ? text.Substring(num2 + 1) : "");
		text = ((num2 >= 0) ? text.Substring(0, num2) : text);
		if (text.StartsWith("//"))
		{
			int num3 = text.IndexOf('/', 2);
			text = ((num3 >= 0) ? text.Substring(num3) : "");
		}
		else
		{
			int num4 = text.IndexOf(':');
			int num5 = text.IndexOf('/');
			if (num4 > 0 && (num5 < 0 || num4 < num5) && IsScheme(text.Substring(0, num4)))
			{
				text = text.Substring(num4 + 1);
				if (text.StartsWith("//"))
				{
					int num6 = text.IndexOf('/', 2);
					text = ((num6 >= 0) ? text.Substring(num6) : "");
				}
			}
		}
		path = text;
	}

	private static bool IsScheme(string s)
	{
		if (s.Length == 0 || !char.IsLetter(s[0]))
		{
			return false;
		}
		foreach (char c in s)
		{
			if (!char.IsLetterOrDigit(c) && c != '+' && c != '-' && c != '.')
			{
				return false;
			}
		}
		return true;
	}

	internal static string Phrase(int code)
	{
		return code switch
		{
			100 => "Continue",
			200 => "OK",
			204 => "No Content",
			400 => "Bad Request",
			401 => "Unauthorized",
			403 => "Forbidden",
			404 => "Not Found",
			413 => "Request Entity Too Large",
			414 => "Request-URI Too Long",
			431 => "Request Header Fields Too Large",
			501 => "Not Implemented",
			505 => "HTTP Version Not Supported",
			_ => "",
		};
	}

	private static string Explain(int code)
	{
		return code switch
		{
			400 => "Bad request syntax or unsupported method",
			413 => "Entity is too large",
			414 => "URI is too long",
			431 => "The server is unwilling to process the request because its header fields are too large",
			501 => "Server does not support this operation",
			505 => "Cannot fulfill request",
			_ => "",
		};
	}

	private void WriteHead(Conn conn, Request req, int code, List<KeyValuePair<string, string>> headers)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append("HTTP/1.1 ").Append(code).Append(' ')
			.Append(Phrase(code))
			.Append("\r\n");
		stringBuilder.Append("Server: ").Append("BaseHTTP/0.6 HelL.Core").Append("\r\n");
		stringBuilder.Append("Date: ").Append(DateTime.UtcNow.ToString("r", CultureInfo.InvariantCulture)).Append("\r\n");
		foreach (KeyValuePair<string, string> header in headers)
		{
			stringBuilder.Append(header.Key).Append(": ").Append(header.Value)
				.Append("\r\n");
			if (req != null && header.Key.Equals("Connection", StringComparison.OrdinalIgnoreCase))
			{
				if (header.Value.Equals("close", StringComparison.OrdinalIgnoreCase))
				{
					req.CloseConnection = true;
				}
				else if (header.Value.Equals("keep-alive", StringComparison.OrdinalIgnoreCase))
				{
					req.CloseConnection = false;
				}
			}
		}
		stringBuilder.Append("\r\n");
		byte[] array = Latin1Bytes(stringBuilder.ToString());
		conn.Stream.Write(array, 0, array.Length);
		if (req != null)
		{
			Log("[bridge] " + req.Method + " " + req.Path);
		}
	}

	private static byte[] Latin1Bytes(string s)
	{
		byte[] array = new byte[s.Length];
		for (int i = 0; i < s.Length; i++)
		{
			array[i] = (byte)((s[i] < 'Ā') ? ((byte)s[i]) : 63);
		}
		return array;
	}

	private void SendError(Conn conn, int code, string message, Request req)
	{
		string value = Explain(code);
		string s = "<!DOCTYPE HTML>\n<html lang=\"en\">\n    <head>\n        <meta charset=\"utf-8\">\n        <title>Error response</title>\n    </head>\n    <body>\n        <h1>Error response</h1>\n        <p>Error code: " + code + "</p>\n        <p>Message: " + WebUtility.HtmlEncode(message) + ".</p>\n        <p>Error code explanation: " + code + " - " + WebUtility.HtmlEncode(value) + ".</p>\n    </body>\n</html>\n";
		byte[] bytes = Encoding.UTF8.GetBytes(s);
		List<KeyValuePair<string, string>> headers = new List<KeyValuePair<string, string>>
		{
			new KeyValuePair<string, string>("Connection", "close"),
			new KeyValuePair<string, string>("Content-Type", "text/html;charset=utf-8"),
			new KeyValuePair<string, string>("Content-Length", bytes.Length.ToString(CultureInfo.InvariantCulture))
		};
		try
		{
			WriteHead(conn, req, code, headers);
			if (req == null || req.Method != "HEAD")
			{
				conn.Stream.Write(bytes, 0, bytes.Length);
			}
			conn.Stream.Flush();
		}
		catch (Exception)
		{
		}
		if (req != null)
		{
			req.CloseConnection = true;
		}
	}

	private void Send(Conn conn, Request req, int code, byte[] body, string contentType, string origin = null, List<KeyValuePair<string, string>> extra = null)
	{
		List<KeyValuePair<string, string>> list = new List<KeyValuePair<string, string>>
		{
			new KeyValuePair<string, string>("Content-Type", contentType),
			new KeyValuePair<string, string>("Content-Length", body.Length.ToString(CultureInfo.InvariantCulture)),
			new KeyValuePair<string, string>("Cache-Control", "no-store")
		};
		if (!string.IsNullOrEmpty(origin))
		{
			list.Add(new KeyValuePair<string, string>("Access-Control-Allow-Origin", origin));
			list.Add(new KeyValuePair<string, string>("Vary", "Origin"));
		}
		if (extra != null)
		{
			list.AddRange(extra);
		}
		WriteHead(conn, req, code, list);
		if (body.Length != 0)
		{
			conn.Stream.Write(body, 0, body.Length);
		}
		conn.Stream.Flush();
	}

	private void SendJson(Conn conn, Request req, int code, object obj, string origin = null)
	{
		byte[] bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(PyJson.Dumps(obj, null, ensureAscii: false));
		Send(conn, req, code, bytes, "application/json; charset=utf-8", origin);
	}

	private static PyDict Err(string msg)
	{
		return PyDict.Of("error", msg);
	}

	private bool RequestOrigin(Conn conn, Request req, out string origin)
	{
		origin = null;
		if (conn.Remote == null || !IPAddress.IsLoopback(conn.Remote.Address))
		{
			SendJson(conn, req, 403, Err("loopback clients only"));
			return false;
		}
		string text = req.Header("Origin");
		if (text == null)
		{
			return true;
		}
		if (!AllowedOrigins.Contains(text))
		{
			SendJson(conn, req, 403, Err("origin not allowed"));
			return false;
		}
		origin = text;
		return true;
	}

	internal static List<string> QueryValues(string query, string key)
	{
		List<string> list = new List<string>();
		string[] array = query.Split('&');
		foreach (string text in array)
		{
			if (text.Length != 0)
			{
				int num = text.IndexOf('=');
				string s = ((num >= 0) ? text.Substring(0, num) : text);
				string s2 = ((num >= 0) ? text.Substring(num + 1) : "");
				if (Unquote(s) == key)
				{
					list.Add(Unquote(s2));
				}
			}
		}
		return list;
	}

	internal static string Unquote(string s)
	{
		s = s.Replace('+', ' ');
		if (s.IndexOf('%') < 0)
		{
			return s;
		}
		List<byte> list = new List<byte>();
		for (int i = 0; i < s.Length; i++)
		{
			if (s[i] == '%' && i + 2 < s.Length && IsHex(s[i + 1]) && IsHex(s[i + 2]))
			{
				list.Add((byte)Convert.ToInt32(s.Substring(i + 1, 2), 16));
				i += 2;
			}
			else
			{
				list.AddRange(Encoding.UTF8.GetBytes(s[i].ToString()));
			}
		}
		return Encoding.UTF8.GetString(list.ToArray());
	}

	private static bool IsHex(char c)
	{
		if ((c < '0' || c > '9') && (c < 'a' || c > 'f'))
		{
			if (c >= 'A')
			{
				return c <= 'F';
			}
			return false;
		}
		return true;
	}

	private bool Authorize(Conn conn, Request req, bool allowSseQuery, out string origin)
	{
		if (!RequestOrigin(conn, req, out origin))
		{
			return false;
		}
		string text = req.Header("X-HelL-Bridge-Token") ?? "";
		if (text.Length == 0 && allowSseQuery)
		{
			List<string> list = QueryValues(req.Query, "token");
			text = ((list.Count == 1) ? list[0] : "");
		}
		if (!TokenMatches(text))
		{
			SendJson(conn, req, 401, Err("authentication required"), origin);
			origin = null;
			return false;
		}
		return true;
	}

	private void HandleOne(Conn conn, Request req)
	{
		switch (req.Method)
		{
		case "GET":
			DoGet(conn, req);
			break;
		case "POST":
			DoPost(conn, req);
			break;
		case "OPTIONS":
			DoOptions(conn, req);
			break;
		default:
			SendError(conn, 501, "Unsupported method (" + Py.ReprStr(req.Method) + ")", req);
			break;
		}
	}

	private void DoOptions(Conn conn, Request req)
	{
		string path = req.Path;
		if (path != "/rpc" && path != "/events" && path != "/health" && path != "/")
		{
			SendJson(conn, req, 404, Err("not found"));
			return;
		}
		string origin;
		bool flag = RequestOrigin(conn, req, out origin);
		if (!flag || origin == null)
		{
			if (flag)
			{
				SendJson(conn, req, 403, Err("origin required"));
			}
		}
		else
		{
			Send(conn, req, 204, new byte[0], "text/plain; charset=utf-8", origin, new List<KeyValuePair<string, string>>
			{
				new KeyValuePair<string, string>("Access-Control-Allow-Methods", "GET, POST, OPTIONS"),
				new KeyValuePair<string, string>("Access-Control-Allow-Headers", "Content-Type, X-HelL-Bridge-Token"),
				new KeyValuePair<string, string>("Access-Control-Max-Age", "600")
			});
		}
	}

	private void DoGet(Conn conn, Request req)
	{
		switch (req.Path)
		{
		case "/health":
		case "/":
		{
			if (Authorize(conn, req, allowSseQuery: false, out string origin2))
			{
				SendJson(conn, req, 200, PyDict.Of("ok", true, "name", "tipcl-bridge"), origin2);
			}
			break;
		}
		case "/events":
		{
			if (Authorize(conn, req, allowSseQuery: true, out string origin3))
			{
				Sse(conn, req, origin3);
			}
			break;
		}
		default:
		{
			if (RequestOrigin(conn, req, out string origin))
			{
				SendJson(conn, req, 404, Err("not found"), origin);
			}
			break;
		}
		}
	}

	private static byte[] ReadExact(Stream s, int n)
	{
		byte[] array = new byte[n];
		int i;
		int num;
		for (i = 0; i < n; i += num)
		{
			num = s.Read(array, i, n - i);
			if (num <= 0)
			{
				break;
			}
		}
		if (i == n)
		{
			return array;
		}
		byte[] array2 = new byte[i];
		Array.Copy(array, array2, i);
		return array2;
	}

	private void DoPost(Conn conn, Request req)
	{
		string path = req.Path;
		if (path != "/rpc" && path != "/")
		{
			if (RequestOrigin(conn, req, out string origin))
			{
				SendJson(conn, req, 404, Err("not found"), origin);
			}
		}
		else
		{
			if (!Authorize(conn, req, allowSseQuery: false, out string origin2))
			{
				return;
			}
			string o = req.Header("Content-Length");
			long num;
			try
			{
				num = (Py.Truthy(o) ? Py.Int(o) : 0);
			}
			catch (PyException)
			{
				SendJson(conn, req, 400, Err("invalid content length"), origin2);
				req.CloseConnection = true;
				return;
			}
			if (num < 0 || num > 4194304)
			{
				SendJson(conn, req, 413, Err("request body too large"), origin2);
				req.CloseConnection = true;
				return;
			}
			byte[] raw = ((num > 0) ? ReadExact(conn.In, (int)num) : Encoding.ASCII.GetBytes("{}"));
			object obj;
			try
			{
				string text = Utils.DecodeUtf8Strict(raw);
				obj = PyJson.Loads((text.Length > 0) ? text : "{}");
			}
			catch (Exception ex2) when (ex2 is UnicodeDecodeError || ex2 is JSONDecodeError)
			{
				SendJson(conn, req, 400, PyDict.Of("jsonrpc", "2.0", "id", null, "error", PyDict.Of("code", -32700L, "message", ex2.Message)), origin2);
				return;
			}
			if (!(obj is PyDict pyDict))
			{
				SendJson(conn, req, 400, PyDict.Of("jsonrpc", "2.0", "id", null, "error", PyDict.Of("code", -32600L, "message", "request must be an object")), origin2);
				return;
			}
			object obj2 = pyDict.Get("id");
			object obj3 = pyDict.Get("method");
			object obj4 = Py.Or(pyDict.Get("params"), new PyDict());
			if (!Py.Truthy(obj3) || !(obj3 is string method))
			{
				SendJson(conn, req, 400, PyDict.Of("jsonrpc", "2.0", "id", obj2, "error", PyDict.Of("code", -32600L, "message", "method required")), origin2);
				return;
			}
			object obj5;
			try
			{
				obj5 = Dispatcher.Call(method, obj4);
			}
			catch (RpcError rpcError)
			{
				SendJson(conn, req, 200, PyDict.Of("jsonrpc", "2.0", "id", obj2, "error", PyDict.Of("code", rpcError.Code, "message", rpcError.Message)), origin2);
				return;
			}
			catch (Exception e)
			{
				SendJson(conn, req, 200, PyDict.Of("jsonrpc", "2.0", "id", obj2, "error", PyDict.Of("code", -32000L, "message", Py.ExcStr(e))), origin2);
				return;
			}
			byte[] bytes;
			try
			{
				bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(PyJson.Dumps(PyDict.Of("jsonrpc", "2.0", "id", obj2, "result", obj5), null, ensureAscii: false));
			}
			catch (Exception e2)
			{
				SendJson(conn, req, 200, PyDict.Of("jsonrpc", "2.0", "id", obj2, "error", PyDict.Of("code", -32000L, "message", Py.ExcStr(e2))), origin2);
				return;
			}
			Send(conn, req, 200, bytes, "application/json; charset=utf-8", origin2);
		}
	}

	private void Sse(Conn conn, Request req, string origin)
	{
		BlockingCollection<PyDict> blockingCollection = null;
		try
		{
			blockingCollection = Bus.Subscribe();
			List<KeyValuePair<string, string>> list = new List<KeyValuePair<string, string>>
			{
				new KeyValuePair<string, string>("Content-Type", "text/event-stream; charset=utf-8"),
				new KeyValuePair<string, string>("Cache-Control", "no-cache, no-store"),
				new KeyValuePair<string, string>("Connection", "keep-alive"),
				new KeyValuePair<string, string>("X-Accel-Buffering", "no")
			};
			if (!string.IsNullOrEmpty(origin))
			{
				list.Add(new KeyValuePair<string, string>("Access-Control-Allow-Origin", origin));
				list.Add(new KeyValuePair<string, string>("Vary", "Origin"));
			}
			WriteHead(conn, req, 200, list);
			UTF8Encoding uTF8Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
			string text = PyJson.Dumps(PyDict.Of("ok", true), null, ensureAscii: false);
			byte[] bytes = uTF8Encoding.GetBytes("event: hello\ndata: " + text + "\n\n");
			conn.Stream.Write(bytes, 0, bytes.Length);
			conn.Stream.Flush();
			byte[] bytes2 = Encoding.ASCII.GetBytes(": keepalive\n\n");
			while (!_stopping)
			{
				if (!blockingCollection.TryTake(out var item, 15000))
				{
					conn.Stream.Write(bytes2, 0, bytes2.Length);
					conn.Stream.Flush();
					continue;
				}
				string text2 = (Py.Truthy(item.Get("event")) ? Py.Str(item.Get("event")) : "message");
				string text3 = PyJson.Dumps(Py.Or(item.Get("data"), new PyDict()), null, ensureAscii: false);
				byte[] bytes3 = uTF8Encoding.GetBytes("event: " + text2 + "\ndata: " + text3 + "\n\n");
				conn.Stream.Write(bytes3, 0, bytes3.Length);
				conn.Stream.Flush();
			}
		}
		catch (Exception)
		{
		}
		finally
		{
			if (blockingCollection != null)
			{
				Bus.Unsubscribe(blockingCollection);
			}
			req.CloseConnection = true;
		}
	}
}
