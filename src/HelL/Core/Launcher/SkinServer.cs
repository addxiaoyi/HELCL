using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace HelL.Core.Launcher;

public static class SkinServer
{
	public sealed class Profile
	{
		public readonly string uuid;

		public readonly string name;

		public readonly string model;

		public readonly string digest;

		public readonly byte[] png;

		public Profile(object uuid, object name, byte[] png, object model)
		{
			this.uuid = Utils.DashedUuid(Py.Str(uuid)).Replace("-", "");
			this.name = (Py.Truthy(name) ? Py.Str(name) : "Player");
			this.png = png;
			this.model = ((Py.Lower(Py.Str(model)) == "slim") ? "slim" : "classic");
			using SHA256 sHA = SHA256.Create();
			byte[] array = sHA.ComputeHash(png);
			StringBuilder stringBuilder = new StringBuilder();
			byte[] array2 = array;
			foreach (byte b in array2)
			{
				stringBuilder.Append(b.ToString("x2"));
			}
			digest = stringBuilder.ToString();
		}
	}

	private sealed class Server
	{
		public TcpListener Listener;

		public int Port;

		public volatile bool Stopping;

		public readonly HashSet<TcpClient> Conns = new HashSet<TcpClient>();
	}

	private sealed class Reply
	{
		public int Code;

		public byte[] Body = new byte[0];

		public string CType = "application/json";

		public bool Drop;
	}

	public const string LOOPBACK = "127.0.0.1";

	public const string CLASSIC = "classic";

	public const string SLIM = "slim";

	public const string ServerVersion = "HelLSkin/1 Python/3.12";

	internal static Func<double> Now = Py.Time;

	private static readonly object _key_lock = new object();

	private static RSAKey _private_key;

	private static readonly Dictionary<string, Profile> _profiles = new Dictionary<string, Profile>();

	private static readonly List<string> _profileOrder = new List<string>();

	private static readonly Dictionary<string, Profile> _by_name = new Dictionary<string, Profile>();

	private static readonly Dictionary<string, byte[]> _textures = new Dictionary<string, byte[]>();

	private static readonly object _registry_lock = new object();

	private static Server _server;

	private static readonly object _server_lock = new object();

	public static string SkinsDir()
	{
		return Skin.SkinsDir();
	}

	public static string _key_path()
	{
		return PPath.Join(SkinsDir(), "yggdrasil_key.pem");
	}

	public static RSAKey _load_key()
	{
		lock (_key_lock)
		{
			if (_private_key != null)
			{
				return _private_key;
			}
			string path = PPath.Abs(_key_path());
			if (File.Exists(path))
			{
				try
				{
					_private_key = RsaLite.load_pem_private_key(File.ReadAllBytes(path));
					return _private_key;
				}
				catch (Exception)
				{
				}
			}
			_private_key = RsaLite.generate();
			File.WriteAllBytes(path, _private_key.private_pem());
			return _private_key;
		}
	}

	internal static void ResetKey()
	{
		lock (_key_lock)
		{
			_private_key = null;
		}
	}

	public static string _public_key_pem()
	{
		return Encoding.ASCII.GetString(_load_key().public_pem());
	}

	public static string _sign(string payload)
	{
		return Convert.ToBase64String(_load_key().sign_sha1(Encoding.ASCII.GetBytes(payload)));
	}

	public static Profile Register(object uuid, object name, byte[] png, object model = null)
	{
		Profile profile = new Profile(uuid, name, png, model ?? "classic");
		lock (_registry_lock)
		{
			if (!_profiles.ContainsKey(profile.uuid))
			{
				_profileOrder.Add(profile.uuid);
			}
			_profiles[profile.uuid] = profile;
			_by_name[Py.Lower(profile.name)] = profile;
			_textures[profile.digest] = png;
			return profile;
		}
	}

	internal static void ResetRegistry()
	{
		lock (_registry_lock)
		{
			_profiles.Clear();
			_profileOrder.Clear();
			_by_name.Clear();
			_textures.Clear();
		}
	}

