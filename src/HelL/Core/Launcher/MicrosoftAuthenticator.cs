using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace HelL.Core.Launcher;

public class MicrosoftAuthenticator
{
	public readonly string client_id;

	public readonly double timeout;
	private readonly CancellationToken cancellation;

	private const string UA = "HelL/1.0";
	private const int MaxAttempts = 3;
	private const double MaxRetryDelaySeconds = 30;

	public MicrosoftAuthenticator(string client_id = "00000000402b5328", double timeout = 15.0, CancellationToken cancellation = default(CancellationToken))
	{
		this.client_id = client_id;
		this.timeout = timeout;
		this.cancellation = cancellation;
	}

	private AuthResponse PostForm(string url, PyDict form)
	{
		return Request(() => AuthHttp.Send("POST", url, form, null, hasJson: false, null, timeout, UA, cancellation));
	}

	private AuthResponse PostJson(string url, object json)
	{
		return Request(() => AuthHttp.Send("POST", url, null, json, hasJson: true, null, timeout, UA, cancellation));
	}

	private AuthResponse GetBearer(string url, object token)
	{
		return Request(() => AuthHttp.Send("GET", url, null, null, hasJson: false, new Dictionary<string, string> {
		{
			"Authorization",
			"Bearer " + Py.Str(token)
		} }, timeout, UA, cancellation));
	}

	private AuthResponse Request(Func<AuthResponse> send)
	{
		for (int attempt = 0; attempt < MaxAttempts; attempt++)
		{
			cancellation.ThrowIfCancellationRequested();
			double delay = 1 << attempt;
			try
			{
				AuthResponse response = send();
				bool transient = response.StatusCode == 408 || response.StatusCode == 429 || response.StatusCode >= 500;
				if (!transient)
					return response;
				delay = Math.Max(delay, response.RetryAfterSeconds ?? 0);
				if (attempt == MaxAttempts - 1 || delay > MaxRetryDelaySeconds)
					throw new AuthError($"登录服务暂时不可用 (HTTP {response.StatusCode})，请稍后重试。");
			}
			catch (AuthHttpException)
			{
				if (attempt == MaxAttempts - 1)
					throw new AuthError("无法连接登录服务，请检查网络或代理后重试。");
			}
			Wait(TimeSpan.FromSeconds(delay));
		}
		throw new InvalidOperationException("Authentication retry limit exceeded.");
	}

	private static PyDict ReadObject(AuthResponse response, string stage)
	{
		try
		{
			if (response.Json() is PyDict json)
				return json;
		}
		catch (ValueError)
		{
			throw new AuthError(stage + "返回了无效响应，请稍后重试。");
		}
		throw new AuthError(stage + "返回了无效响应，请稍后重试。");
	}

	private static string RequiredText(PyDict json, string key, string stage)
	{
		if (json.Get(key) is string value && !string.IsNullOrWhiteSpace(value))
			return value;
		throw new AuthError(stage + "响应缺少有效的 " + key + "，请稍后重试。");
	}

	private static double PositiveSeconds(object value, string stage)
	{
		if (value is long || value is double || value is int)
		{
			double seconds = Convert.ToDouble(value);
			if (!double.IsNaN(seconds) && !double.IsInfinity(seconds) && seconds > 0 && seconds <= int.MaxValue)
				return seconds;
		}
		throw new AuthError(stage + "返回了无效的有效期，请稍后重试。");
	}

	public PyDict GetDeviceCode()
	{
		AuthResponse authResponse = PostForm("https://login.live.com/oauth20_connect.srf", PyDict.Of("client_id", client_id, "scope", "service::user.auth.xboxlive.com::MBI_SSL", "response_type", "device_code"));
		if (authResponse.StatusCode != 200)
		{
			throw new AuthError($"获取设备码失败 (HTTP {authResponse.StatusCode})，请检查微软登录配置。");
		}
		PyDict code = ReadObject(authResponse, "微软登录");
		return PyDict.Of("device_code", RequiredText(code, "device_code", "微软登录"),
			"user_code", RequiredText(code, "user_code", "微软登录"),
			"verification_uri", RequiredText(code, "verification_uri", "微软登录"),
			"expires_in", (long)PositiveSeconds(code["expires_in"], "微软登录"),
			"interval", (long)PositiveSeconds(code.Get("interval", 5L), "微软登录"));
	}

