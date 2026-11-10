using System;
using System.IO;
using System.Text.RegularExpressions;

namespace HelL.Core.Launcher;

public static class Nide8
{
	public const string NIDE8_AUTH = "https://auth.mc-user.com:233";

	public const string NIDE8_JAR_URL = "https://login.mc-user.com:233/index/jar";

	private static readonly Regex _SID_RE = new Regex("([0-9a-fA-F]{32})", RegexOptions.CultureInvariant);

	public static string NormalizeServerId(object raw)
	{
		string text = Py.Strip(Py.S(raw));
		if (text.Length == 0)
		{
			throw new AuthError("请填写统一通行证服务器 ID");
		}
		Match match = _SID_RE.Match(text);
		if (match.Success)
		{
			return match.Groups[1].Value.ToLowerInvariant();
		}
		string text2 = (text.Contains("://") ? text : ("https://" + text));
		string text3 = Authlib.Netloc(text2);
		string text4 = "";
		int num = text2.IndexOf("//");
		if (num >= 0)
		{
			string text5 = text2.Substring(num + 2 + text3.Length);
			int num2 = text5.IndexOfAny(new char[2] { '?', '#' });
			text4 = ((num2 < 0) ? text5 : text5.Substring(0, num2));
			int num3 = text4.IndexOf(';');
			if (num3 >= 0)
			{
				text4 = text4.Substring(0, num3);
			}
		}
		match = _SID_RE.Match(text4);
		if (match.Success)
		{
			return match.Groups[1].Value.ToLowerInvariant();
		}
		throw new AuthError("服务器 ID 应为 32 位十六进制，或含该 ID 的链接");
	}

	public static string ApiUrl(object server_id)
	{
		return "https://auth.mc-user.com:233/" + NormalizeServerId(server_id);
	}

	public static string JarPath()
	{
		return PPath.Join(Utils.ROOT, "nide8auth.jar");
	}

	public static string EnsureJar(DownloadManager dm = null, Action<string> on_note = null)
	{
		string text = JarPath();
		if (new PPath(text).IsFile() && new FileInfo(Utils.AbsPath(text)).Length > 8000)
		{
			return text;
		}
		dm = dm ?? new DownloadManager(2L);
		on_note?.Invoke("下载 nide8auth");
		dm.Download("https://login.mc-user.com:233/index/jar", text, null, null, force: false, 60L);
		if (!new PPath(text).IsFile() || new FileInfo(Utils.AbsPath(text)).Length < 8000)
		{
			throw new AuthError("无法下载 nide8auth.jar");
		}
		return text;
	}

	public static string JavaagentArg(object server_id)
	{
		return "-javaagent:" + JarPath() + "=" + NormalizeServerId(server_id);
	}

	public static PyDict Login(object server_id, object username0, object password)
	{
		string text = NormalizeServerId(server_id);
		string text2 = Py.Strip(Py.AsStr(Py.Or(username0, ""), "strip"));
		if (text2.Length == 0 || !Py.Truthy(password))
		{
			throw new AuthError("请输入统一通行证账号和密码");
		}
		string url = ApiUrl(text) + "/authserver/authenticate";
		AuthResponse authResponse;
		try
		{
			authResponse = AuthHttp.PostJson(url, PyDict.Of("agent", PyDict.Of("name", "Minecraft", "version", 1L), "username", text2, "password", password, "requestUser", true));
		}
		catch (AuthHttpException ex)
		{
			throw new AuthError("统一通行证无法连接: " + ex.Message);
		}
		object obj = Authlib.JsonOrEmpty(authResponse);
		if (authResponse.StatusCode >= 400)
		{
			object o = Py.Or(Authlib.PyGet(obj, "errorMessage"), Authlib.PyGet(obj, "error"), authResponse.Text200);
			throw new AuthError("统一通行证登录失败: " + Py.Str(o));
		}
		object d = Authlib.PickProfile(obj);
		if (!Py.Truthy(Authlib.PyGet(d, "name")))
		{
			throw new AuthError("该通行证没有可用角色");
		}
		return PyDict.Of("type", "nide8", "name", Authlib.PyGet(d, "name"), "uuid", Utils.DashedUuid(Py.Str(Py.Or(Authlib.PyGet(d, "id"), ""))), "access_token", Py.Or(Authlib.PyGet(obj, "accessToken"), "0"), "client_token", Py.Or(Authlib.PyGet(obj, "clientToken"), ""), "server_id", text, "api", ApiUrl(text), "username", text2, "expires_at", Py.Time() + 604800.0, "updated_at", Py.Time());
	}

	public static PyDict Refresh(PyDict account0)
	{
		string server_id = NormalizeServerId(Py.Or(account0.Get("server_id"), account0.Get("api"), ""));
		object obj = account0.Get("access_token");
		object obj2 = Py.Or(account0.Get("client_token"), "");
		if (!Py.Truthy(obj))
		{
			throw new AuthError("统一通行证令牌缺失，请重新登录");
		}
		string url = ApiUrl(server_id) + "/authserver/refresh";
		AuthResponse authResponse;
		try
		{
			authResponse = AuthHttp.PostJson(url, PyDict.Of("accessToken", obj, "clientToken", obj2, "requestUser", true));
		}
		catch (AuthHttpException ex)
		{
			throw new AuthError("统一通行证刷新失败: " + ex.Message);
		}
		if (authResponse.StatusCode >= 400)
		{
			throw new AuthError("统一通行证令牌已过期，请重新登录");
		}
		object d = ((authResponse.Content != null && authResponse.Content.Length != 0) ? authResponse.Json() : new PyDict());
		object d2 = Authlib.OrDict(Authlib.PyGet(d, "selectedProfile"));
		PyDict pyDict = new PyDict(account0);
		pyDict["access_token"] = Py.Or(Authlib.PyGet(d, "accessToken"), obj);
		if (Py.Truthy(Authlib.PyGet(d2, "name")))
		{
			pyDict["name"] = Authlib.PyGet(d2, "name");
		}
		if (Py.Truthy(Authlib.PyGet(d2, "id")))
		{
			pyDict["uuid"] = Utils.DashedUuid(Py.Str(Authlib.PyGet(d2, "id")));
		}
		pyDict["expires_at"] = Py.Time() + 604800.0;
		return pyDict;
	}
}
