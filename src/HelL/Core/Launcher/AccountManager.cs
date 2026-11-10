using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace HelL.Core.Launcher;

public class AccountManager
{
	public List<object> accounts = new List<object>();

	public object active;

	public readonly object _sync = new object();
	private readonly object refreshGate = new object();

	public static Func<PyDict, PyDict> MicrosoftRefreshHook = DefaultMicrosoftRefresh;

	public static Func<PyDict, PyDict> AuthlibRefreshHook = Authlib.Refresh;

	public static Func<PyDict, PyDict> Nide8RefreshHook = Nide8.Refresh;

	public AccountManager()
	{
		Load();
	}

	public void Load()
	{
		object obj = Utils.ReadJson(Auth.ACCOUNTS_FILE);
		if (!Py.Truthy(obj))
		{
			obj = new PyDict();
		}
		PyDict pyDict = obj as PyDict;
		if (pyDict == null)
		{
			pyDict = new PyDict();
		}
		List<object> source = (pyDict.Get("accounts", new List<object>()) as List<object>) ?? new List<object>();
		accounts = source.Where((object a) => a is PyDict).Select<object, object>((Func<object, object>)((object a) => Auth.OpenAccount((PyDict)a))).ToList();
		active = pyDict["active"];
	}

	public void Save()
	{
		lock (_sync)
		{
			List<object> list = accounts.Select(a => (object)Auth.SealAccount((PyDict)a)).ToList();
			Utils.WriteJson(Auth.ACCOUNTS_FILE, PyDict.Of("active", active, "accounts", list));
			Auth._restrict_accounts_file();
		}
	}

	public PyDict AddAccount(PyDict account)
	{
		lock (_sync)
		{
			_remove_stored_secrets(account["name"]);
			accounts = accounts.Where(a => !Py.Eq(((PyDict)a)["name"], account["name"])).ToList();
			accounts.Add(account);
			active = account.Req("name");
			Save();
			return account;
		}
	}

	public void RemoveAccount(object name)
	{
		lock (_sync)
		{
			_remove_stored_secrets(name);
			PyDict account = GetAccount(name);
			if (account != null && Py.Truthy(account["skin_file"]))
				Skin.RemoveSkin(account);
			accounts = accounts.Where(a => !Py.Eq(((PyDict)a)["name"], name)).ToList();
			if (Py.Eq(active, name))
				active = accounts.Count > 0 ? ((PyDict)accounts[0]).Req("name") : null;
			Save();
		}
	}

	private void _remove_stored_secrets(object name)
	{
		if (!Py.Truthy(name))
		{
			return;
		}
		object obj = Utils.ReadJson(Auth.ACCOUNTS_FILE, new PyDict());
		if (!Py.Truthy(obj))
		{
			obj = new PyDict();
		}
		if (!(obj is PyDict pyDict) || !(pyDict.Get("accounts", new List<object>()) is List<object> list))
		{
			return;
		}
		foreach (object item in list)
		{
			if (item is PyDict pyDict2 && Py.Eq(pyDict2["name"], name))
			{
				string[] tOKEN_KEYS = Auth._TOKEN_KEYS;
				foreach (string key in tOKEN_KEYS)
				{
					Auth._delete_keyring_reference(Py.Or(pyDict2[key], ""));
				}
			}
		}
	}

	public PyDict GetAccount(object name)
	{
		lock (_sync)
		{
			foreach (PyDict account in accounts)
			{
				if (Py.Eq(account["name"], name))
					return account;
			}
			return null;
		}
	}

	public PyDict GetActive()
	{
		if (Py.Truthy(active))
		{
			PyDict account = GetAccount(active);
			if (account != null)
			{
				return account;
			}
		}
		return null;
	}

	public object SetActive(object name)
	{
		lock (_sync)
		{
			if (Py.Truthy(name) && GetAccount(name) != null)
			{
				active = name;
				Save();
			}
			return active;
		}
	}

	public PyDict OfflineAccount(object username0, object skin0 = null)
	{
		string text = Py.Strip(Py.AsStr(username0, "strip"));
		if (text.Length == 0)
		{
			text = "Player";
		}
		string text2 = Py.Lower(Py.AsStr(Py.Or(skin0, "default"), "lower"));
		string text3 = ((text2 == "steve") ? "8667ba71-b85a-4004-af54-457a9734eed7" : ((!(text2 == "alex")) ? Utils.OfflineUuid(text) : "ec561538-f3fd-461d-a7c9-7aa354f5bba9"));
		return PyDict.Of("type", "offline", "name", text, "uuid", text3, "skin", text2);
	}