	public PyDict PollToken(object device_code, long interval = 5L, long expires_in = 900L, Action<string> on_status = null)
	{
		interval = Math.Max(1, interval);
		double num = Py.Monotonic() + Math.Max(0, expires_in);
		while (Py.Monotonic() < num)
		{
			AuthResponse authResponse = PostForm("https://login.live.com/oauth20_token.srf", PyDict.Of("grant_type", "urn:ietf:params:oauth:grant-type:device_code", "client_id", client_id, "device_code", device_code));
			PyDict obj = ReadObject(authResponse, "微软授权");
			if (authResponse.StatusCode == 200)
			{
				return PyDict.Of("access_token", RequiredText(obj, "access_token", "微软授权"), "refresh_token", obj.Get("refresh_token"));
			}
			object obj2 = obj.Get("error", "");
			if (Py.Eq(obj2, "authorization_pending"))
			{
				on_status?.Invoke("等待授权中…");
			}
			else
			{
				if (!Py.Eq(obj2, "slow_down"))
				{
					if (Py.Eq(obj2, "expired_token") || Py.Eq(obj2, "authorization_declined") || Py.Eq(obj2, "bad_verification_code"))
					{
						throw new AuthError("授权已过期或被拒绝，请重试。");
					}
					throw new AuthError($"微软授权失败 (HTTP {authResponse.StatusCode})，请重新尝试登录。");
				}
				interval += 5;
			}
			Wait(TimeSpan.FromSeconds(Math.Min(interval, Math.Max(0, num - Py.Monotonic()))));
		}
		throw new AuthError("授权超时。");
	}

	private void Wait(TimeSpan duration)
	{
		cancellation.ThrowIfCancellationRequested();
		if (cancellation.WaitHandle.WaitOne(duration)) cancellation.ThrowIfCancellationRequested();
	}

	public object XblAuthenticate(object ms_token0)
	{
		string text = Py.Str(ms_token0);
		// login.live.com 设备码流程拿到的令牌要以 "d=" 开头，先试它，省掉一次必然失败的往返
		List<string> obj = ((text.StartsWith("d=", StringComparison.Ordinal) || text.StartsWith("t=", StringComparison.Ordinal)) ? new List<string> { text } : new List<string>
		{
			"d=" + text,
			text
		});
		AuthResponse authResponse = null;
		foreach (string item in obj)
		{
			authResponse = PostJson("https://user.auth.xboxlive.com/user/authenticate", PyDict.Of("Properties", PyDict.Of("AuthMethod", "RPS", "SiteName", "user.auth.xboxlive.com", "RpsTicket", item), "RelyingParty", "http://auth.xboxlive.com", "TokenType", "JWT"));
			if (authResponse.StatusCode == 200)
			{
				return RequiredText(ReadObject(authResponse, "Xbox Live"), "Token", "Xbox Live");
			}
			if (authResponse.StatusCode != 400 && authResponse.StatusCode != 401)
				break;
		}
		throw new AuthError($"Xbox Live 认证失败 (HTTP {authResponse.StatusCode})");
	}

