using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using HelL.Core.Platform;

namespace HelL.Core.Launcher;

public static class Auth
{
	private struct DATA_BLOB
	{
		public int cbData;

		public IntPtr pbData;
	}

	public static readonly string[] _TOKEN_KEYS = new string[2] { "access_token", "refresh_token" };

	public const string _DPAPI_PREFIX = "dpapi:";

	public const string _KEYRING_PREFIX = "keyring:";

	public const string _UNAVAILABLE_PREFIX = "unavailable:";

	public const string _KEYRING_SERVICE = "HelL launcher tokens";

	public const string MS_DEVICE_CODE_URL = "https://login.live.com/oauth20_connect.srf";

	public const string MS_TOKEN_URL = "https://login.live.com/oauth20_token.srf";

	public const string MS_SCOPE = "service::user.auth.xboxlive.com::MBI_SSL";

	public const string XBL_AUTH_URL = "https://user.auth.xboxlive.com/user/authenticate";

	public const string XSTS_AUTH_URL = "https://xsts.auth.xboxlive.com/xsts/authorize";

	public const string MC_LOGIN_URL = "https://api.minecraftservices.com/authentication/login_with_xbox";

	public const string MC_ENTITLEMENTS_URL = "https://api.minecraftservices.com/entitlements/mcstore";

	public const string MC_PROFILE_URL = "https://api.minecraftservices.com/minecraft/profile";

	public static readonly Dictionary<long, string> XSTS_ERRORS = new Dictionary<long, string>
	{
		{ 2148916227L, "账号已被封禁。" },
		{ 2148916233L, "该微软账号没有 Xbox Live 账户，请先注册 Xbox。" },
		{ 2148916235L, "所在地区不支持 Xbox Live。" },
		{ 2148916236L, "需要成年账户才能进行身份验证。" },
		{ 2148916237L, "需要成年账户（家长同意缺失）。" },
		{ 2148916238L, "该账户是儿童账户，需要家长将其添加到家庭组。" }
	};

	public static readonly HashSet<string> _OWNED_ITEMS = new HashSet<string> { "game_minecraft", "product_minecraft", "product_minecraft_java" };

	public static Action<string> OpenBrowserHook;

	public static string ACCOUNTS_FILE => PPath.Join(Utils.ROOT, "accounts.json");

