using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace HelL.Core.Launcher;

public static class Authlib
{
	public static readonly string[][] PRESETS = new string[2][]
	{
		new string[2] { "Little Skin", "https://littleskin.cn/api/yggdrasil" },
		new string[2] { "Blessing Skin（自填）", "" }
	};

	public const string LATEST = "https://authlib-injector.yushi.moe/artifact/latest.json";

	public const string BMCL_LATEST = "https://bmclapi2.bangbang93.com/mirrors/authlib-injector/artifact/latest.json";

	internal static object PyGet(object d, string key)
	{
		if (d is PyDict pyDict)
		{
			return pyDict.Get(key);
		}
		throw new AttributeError("'" + Py.TypeName(d) + "' object has no attribute 'get'");
	}

	internal static object JsonOrEmpty(AuthResponse resp)
	{
		try
		{
			return resp.Json();
		}
		catch (ValueError)
		{
			return new PyDict();
		}
	}

	internal static object OrDict(object x)
	{
		if (!Py.Truthy(x))
		{
			return new PyDict();
		}
		return x;
	}

	internal static object PickProfile(object data)
	{
		object obj = OrDict(PyGet(data, "selectedProfile"));
		if (!Py.Truthy(obj))
		{
			object obj2 = Py.Or(PyGet(data, "availableProfiles"), new List<object>());
			if (Py.Truthy(obj2))
			{
				obj = First(obj2);
			}
		}
		return obj;
	}

	internal static object First(object seq)
	{
		if (seq is List<object> list)
		{
			return list[0];
		}
		if (seq is string text)
		{
			return text.Substring(0, 1);
		}
		if (seq is PyDict)
		{
			throw new KeyError(0L);
		}
		throw new TypeError("'" + Py.TypeName(seq) + "' object is not subscriptable");
	}

	private static object _post(string api, string path, PyDict payload)
	{
		string url = NormalizeApi(api) + path;
		AuthResponse authResponse;
		try
		{
			authResponse = AuthHttp.PostJson(url, payload);
		}
		catch (AuthHttpException ex)
		{
			throw new AuthError("皮肤站无法连接: " + ex.Message);
		}
		object obj = JsonOrEmpty(authResponse);
		if (authResponse.StatusCode >= 400)
		{
			object o = Py.Or(PyGet(obj, "errorMessage"), PyGet(obj, "error"), authResponse.Text200);
			throw new AuthError("皮肤站登录失败: " + Py.Str(o));
		}
		return obj;
	}

	public static PyDict Login(object api0, object username0, object password)
	{
		string text = NormalizeApi(api0);
		string text2 = Py.Strip(Py.AsStr(Py.Or(username0, ""), "strip"));
		if (text2.Length == 0 || !Py.Truthy(password))
		{
			throw new AuthError("请输入皮肤站账号和密码");
		}
		object obj = _post(text, "/authserver/authenticate", PyDict.Of("agent", PyDict.Of("name", "Minecraft", "version", 1L), "username", text2, "password", password, "requestUser", true));
		object d = PickProfile(obj);
		if (!Py.Truthy(PyGet(d, "name")))
		{
			throw new AuthError("皮肤站没有可用角色，请先在网站创建角色");
		}
		return PyDict.Of("type", "authlib", "name", PyGet(d, "name"), "uuid", Utils.DashedUuid(Py.Str(Py.Or(PyGet(d, "id"), ""))), "access_token", Py.Or(PyGet(obj, "accessToken"), "0"), "client_token", Py.Or(PyGet(obj, "clientToken"), ""), "api", text, "username", text2, "expires_at", Py.Time() + 604800.0, "updated_at", Py.Time());
	}

	public static PyDict Refresh(PyDict account)
	{
		string api = NormalizeApi(Py.Or(account.Get("api"), ""));
		object obj = account.Get("access_token");
		if (!Py.Truthy(obj))
		{
			throw new AuthError("皮肤站令牌缺失，请重新登录");
		}
		PyDict pyDict = PyDict.Of("accessToken", obj, "requestUser", true);
		if (Py.Truthy(account.Get("client_token")))
		{
			pyDict["clientToken"] = account.Req("client_token");
		}
		object d = _post(api, "/authserver/refresh", pyDict);
		object d2 = OrDict(PyGet(d, "selectedProfile"));
		account.Update(PyDict.Of("access_token", Py.Or(PyGet(d, "accessToken"), obj), "client_token", Py.Or(PyGet(d, "clientToken"), account.Get("client_token"), ""), "name", Py.Or(PyGet(d2, "name"), account.Get("name")), "uuid", Utils.DashedUuid(Py.Str(Py.Or(PyGet(d2, "id"), account.Get("uuid"), ""))), "expires_at", Py.Time() + 604800.0, "updated_at", Py.Time()));
		return account;
	}

	internal static string Netloc(string url)
	{
		Match match = Regex.Match(url, "^[A-Za-z][A-Za-z0-9+.\\-]*:");
		string text = (match.Success ? url.Substring(match.Length) : url);
		if (!text.StartsWith("//"))
		{
			return "";
		}
		text = text.Substring(2);
		int num = text.IndexOfAny(new char[3] { '/', '?', '#' });
		if (num >= 0)
		{
			return text.Substring(0, num);
		}
		return text;
	}

	public static string NormalizeApi(object url)
	{
		string text = Py.RStrip(Py.Strip(Py.S(url)), "/");
		if (text.Length == 0)
		{
			throw new AuthError("请填写皮肤站 Yggdrasil API 地址");
		}
		if (!text.StartsWith("http"))
		{
			text = "https://" + text;
		}
		if (Netloc(text).Length == 0)
		{
			throw new AuthError("皮肤站地址无效");
		}
		return text;
	}

	public static string InjectorPath()
	{
		return PPath.Join(Utils.ROOT, "authlib-injector.jar");
	}

	public static string EnsureInjector(DownloadManager dm = null, Action<string> on_note = null)
	{
		string text = InjectorPath();
		if (new PPath(text).IsFile() && new FileInfo(Utils.AbsPath(text)).Length > 10000)
		{
			return text;
		}
		dm = dm ?? new DownloadManager(2L);
		Exception ex = null;
		string[] array = new string[2] { "https://bmclapi2.bangbang93.com/mirrors/authlib-injector/artifact/latest.json", "https://authlib-injector.yushi.moe/artifact/latest.json" };
		foreach (string url in array)
		{
			try
			{
				PyDict pyDict = Py.DictOr(Py.Or(dm.FetchJson(url, 20L), new PyDict()));
				object obj = Py.Or(pyDict.Get("download_url"), pyDict.Get("url"));
				if (Py.Truthy(obj))
				{
					on_note?.Invoke("下载 authlib-injector");
					dm.Download(obj, text);
					if (new PPath(text).IsFile())
					{
						return text;
					}
				}
			}
			catch (Exception ex2)
			{
				ex = ex2;
			}
		}
		throw new AuthError("无法下载 authlib-injector: " + ((ex == null) ? "None" : Py.ExcStr(ex)));
	}

	public static string JavaagentArg(object api)
	{
		return "-javaagent:" + InjectorPath() + "=" + NormalizeApi(api);
	}
}
