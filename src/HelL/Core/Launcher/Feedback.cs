using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace HelL.Core.Launcher;

public static class Feedback
{
	public const int MAX_HISTORY = 30;

	public static string HISTORY_FILE => PPath.Join(Utils.ROOT, "feedback_history.json");

	public static string DEVICE_FILE => PPath.Join(Utils.ROOT, "device_id");

	public static List<object> _history_load()
	{
		object obj = Utils.ReadJson(HISTORY_FILE, new List<object>());
		if (!Py.Truthy(obj))
		{
			obj = new List<object>();
		}
		return (obj as List<object>) ?? new List<object>();
	}

	public static List<object> History()
	{
		return _history_load();
	}

	public static string CategoryLabel(object key)
	{
		(string, string)[] cATEGORIES = FeedbackDefaults.CATEGORIES;
		for (int i = 0; i < cATEGORIES.Length; i++)
		{
			var (a, result) = cATEGORIES[i];
			if (Py.Eq(a, key))
			{
				return result;
			}
		}
		if (!Py.Truthy(key))
		{
			return "其他";
		}
		return Py.Str(key);
	}

	public static string DeviceId()
	{
		string text = Py.Strip(Py.AsStr(Py.Or(Config.CONFIG.Get("device_id"), ""), "strip"));
		if (text.Length > 0)
		{
			return text;
		}
		try
		{
			string path = Utils.AbsPath(DEVICE_FILE);
			if (File.Exists(path))
			{
				string text2 = Py.Strip(Utils.DecodeUtf8Strict(File.ReadAllBytes(path)));
				if (text2.Length > 0)
				{
					return text2;
				}
			}
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
		{
		}
		string text3 = Guid.NewGuid().ToString("N");
		try
		{
			Utils.EnsureDir(new PPath(DEVICE_FILE).Parent.ToString());
			File.WriteAllBytes(Utils.AbsPath(DEVICE_FILE), Encoding.UTF8.GetBytes(text3));
		}
		catch (Exception ex2) when (ex2 is IOException || ex2 is UnauthorizedAccessException)
		{
		}
		try
		{
			Config.CONFIG.Set("device_id", text3);
			Config.CONFIG.Save();
		}
		catch (Exception)
		{
		}
		return text3;
	}

	public static string ResolveUrl()
	{
		string text = Py.Strip(Py.AsStr(Py.Or(Config.CONFIG.Get("feedback_url"), ""), "strip"));
		if (text.Length == 0)
		{
			text = Py.Strip((Py.Or(Environment.GetEnvironmentVariable("TIPCL_FEEDBACK_URL"), "http://114.66.28.184:53611", "") as string) ?? "");
		}
		return Py.RStrip(text, "/");
	}

	public static bool HasConsent()
	{
		object obj = Config.CONFIG.Get("feedback_consent");
		bool flag = false;
		int num;
		if (obj is bool)
		{
			flag = (bool)obj;
			num = 1;
		}
		else
		{
			num = 0;
		}
		return (byte)(num & (flag ? 1 : 0)) != 0;
	}

	public static bool ConsentAsked()
	{
		return Config.CONFIG.Get("feedback_consent") != null;
	}

	private static Dictionary<string, string> _headers()
	{
		return new Dictionary<string, string> { { "X-HelL-Client", AppInfo.CLIENT_HEADER } };
	}

	public static PyDict _post(string path, PyDict payload, double timeout = 20.0)
	{
		string text = ResolveUrl();
		if (text.Length == 0)
		{
			throw new FeedbackError("未配置反馈服务器。开发者请启动 feedback_hub，并在设置里填写地址。");
		}
		string url = text + path;
		AuthResponse authResponse;
		try
		{
			authResponse = AuthHttp.Send("POST", url, null, payload, hasJson: true, _headers(), timeout, AppInfo.CLIENT_HEADER);
		}
		catch (AuthHttpException ex)
		{
			throw new FeedbackError("连不上反馈服务器: " + ex.Message, ex);
		}
		string arg = Py.Slice(authResponse.Text ?? "", 0, 800);
		if (authResponse.StatusCode >= 400)
		{
			throw new FeedbackError($"反馈服务器 HTTP {authResponse.StatusCode}: {arg}");
		}
		object obj;
		try
		{
			obj = authResponse.Json();
		}
		catch (Exception)
		{
			throw new FeedbackError("反馈服务器返回了无法解析的内容");
		}
		if (!(obj is PyDict pyDict))
		{
			throw new FeedbackError("反馈服务器返回格式不对");
		}
		if (pyDict.ContainsKey("ok"))
		{
			object obj2 = pyDict["ok"];
			if (obj2 is bool && !(bool)obj2)
			{
				throw new FeedbackError(Py.Str(Py.Or(pyDict["error"], "提交失败")));
			}
		}
		return pyDict;
	}

	public static void _history_add(PyDict row)
	{
		List<object> list = _history_load();
		list.Insert(0, row);
		Utils.WriteJson(HISTORY_FILE, list.Take(30).ToList());
	}

	private static string CpSlice(string s, int n)
	{
		StringBuilder stringBuilder = new StringBuilder();
		int num = 0;
		int num2 = 0;
		while (num2 < s.Length && num < n)
		{
			stringBuilder.Append(s[num2]);
			if (char.IsHighSurrogate(s[num2]) && num2 + 1 < s.Length && char.IsLowSurrogate(s[num2 + 1]))
			{
				stringBuilder.Append(s[++num2]);
			}
			num2++;
			num++;
		}
		return stringBuilder.ToString();
	}

	private static string StripArg(object x)
	{
		return Py.Strip(Py.AsStr(Py.Truthy(x) ? x : "", "strip"));
	}

	public static PyDict Submit(object category, object title, object body, object contact = null, object include_sysinfo = null, object crash = null, bool scan_system_java = true)
	{
		if (!HasConsent())
		{
			throw new FeedbackError("需要先同意上传诊断数据。第一次打开启动器时会询问，也可在设置里开启。");
		}
		string cat = Py.Lower(Py.Strip(Py.AsStr(Py.Truthy(category) ? category : "other", "strip")));
		if (!FeedbackDefaults.CATEGORIES.Any(((string key, string label) c) => c.key == cat))
		{
			cat = "other";
		}
		string text = CpSlice(StripArg(title), 120);
		string text2 = CpSlice(StripArg(body), 16000);
		string text3 = CpSlice(StripArg(contact), 120);
		if (text.Length == 0 && text2.Length == 0)
		{
			throw new FeedbackError("请填写标题或内容");
		}
		if (text.Length == 0)
		{
			text = CpSlice((text2.Length > 0) ? Py.SplitLines(text2)[0] : "未命名反馈", 80);
		}
		PyDict pyDict = PyDict.Of("device_id", DeviceId(), "category", cat, "title", text, "body", text2, "contact", text3, "app_version", AppInfo.APP_VERSION, "crash", (crash is PyDict) ? crash : null);
		if (Py.Truthy(include_sysinfo))
		{
			pyDict["sysinfo"] = Sysinfo.Collect(force: true, scan_system_java);
		}
		PyDict pyDict2 = _post("/api/v1/feedback", pyDict, 25.0);
		_history_add(PyDict.Of("id", Py.Or(pyDict2["id"], ""), "ts", Py.Time(), "category", cat, "title", text, "ok", true));
		return pyDict2;
	}

	private static object G(object d, string k)
	{
		if (d is PyDict pyDict)
		{
			return pyDict[k];
		}
		throw new AttributeError("'" + Py.TypeName(d) + "' object has no attribute 'get'");
	}

	private static object SliceObj(object x, int n)
	{
		if (!(x is string s))
		{
			List<object> obj = x as List<object>;
			if (obj == null)
			{
				if (x is PyDict)
				{
					throw new PyException("KeyError", $"slice(None, {n}, None)");
				}
				throw new TypeError("'" + Py.TypeName(x) + "' object is not subscriptable");
			}
			return obj.Take(n).ToList();
		}
		return CpSlice(s, n);
	}

	public static PyDict SubmitCrash(object report)
	{
		return SubmitCrash(report, "");
	}

	public static PyDict SubmitCrash(object report, object extra)
	{
		if (!Py.Truthy(report))
		{
			report = new PyDict();
		}
		object obj = SliceObj(Py.Or(G(report, "headline"), G(report, "title"), Py.Or(G(report, "summary"), "游戏崩溃")), 120);
		List<object> source = new List<object>
		{
			Py.Strip(Py.AsStr(extra, "strip")),
			Py.Str(Py.Or(G(report, "summary"), "")),
			SliceObj(Py.Str(Py.Or(G(report, "detail"), G(report, "output_tail"), "")), 8000)
		};
		object obj2 = Py.Strip(string.Join("\n\n", source.Where(Py.Truthy).Cast<string>()));
		if (!Py.Truthy(obj2))
		{
			obj2 = obj;
		}
		PyDict crash = PyDict.Of("headline", Py.Or(G(report, "headline"), ""), "summary", Py.Or(G(report, "summary"), ""), "title", Py.Or(G(report, "title"), ""), "help", Py.Or(G(report, "help"), ""), "direct_file", Py.Or(G(report, "direct_file"), ""));
		return Submit("crash", obj, obj2, "", true, crash);
	}
}