	public object[] XstsAuthenticate(object xbl_token)
	{
		AuthResponse authResponse = PostJson("https://xsts.auth.xboxlive.com/xsts/authorize", PyDict.Of("Properties", PyDict.Of("SandboxId", "RETAIL", "UserTokens", new List<object> { xbl_token }), "RelyingParty", "rp://api.minecraftservices.com/", "TokenType", "JWT"));
		if (authResponse.StatusCode == 401)
		{
			object obj = ReadObject(authResponse, "XSTS").Get("XErr");
			string value = null;
			if (obj is long key)
			{
				Auth.XSTS_ERRORS.TryGetValue(key, out value);
			}
			else if (obj is bool flag)
			{
				Auth.XSTS_ERRORS.TryGetValue(flag ? 1 : 0, out value);
			}
			else if (obj is double num && num == Math.Floor(num) && Math.Abs(num) < 9E+18)
			{
				Auth.XSTS_ERRORS.TryGetValue((long)num, out value);
			}
			throw new AuthError("XSTS 认证失败: " + (value ?? ("未知错误 " + Py.Str(obj))));
		}
		if (authResponse.StatusCode != 200)
		{
			throw new AuthError($"XSTS 认证失败 (HTTP {authResponse.StatusCode})");
		}
		PyDict claims = ReadObject(authResponse, "XSTS");
		string token = RequiredText(claims, "Token", "XSTS");
		if (!(claims.Get("DisplayClaims") is PyDict display) || !(display.Get("xui") is List<object> users)
			|| users.Count == 0 || !(users[0] is PyDict user))
			throw new AuthError("XSTS 响应缺少玩家身份，请稍后重试。");
		return new object[] { token, RequiredText(user, "uhs", "XSTS") };
	}

	public object McLogin(object uhs, object xsts_token)
	{
		return MinecraftToken(uhs, xsts_token)["access_token"];
	}

	private PyDict MinecraftToken(object uhs, object xsts_token)
	{
		AuthResponse authResponse = PostJson("https://api.minecraftservices.com/authentication/login_with_xbox", PyDict.Of("identityToken", "XBL3.0 x=" + Py.Str(uhs) + ";" + Py.Str(xsts_token)));
		if (authResponse.StatusCode != 200)
		{
			throw new AuthError($"Minecraft 登录失败 (HTTP {authResponse.StatusCode})，请稍后重试。");
		}
		PyDict token = ReadObject(authResponse, "Minecraft 登录");
		RequiredText(token, "access_token", "Minecraft 登录");
		token["expires_at"] = Py.Time() + PositiveSeconds(token.Get("expires_in"), "Minecraft 登录");
		return token;
	}

	public bool CheckEntitlements(object mc_token)
	{
		AuthResponse bearer = GetBearer("https://api.minecraftservices.com/entitlements/mcstore", mc_token);
		if (bearer.StatusCode != 200)
		{
			throw new AuthError($"检查正版资格失败 (HTTP {bearer.StatusCode})");
		}
		object o = ReadObject(bearer, "购买资格检查").Get("items");
		if (!(o is List<object>))
			throw new AuthError("购买资格检查返回了无效响应，请稍后重试。");
		if (!Py.Truthy(o))
		{
			throw new AuthError("该账号尚未购买正版 Minecraft，或 Xbox Game Pass 已到期。");
		}
		List<object> list = new List<object>();
		foreach (object item2 in Py.ListCtor(o))
		{
			if (item2 is PyDict pyDict)
			{
				object n = pyDict.Get("name");
				if (!list.Any((object x) => Py.Eq(x, n)))
				{
					list.Add(n);
				}
			}
		}
		if (!list.Any((object obj) => obj is string item && Auth._OWNED_ITEMS.Contains(item)))
		{
			Utils.log.Warning("entitlements 未包含已知 Java 项，改由档案接口判定: %s", "{" + string.Join(", ", list.Select(Py.Repr)) + "}");
		}
		return true;
	}

	public PyDict GetProfile(object mc_token)
	{
		AuthResponse bearer = GetBearer("https://api.minecraftservices.com/minecraft/profile", mc_token);
		if (bearer.StatusCode == 404)
		{
			throw new AuthError("该账号尚未创建 Minecraft 档案（没有游戏角色名）。");
		}
		if (bearer.StatusCode != 200)
		{
			throw new AuthError($"获取玩家档案失败 (HTTP {bearer.StatusCode})");
		}
		PyDict profile = ReadObject(bearer, "玩家档案");
		if (!Guid.TryParse(RequiredText(profile, "id", "玩家档案"), out Guid uuid))
			throw new AuthError("玩家档案返回了无效的 UUID，请稍后重试。");
		return PyDict.Of("uuid", uuid.ToString(), "name", RequiredText(profile, "name", "玩家档案"));
	}