	public static Profile _find(string uuid = "", string name = "")
	{
		string text = Py.Lower(Utils.DashedUuid(uuid ?? "").Replace("-", ""));
		lock (_registry_lock)
		{
			if (text.Length > 0 && _profiles.TryGetValue(text, out Profile value))
			{
				return value;
			}
			Profile value2;
			if (!string.IsNullOrEmpty(name))
			{
				return _by_name.TryGetValue(Py.Lower(name), out value2) ? value2 : null;
			}
		}
		return null;
	}

	private static Profile _first_profile()
	{
		lock (_registry_lock)
		{
			return (_profileOrder.Count > 0) ? _profiles[_profileOrder[0]] : null;
		}
	}

	public static string _texture_payload(Profile prof, int port)
	{
		PyDict pyDict = PyDict.Of("url", string.Format("http://{0}:{1}/textures/{2}", "127.0.0.1", port, prof.digest));
		if (prof.model == "slim")
		{
			pyDict["metadata"] = PyDict.Of("model", "slim");
		}
		string s = PyJson.Dumps(PyDict.Of("timestamp", (long)Math.Floor(Now() * 1000.0), "profileId", prof.uuid, "profileName", prof.name, "textures", PyDict.Of("SKIN", pyDict)), null, ensureAscii: false, sortKeys: false, ",", ":");
		return Convert.ToBase64String(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(s));
	}

	public static PyDict _profile_json(Profile prof, int port, bool signed = true)
	{
		string text = _texture_payload(prof, port);
		PyDict pyDict = PyDict.Of("name", "textures", "value", text);
		if (signed)
		{
			pyDict["signature"] = _sign(text);
		}
		return PyDict.Of("id", prof.uuid, "name", prof.name, "properties", new List<object> { pyDict });
	}

	private static Reply R(int code, byte[] body = null, string ctype = "application/json")
	{
		return new Reply
		{
			Code = code,
			Body = (body ?? new byte[0]),
			CType = ctype
		};
	}

	private static Reply J(int code, object doc)
	{
		return R(code, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(PyJson.Dumps(doc, null, ensureAscii: false)));
	}

	private static string Qs(string query, string key)
	{
		string[] array = query.Split('&');
		foreach (string text in array)
		{
			if (text.Length == 0)
			{
				continue;
			}
			int num = text.IndexOf('=');
			if (num >= 0)
			{
				string text2 = BridgeServer.Unquote(text.Substring(0, num));
				string text3 = BridgeServer.Unquote(text.Substring(num + 1));
				if (text2 == key && text3.Length > 0)
				{
					return text3;
				}
			}
		}
		return null;
	}

	private static string RStripSlash(string p)
	{
		string text = p.TrimEnd('/');
		if (text.Length != 0)
		{
			return text;
		}
		return "/";
	}

	private static string LastSeg(string p)
	{
		int num = p.LastIndexOf('/');
		if (num >= 0)
		{
			return p.Substring(num + 1);
		}
		return p;
	}

	private static Reply DoGet(string target, int port)
	{
		BridgeServer.SplitTarget(target, out string path, out string query);
		string text = RStripSlash(path);
		if (text == "/")
		{
			return J(200, PyDict.Of("meta", PyDict.Of("serverName", "HelL 本地皮肤", "implementationName", "tipcl-skinserver", "implementationVersion", "1"), "skinDomains", new List<object> { "127.0.0.1", "localhost" }, "signaturePublickey", _public_key_pem()));
		}
		if (text.StartsWith("/textures/", StringComparison.Ordinal))
		{
			string key = LastSeg(text);
			byte[] value;
			lock (_registry_lock)
			{
				_textures.TryGetValue(key, out value);
			}
			if (value == null)
			{
				return R(404);
			}
			return R(200, value, "image/png");
		}
		if (text.StartsWith("/sessionserver/session/minecraft/profile/", StringComparison.Ordinal))
		{
			Profile profile = _find(LastSeg(text));
			if (profile == null)
			{
				return R(204);
			}
			bool signed = Py.Lower(Qs(query, "unsigned") ?? "false") != "true";
			return J(200, _profile_json(profile, port, signed));
		}
		if (text == "/sessionserver/session/minecraft/hasJoined")
		{
			Profile profile2 = _find("", Qs(query, "username") ?? "");
			if (profile2 == null)
			{
				return R(204);
			}
			return J(200, _profile_json(profile2, port));
		}
		return R(404);
	}