	[DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool CryptProtectData(ref DATA_BLOB pDataIn, string szDataDescr, IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

	[DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool CryptUnprotectData(ref DATA_BLOB pDataIn, IntPtr ppszDataDescr, IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

	[DllImport("kernel32.dll")]
	private static extern IntPtr LocalFree(IntPtr h);

	private static byte[] DpapiCall(byte[] input, bool protect)
	{
		GCHandle gCHandle = GCHandle.Alloc(input, GCHandleType.Pinned);
		try
		{
			DATA_BLOB pDataIn = new DATA_BLOB
			{
				cbData = input.Length,
				pbData = gCHandle.AddrOfPinnedObject()
			};
			DATA_BLOB pDataOut = default(DATA_BLOB);
			bool flag;
			try
			{
				flag = (protect ? CryptProtectData(ref pDataIn, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, ref pDataOut) : CryptUnprotectData(ref pDataIn, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, ref pDataOut));
			}
			catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
			{
				throw new OSError(protect ? "CryptProtectData failed" : "CryptUnprotectData failed");
			}
			if (!flag)
			{
				throw new OSError(protect ? "CryptProtectData failed" : "CryptUnprotectData failed");
			}
			try
			{
				byte[] array = new byte[pDataOut.cbData];
				Marshal.Copy(pDataOut.pbData, array, 0, pDataOut.cbData);
				return array;
			}
			finally
			{
				LocalFree(pDataOut.pbData);
			}
		}
		finally
		{
			gCHandle.Free();
		}
	}

	public static byte[] _dpapi_protect(byte[] plain)
	{
		return DpapiCall(plain, protect: true);
	}

	public static byte[] _dpapi_unprotect(byte[] blob)
	{
		return DpapiCall(blob, protect: false);
	}

	private static ISecretStore _keyring_backend()
	{
		return PlatformServices.SecretStore;
	}

	public static string _keyring_name(string value)
	{
		using SHA256 sHA = SHA256.Create();
		byte[] array = sHA.ComputeHash(Encoding.UTF8.GetBytes(value));
		StringBuilder stringBuilder = new StringBuilder("token-");
		byte[] array2 = array;
		foreach (byte b in array2)
		{
			stringBuilder.Append(b.ToString("x2"));
		}
		return stringBuilder.ToString();
	}

	private static string _store_in_keyring(string value)
	{
		ISecretStore secretStore = _keyring_backend();
		if (secretStore == null)
		{
			return null;
		}
		string text = _keyring_name(value);
		try
		{
			secretStore.SetPassword("HelL launcher tokens", text, value);
		}
		catch (Exception e)
		{
			Utils.log.Warning("系统凭据库不可用，令牌不会写入磁盘: %s", Py.ExcStr(e));
			return null;
		}
		return "keyring:" + text;
	}

	private static string _read_from_keyring(string reference)
	{
		ISecretStore secretStore = _keyring_backend();
		if (secretStore == null)
		{
			Utils.log.Warning("系统凭据库不可用，需要重新登录账号。");
			return "";
		}
		string username = reference.Substring("keyring:".Length);
		try
		{
			return secretStore.GetPassword("HelL launcher tokens", username) ?? "";
		}
		catch (Exception e)
		{
			Utils.log.Warning("读取系统凭据库失败，需要重新登录账号: %s", Py.ExcStr(e));
			return "";
		}
	}

	public static void _delete_keyring_reference(object value)
	{
		string text = Py.S(value);
		if (!text.StartsWith("keyring:", StringComparison.Ordinal))
		{
			return;
		}
		ISecretStore secretStore = _keyring_backend();
		if (secretStore == null)
		{
			return;
		}
		try
		{
			secretStore.DeletePassword("HelL launcher tokens", text.Substring("keyring:".Length));
		}
		catch (Exception)
		{
		}
	}

	private static bool HasSealPrefix(string v)
	{
		if (!v.StartsWith("dpapi:", StringComparison.Ordinal) && !v.StartsWith("keyring:", StringComparison.Ordinal))
		{
			return v.StartsWith("unavailable:", StringComparison.Ordinal);
		}
		return true;
	}

	public static object SealSecret(object value0)
	{
		if (!Py.Truthy(value0))
		{
			return value0;
		}
		string text = Py.Str(value0);
		if (HasSealPrefix(text))
		{
			return text;
		}
		if (Utils.IS_WINDOWS)
		{
			try
			{
				return "dpapi:" + Convert.ToBase64String(_dpapi_protect(Encoding.UTF8.GetBytes(text)));
			}
			catch (OSError oSError)
			{
				Utils.log.Warning("DPAPI 不可用，令牌不会写入磁盘: %s", oSError.Message);
				return "unavailable:";
			}
		}
		return _store_in_keyring(text) ?? "unavailable:";
	}

	public static object OpenSecret(object value0)
	{
		if (!Py.Truthy(value0))
		{
			return value0;
		}
		string text = Py.Str(value0);
		if (text.StartsWith("unavailable:", StringComparison.Ordinal))
		{
			return "";
		}
		if (text.StartsWith("keyring:", StringComparison.Ordinal))
		{
			return _read_from_keyring(text);
		}
		if (!text.StartsWith("dpapi:", StringComparison.Ordinal))
		{
			return text;
		}
		if (!Utils.IS_WINDOWS)
		{
			Utils.log.Warning("当前系统无法解密 Windows DPAPI 令牌，需要重新登录账号。");
			return "";
		}
		try
		{
			byte[] blob = Convert.FromBase64String(text.Substring("dpapi:".Length));
			return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(_dpapi_unprotect(blob));
		}
		catch (Exception ex) when (ex is OSError || ex is FormatException || ex is DecoderFallbackException)
		{
			Utils.log.Warning("无法解密账号令牌，需要重新登录账号。");
			return "";
		}
	}

	public static PyDict SealAccount(PyDict account)
	{
		PyDict pyDict = new PyDict(account ?? new PyDict());
		string[] tOKEN_KEYS = _TOKEN_KEYS;
		foreach (string key in tOKEN_KEYS)
		{
			if (Py.Truthy(pyDict[key]))
			{
				pyDict[key] = SealSecret(pyDict[key]);
			}
		}
		return pyDict;
	}

	public static PyDict OpenAccount(PyDict account)
	{
		PyDict pyDict = new PyDict(account ?? new PyDict());
		string[] tOKEN_KEYS = _TOKEN_KEYS;
		foreach (string key in tOKEN_KEYS)
		{
			if (Py.Truthy(pyDict[key]))
			{
				pyDict[key] = OpenSecret(pyDict[key]);
			}
		}
		return pyDict;
	}

	public static void _restrict_accounts_file()
	{
	}

	internal static void OpenBrowser(string url)
	{
		Action<string> openBrowserHook = OpenBrowserHook;
		if (openBrowserHook != null)
		{
			openBrowserHook(url);
		}
		else if (Utils.IS_WINDOWS)
		{
			Process.Start(new ProcessStartInfo(url)
			{
				UseShellExecute = true
			})?.Dispose();
		}
	}

	internal static object Idx(object obj, object key)
	{
		if (obj is PyDict pyDict)
		{
			if (key is string key2)
			{
				return pyDict.Req(key2);
			}
			throw new KeyError(key);
		}
		if (obj is List<object> list)
		{
			if (key is long num)
			{
				if (num < 0)
				{
					num += list.Count;
				}
				if (num < 0 || num >= list.Count)
				{
					throw new IndexError("list index out of range");
				}
				return list[(int)num];
			}
			throw new TypeError("list indices must be integers or slices, not " + Py.TypeName(key));
		}
		if (obj is string text && key is long num2)
		{
			if (num2 < 0)
			{
				num2 += text.Length;
			}
			if (num2 < 0 || num2 >= text.Length)
			{
				throw new IndexError("string index out of range");
			}
			return text[(int)num2].ToString();
		}
		if (obj is string)
		{
			throw new TypeError("string indices must be integers, not '" + Py.TypeName(key) + "'");
		}
		throw new TypeError("'" + Py.TypeName(obj) + "' object is not subscriptable");
	}
}