	private static PyDict DefaultMicrosoftRefresh(PyDict acc)
	{
		return new MicrosoftAuthenticator(Config.MsClientId()).Refresh(acc);
	}

	private static bool Expired(PyDict account)
	{
		return !double.TryParse(Py.Str(account.Get("expires_at")), NumberStyles.Float, CultureInfo.InvariantCulture, out double expiry)
			|| double.IsNaN(expiry) || double.IsInfinity(expiry) || expiry <= Py.Time() + 60;
	}

	public PyDict EnsureValid(PyDict account)
	{
		if (!Py.Truthy(account))
			return account;
		lock (refreshGate)
		{
			object originalName = account.Get("name");
			PyDict stored = GetAccount(originalName);
			account = stored ?? account;
			string type = account.Get("type") as string;
			Func<PyDict, PyDict> refresh;
			switch (type)
			{
			case "microsoft": refresh = MicrosoftRefreshHook ?? DefaultMicrosoftRefresh; break;
			case "authlib": refresh = AuthlibRefreshHook ?? Authlib.Refresh; break;
			case "nide8": refresh = Nide8RefreshHook ?? Nide8.Refresh; break;
			default: return account;
			}
			if (!Expired(account) && Py.Truthy(account.Get("access_token")))
				return account;
			if (type == "microsoft" && !Py.Truthy(account.Get("refresh_token")))
				throw new AuthError("正版令牌已过期且无法刷新，请重新登录。");

			PyDict refreshed = refresh(new PyDict(account));
			if (refreshed == null || !(refreshed.Get("name") is string name) || string.IsNullOrWhiteSpace(name)
				|| !(refreshed.Get("access_token") is string token) || string.IsNullOrWhiteSpace(token))
				throw new AuthError("刷新账号返回了无效响应，请稍后重试。");
			lock (_sync)
			{
				if (stored != null && !ReferenceEquals(GetAccount(originalName), stored))
					throw new AuthError("账号在刷新期间已被修改，请重新选择账号。");
				if (!Py.Eq(name, originalName) && GetAccount(name) != null)
					throw new AuthError("刷新后的角色名与已有账号冲突，请先处理同名账号。");
				List<object> previous = accounts;
				object previousActive = active;
				accounts = accounts.Where(item => !Py.Eq(((PyDict)item)["name"], originalName)).ToList();
				accounts.Add(refreshed);
				if (Py.Eq(active, originalName) || !Py.Truthy(active))
					active = name;
				try
				{
					Save();
				}
				catch
				{
					accounts = previous;
					active = previousActive;
					throw;
				}
				// Callers may still hold the original object, including its old player name.
				account.Update(refreshed);
				accounts[accounts.Count - 1] = account;
				return account;
			}
		}
	}

	public PyDict LaunchProps(PyDict account)
	{
		object a = account.Get("type");
		if (Py.Eq(a, "microsoft"))
		{
			return PyDict.Of("name", account.Req("name"), "uuid", Utils.DashedUuid(Py.Str(Py.Or(account.Get("uuid"), ""))), "token", Py.Or(account.Get("access_token"), "0"), "user_type", "msa", "xuid", Py.Or(account.Get("xuid"), ""));
		}
		if (Py.Eq(a, "authlib"))
		{
			return PyDict.Of("name", account.Get("name", "Player"), "uuid", Utils.DashedUuid(Py.Str(Py.Or(account.Get("uuid"), ""))), "token", Py.Or(account.Get("access_token"), "0"), "user_type", "mojang", "xuid", "", "authlib_api", Py.Or(account.Get("api"), ""));
		}
		if (Py.Eq(a, "nide8"))
		{
			return PyDict.Of("name", account.Get("name", "Player"), "uuid", Utils.DashedUuid(Py.Str(Py.Or(account.Get("uuid"), ""))), "token", Py.Or(account.Get("access_token"), "0"), "user_type", "mojang", "xuid", "", "nide8_id", Py.Or(account.Get("server_id"), ""));
		}
		object obj = account.Get("name", "Player");
		return PyDict.Of("name", obj, "uuid", Py.Or(Utils.DashedUuid(Py.Str(Py.Or(account.Get("uuid"), ""))), Utils.OfflineUuid(Py.Str(obj))), "token", "0", "user_type", "legacy", "xuid", "", "skin_file", Py.Or(account.Get("skin_file"), ""), "skin_model", Py.Or(account.Get("skin_model"), ""));
	}
}