	private static Reply DoPost(string target, byte[] raw)
	{
		BridgeServer.SplitTarget(target, out string path, out string _);
		string text = RStripSlash(path);
		object obj;
		try
		{
			obj = ((raw.Length != 0) ? PyJson.Loads(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(raw)) : new PyDict());
		}
		catch (Exception)
		{
			obj = new PyDict();
		}
		if (text == "/api/profiles/minecraft")
		{
			List<object> obj2 = (obj as List<object>) ?? new List<object>();
			List<object> list = new List<object>();
			foreach (object item in obj2)
			{
				Profile profile = _find("", Py.Str(item));
				if (profile != null)
				{
					list.Add(PyDict.Of("id", profile.uuid, "name", profile.name));
				}
			}
			return J(200, list);
		}
		if (text == "/sessionserver/session/minecraft/join")
		{
			return R(204);
		}
		if (text.StartsWith("/authserver/", StringComparison.Ordinal))
		{
			switch (LastSeg(text))
			{
			case "validate":
			case "invalidate":
			case "signout":
				return R(204);
			default:
			{
				if (!(obj is PyDict pyDict))
				{
					return new Reply
					{
						Drop = true
					};
				}
				Profile profile2 = _find("", Py.Str(Py.Or(pyDict.Get("username"), ""))) ?? _first_profile();
				if (profile2 == null)
				{
					return J(403, PyDict.Of("error", "ForbiddenOperationException", "errorMessage", "没有可用的离线角色"));
				}
				PyDict pyDict2 = PyDict.Of("id", profile2.uuid, "name", profile2.name);
				return J(200, PyDict.Of("accessToken", Py.Or(pyDict.Get("accessToken"), "tipcl-offline"), "clientToken", Py.Or(pyDict.Get("clientToken"), "tipcl"), "selectedProfile", pyDict2, "availableProfiles", new List<object> { pyDict2 }));
			}
			}
		}
		return R(404);
	}

	private static void Write(Stream s, string method, Reply r, bool close)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append("HTTP/1.1 ").Append(r.Code).Append(' ')
			.Append(BridgeServer.Phrase(r.Code))
			.Append("\r\n");
		stringBuilder.Append("Server: ").Append("HelLSkin/1 Python/3.12").Append("\r\n");
		stringBuilder.Append("Date: ").Append(DateTime.UtcNow.ToString("r", CultureInfo.InvariantCulture)).Append("\r\n");
		if (r.Body.Length != 0)
		{
			stringBuilder.Append("Content-Type: ").Append(r.CType).Append("\r\n");
		}
		stringBuilder.Append("Content-Length: ").Append(r.Body.Length.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
		if (close)
		{
			stringBuilder.Append("Connection: close\r\n");
		}
		stringBuilder.Append("\r\n");
		byte[] bytes = Encoding.ASCII.GetBytes(stringBuilder.ToString());
		s.Write(bytes, 0, bytes.Length);
		if (r.Body.Length != 0 && method != "HEAD")
		{
			s.Write(r.Body, 0, r.Body.Length);
		}
		s.Flush();
	}