	public PyDict Login(Action<object, object, object> on_code = null, Action<string> on_status = null, bool open_browser = true)
	{
		PyDict deviceCode = GetDeviceCode();
		AuthHttp.Warmup("https://user.auth.xboxlive.com/", "https://xsts.auth.xboxlive.com/", "https://api.minecraftservices.com/");
		if (open_browser)
		{
			try
			{
				Auth.OpenBrowser(Py.Str(deviceCode["verification_uri"]));
			}
			catch (Exception error)
			{
				Utils.log.Warning("无法打开微软授权页面: %s", error.GetType().Name);
			}
		}
		on_code?.Invoke(deviceCode["user_code"], deviceCode["verification_uri"], deviceCode["expires_in"]);
		PyDict pyDict = PollToken(deviceCode["device_code"], (long)deviceCode["interval"], (long)deviceCode["expires_in"], on_status);
		object xbl_token = XblAuthenticate(pyDict["access_token"]);
		object[] array = XstsAuthenticate(xbl_token);
		PyDict minecraft = MinecraftToken(array[1], array[0]);
		object obj = minecraft["access_token"];
		System.Threading.Tasks.Task<bool> entitlements = System.Threading.Tasks.Task.Run(() => CheckEntitlements(obj));
		// 正版资格与玩家档案互不依赖，并行请求；报错优先级与原先串行时一致（先资格后档案）
		PyDict profile = null;
		Exception profileError = null;
		try
		{
			profile = GetProfile(obj);
		}
		catch (Exception ex)
		{
			profileError = ex;
		}
		entitlements.GetAwaiter().GetResult();
		if (profileError != null)
		{
			System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(profileError).Throw();
		}
		return PyDict.Of("type", "microsoft", "name", profile["name"], "uuid", profile["uuid"], "access_token", obj, "refresh_token", pyDict.Get("refresh_token"), "xuid", array[1], "expires_at", minecraft["expires_at"], "updated_at", Py.Time());
	}

	public PyDict Refresh(PyDict account)
	{
		if (!Py.Truthy(account.Get("refresh_token")))
		{
			throw new AuthError("缺少刷新令牌，需要重新登录。");
		}
		AuthResponse authResponse = PostForm("https://login.live.com/oauth20_token.srf", PyDict.Of("grant_type", "refresh_token", "client_id", client_id, "refresh_token", account.Req("refresh_token"), "scope", "service::user.auth.xboxlive.com::MBI_SSL"));
		if (authResponse.StatusCode != 200)
		{
			PyDict failure = ReadObject(authResponse, "刷新令牌");
			string code = failure.Get("error") as string;
			if (code == "invalid_grant" || code == "interaction_required" || code == "consent_required")
				throw new AuthError("登录授权已失效，请重新登录微软账号。");
			if (code == "invalid_client" || code == "unauthorized_client")
				throw new AuthError("微软登录配置无效，请检查客户端 ID。");
			throw new AuthError($"刷新令牌失败 (HTTP {authResponse.StatusCode})，请稍后重试。");
		}
		PyDict obj = ReadObject(authResponse, "刷新令牌");
		object ms_token = RequiredText(obj, "access_token", "刷新令牌");
		object obj2 = Py.Or(obj.Get("refresh_token"), account.Req("refresh_token"));
		object xbl_token = XblAuthenticate(ms_token);
		object[] array = XstsAuthenticate(xbl_token);
		PyDict minecraft = MinecraftToken(array[1], array[0]);
		object obj3 = minecraft["access_token"];
		PyDict profile = GetProfile(obj3);
		account = new PyDict(account);
		account.Update(PyDict.Of("name", profile["name"], "uuid", profile["uuid"], "access_token", obj3, "refresh_token", obj2, "xuid", array[1], "expires_at", minecraft["expires_at"], "updated_at", Py.Time()));
		return account;
	}
}