	private static void HandleConnection(Server srv, TcpClient client)
	{
		try
		{
			client.NoDelay = true;
			NetworkStream stream = client.GetStream();
			BufferedStream bufferedStream = new BufferedStream(stream, 65536);
			while (!srv.Stopping)
			{
				bool eof;
				bool tooLong;
				string text = BridgeServer.ReadLine(bufferedStream, out eof, out tooLong);
				if (eof || tooLong || string.IsNullOrEmpty(text))
				{
					break;
				}
				string[] array = text.Split(new char[1] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
				if (array.Length != 3 || !array[2].StartsWith("HTTP/"))
				{
					break;
				}
				bool flag = string.CompareOrdinal(array[2], "HTTP/1.1") < 0;
				Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				while (true)
				{
					bool eof2;
					bool tooLong2;
					string text2 = BridgeServer.ReadLine(bufferedStream, out eof2, out tooLong2);
					if (tooLong2)
					{
						return;
					}
					if (eof2 || text2.Length == 0)
					{
						break;
					}
					int num = text2.IndexOf(':');
					if (num > 0)
					{
						dictionary[text2.Substring(0, num).Trim()] = text2.Substring(num + 1).Trim(' ', '\t');
					}
				}
				if (dictionary.TryGetValue("Connection", out var value))
				{
					if (value.Equals("close", StringComparison.OrdinalIgnoreCase))
					{
						flag = true;
					}
					else if (value.Equals("keep-alive", StringComparison.OrdinalIgnoreCase))
					{
						flag = false;
					}
				}
				string text3 = array[0];
				string target = array[1];
				Reply reply;
				if (text3 == "GET")
				{
					reply = DoGet(target, srv.Port);
				}
				else
				{
					if (!(text3 == "POST"))
					{
						byte[] bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes("<!DOCTYPE HTML>\n<html lang=\"en\">\n    <head>\n        <meta charset=\"utf-8\">\n        <title>Error response</title>\n    </head>\n    <body>\n        <h1>Error response</h1>\n        <p>Error code: 501</p>\n        <p>Message: Unsupported method (" + WebUtility.HtmlEncode(Py.ReprStr(text3)) + ").</p>\n        <p>Error code explanation: 501 - Server does not support this operation.</p>\n    </body>\n</html>\n");
						Write(stream, text3, new Reply
						{
							Code = 501,
							Body = bytes,
							CType = "text/html;charset=utf-8"
						}, close: true);
						break;
					}
					long result = 0L;
					if (dictionary.TryGetValue("Content-Length", out var value2) && value2.Length > 0 && !long.TryParse(value2.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out result))
					{
						break;
					}
					byte[] array2 = new byte[Math.Max(0L, result)];
					int i;
					int num2;
					for (i = 0; i < array2.Length; i += num2)
					{
						num2 = bufferedStream.Read(array2, i, array2.Length - i);
						if (num2 <= 0)
						{
							break;
						}
					}
					if (i < array2.Length)
					{
						Array.Resize(ref array2, i);
					}
					reply = DoPost(target, array2);
				}
				if (!reply.Drop)
				{
					Write(stream, text3, reply, close: false);
					if (flag)
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
			lock (srv.Conns)
			{
				srv.Conns.Remove(client);
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

	private static void AcceptLoop(Server srv)
	{
		while (!srv.Stopping)
		{
			TcpClient client;
			try
			{
				client = srv.Listener.AcceptTcpClient();
			}
			catch (Exception)
			{
				if (srv.Stopping)
				{
					break;
				}
				Thread.Sleep(50);
				continue;
			}
			lock (srv.Conns)
			{
				srv.Conns.Add(client);
			}
			Thread thread = new Thread((ThreadStart)delegate
			{
				HandleConnection(srv, client);
			});
			thread.IsBackground = true;
			thread.Name = "tipcl-skinserver-conn";
			thread.Start();
		}
	}

	public static string EnsureRunning()
	{
		int port;
		lock (_server_lock)
		{
			if (_server == null)
			{
				Server srv = new Server
				{
					Listener = new TcpListener(IPAddress.Loopback, 0)
				};
				srv.Listener.Start();
				srv.Port = ((IPEndPoint)srv.Listener.LocalEndpoint).Port;
				Thread thread = new Thread((ThreadStart)delegate
				{
					AcceptLoop(srv);
				});
				thread.IsBackground = true;
				thread.Name = "tipcl-skinserver";
				thread.Start();
				_server = srv;
			}
			port = _server.Port;
		}
		return string.Format("http://{0}:{1}", "127.0.0.1", port);
	}

	public static string ApiFor(object uuid, object name, byte[] png, object model = null)
	{
		if (png == null || png.Length == 0)
		{
			throw new SkinServerError("皮肤数据为空");
		}
		_load_key();
		Register(uuid, name, png, model ?? "classic");
		return EnsureRunning();
	}

	public static void Shutdown()
	{
		Server server;
		lock (_server_lock)
		{
			server = _server;
			_server = null;
		}
		if (server == null)
		{
			return;
		}
		server.Stopping = true;
		try
		{
			server.Listener.Stop();
		}
		catch (Exception)
		{
		}
		List<TcpClient> list;
		lock (server.Conns)
		{
			list = server.Conns.ToList();
		}
		foreach (TcpClient item in list)
		{
			try
			{
				item.Close();
			}
			catch (Exception)
			{
			}
		}
	}
}
