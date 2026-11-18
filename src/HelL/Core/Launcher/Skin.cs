using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace HelL.Core.Launcher;

public static class Skin
{
	public const string STEVE = "https://mc-heads.net/avatar/Steve/128";

	public const string BODY = "https://mc-heads.net/body/{0}/180";

	public const string CLASSIC = "classic";

	public const string SLIM = "slim";

	public static string Quote(string s, string safe = "/")
	{
		StringBuilder stringBuilder = new StringBuilder();
		byte[] bytes = Encoding.UTF8.GetBytes(s ?? "");
		for (int i = 0; i < bytes.Length; i++)
		{
			byte b = bytes[i];
			char c = (char)b;
			if (b < 128 && ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || "_.-~".IndexOf(c) >= 0 || safe.IndexOf(c) >= 0))
			{
				stringBuilder.Append(c);
			}
			else
			{
				stringBuilder.Append('%').Append(b.ToString("X2"));
			}
		}
		return stringBuilder.ToString();
	}

	public static string _site_origin(object api)
	{
		string text = Py.RStrip(Py.S(api), "/");
		string[] array = new string[2] { "/api/yggdrasil", "/yggdrasil" };
		foreach (string text2 in array)
		{
			if (text.EndsWith(text2, StringComparison.Ordinal))
			{
				return text.Substring(0, text.Length - text2.Length);
			}
		}
		string text3 = (text.Contains("://") ? text : ("https://" + text));
		int num = text3.IndexOf(':');
		if (num > 0)
		{
			string text4 = text3.Substring(0, num);
			bool flag = char.IsLetter(text4[0]);
			string text5 = text4;
			foreach (char c in text5)
			{
				if ((!char.IsLetterOrDigit(c) && c != '+' && c != '-' && c != '.') || c > '\u007f')
				{
					flag = false;
				}
			}
			if (flag && text3.Substring(num).StartsWith("://"))
			{
				string text6 = text3.Substring(num + 3);
				int num2 = text6.IndexOfAny(new char[3] { '/', '?', '#' });
				string text7 = ((num2 >= 0) ? text6.Substring(0, num2) : text6);
				if (text7.Length > 0)
				{
					return text4.ToLowerInvariant() + "://" + text7;
				}
			}
		}
		return text;
	}

	public static string AvatarUrl(PyDict account)
	{
		PyDict pyDict = account ?? new PyDict();
		string text = Utils.DashedUuid(Py.S(pyDict["uuid"])).Replace("-", "");
		string text2 = Py.Str(Py.Or(pyDict["name"], "Steve"));
		string text3 = Py.Str(Py.Or(pyDict["type"], "offline"));
		if (text3 == "authlib" && Py.Truthy(pyDict["api"]))
		{
			string text4 = _site_origin(pyDict["api"]);
			if (text2.Length > 0)
			{
				return text4 + "/avatar/" + Quote(text2);
			}
			if (text.Length > 0)
			{
				return text4 + "/avatar/" + text;
			}
		}
		if (text.Length > 0 && text3 == "microsoft")
		{
			return "https://crafatar.com/avatars/" + text + "?overlay=true&size=128";
		}
		return "https://mc-heads.net/avatar/" + Quote(text2) + "/128";
	}

	public static string BodyUrl(PyDict account)
	{
		PyDict pyDict = account ?? new PyDict();
		string text = Utils.DashedUuid(Py.S(pyDict["uuid"])).Replace("-", "");
		string text2 = Py.Str(Py.Or(pyDict["name"], "Steve"));
		if (Py.Eq(pyDict["type"], "authlib") && Py.Truthy(pyDict["api"]) && text2.Length > 0)
		{
			return _site_origin(pyDict["api"]) + "/preview/" + Quote(text2);
		}
		if (Py.Eq(pyDict["type"], "microsoft") && text.Length > 0)
		{
			return "https://crafatar.com/renders/body/" + text + "?overlay=true&scale=6";
		}
		return Py.Fmt("https://mc-heads.net/body/{0}/180", Quote(text2));
	}

	public static string SteveUrl()
	{
		return "https://mc-heads.net/avatar/Steve/128";
	}

	public static string SkinsDir()
	{
		string text = PPath.Join(Utils.ROOT, "skins");
		Directory.CreateDirectory(PPath.Abs(text));
		return text;
	}

	public static long[] PngSize(byte[] data)
	{
		if (data.Length < 24 || !StartsWith(data, 0, new byte[8] { 137, 80, 78, 71, 13, 10, 26, 10 }) || !StartsWith(data, 12, Encoding.ASCII.GetBytes("IHDR")))
		{
			throw new SkinError("这不是一个有效的 PNG 文件");
		}
		long num = (long)(((ulong)data[16] << 24) | ((ulong)data[17] << 16) | ((ulong)data[18] << 8) | data[19]);
		long num2 = (long)(((ulong)data[20] << 24) | ((ulong)data[21] << 16) | ((ulong)data[22] << 8) | data[23]);
		return new long[2] { num, num2 };
	}

	private static bool StartsWith(byte[] data, int off, byte[] prefix)
	{
		for (int i = 0; i < prefix.Length; i++)
		{
			if (data[off + i] != prefix[i])
			{
				return false;
			}
		}
		return true;
	}

	public static long[] ValidateSkin(byte[] data)
	{
		long[] array = PngSize(data);
		long num = array[0];
		long num2 = array[1];
		if ((num != 64 || num2 != 64) && (num != 64 || num2 != 32))
		{
			throw new SkinError($"皮肤尺寸必须是 64x64 或 64x32，这张是 {num}x{num2}");
		}
		return array;
	}

	private static bool IsAlnum(string s, int i)
	{
		UnicodeCategory unicodeCategory = char.GetUnicodeCategory(s, i);
		if ((uint)unicodeCategory <= 4u || (uint)(unicodeCategory - 8) <= 2u)
		{
			return true;
		}
		return false;
	}

	public static string _dest_for(object account_name)
	{
		string text = (Py.Truthy(account_name) ? Py.Str(account_name) : "player");
		StringBuilder stringBuilder = new StringBuilder();
		for (int num = 0; num < text.Length; num++)
		{
			int num2;
			string text2;
			if (char.IsHighSurrogate(text[num]) && num + 1 < text.Length)
			{
				num2 = (char.IsLowSurrogate(text[num + 1]) ? 1 : 0);
				if (num2 != 0)
				{
					text2 = text.Substring(num, 2);
					goto IL_0074;
				}
			}
			else
			{
				num2 = 0;
			}
			text2 = text[num].ToString();
			goto IL_0074;
			IL_0074:
			string text3 = text2;
			if (IsAlnum(text, num) || text3 == "-" || text3 == "_")
			{
				stringBuilder.Append(text3);
			}
			else
			{
				stringBuilder.Append('_');
			}
			if (num2 != 0)
			{
				num++;
			}
		}
		string text4 = stringBuilder.ToString();
		return PPath.Join(SkinsDir(), ((text4.Length > 0) ? text4 : "player") + ".png");
	}

	public static string SaveSkinBytes(object account_name, byte[] data)
	{
		string text = _dest_for(account_name);
		File.WriteAllBytes(PPath.Abs(text), data);
		return new PPath(text).Name;
	}

	internal static string ExpandUser(string s)
	{
		if (!s.StartsWith("~"))
		{
			return s;
		}
		int num = (Utils.IS_WINDOWS ? s.IndexOfAny(new char[2] { '/', '\\' }) : s.IndexOf('/'));
		if (((num < 0) ? s : s.Substring(0, num)) != "~")
		{
			return s;
		}
		string text = (Utils.IS_WINDOWS ? Environment.GetEnvironmentVariable("USERPROFILE") : Environment.GetEnvironmentVariable("HOME"));
		if (string.IsNullOrEmpty(text))
		{
			text = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		}
		return text + ((num < 0) ? "" : s.Substring(num));
	}

	public static string ImportSkin(object account_name, string src)
	{
		string text = PPath.Norm(ExpandUser(src));
		string text2 = PPath.Abs(text);
		if (!File.Exists(text2))
		{
			throw new SkinError("找不到文件：" + text);
		}
		if (new FileInfo(text2).Length > 2097152)
		{
			throw new SkinError("皮肤文件过大（上限 2 MB）");
		}
		ValidateSkin(File.ReadAllBytes(text2));
		string text3 = _dest_for(account_name);
		string destFileName = PPath.Abs(text3);
		if (!string.Equals(new PPath(text).Resolve().ToString(), new PPath(text3).Resolve().ToString(), StringComparison.Ordinal))
		{
			File.Copy(text2, destFileName, overwrite: true);
		}
		return new PPath(text3).Name;
	}

	public static string SkinFileFor(PyDict account)
	{
		string text = Py.Strip(Py.AsStr(Py.Or((account ?? new PyDict())["skin_file"], ""), "strip"));
		if (text.Length == 0)
		{
			return null;
		}
		string text2 = PPath.Join(SkinsDir(), new PPath(text).Name);
		if (!LocalFs.IsFile(text2))
		{
			return null;
		}
		return text2;
	}

	public static byte[] LoadSkinPng(PyDict account)
	{
		string text = SkinFileFor(account);
		if (text == null)
		{
			return null;
		}
		try
		{
			byte[] array = File.ReadAllBytes(PPath.Abs(text));
			ValidateSkin(array);
			return array;
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is SkinError)
		{
			return null;
		}
	}

	public static string SkinModel(PyDict account)
	{
		if (!(Py.Lower(Py.AsStr(Py.Or((account ?? new PyDict())["skin_model"], ""), "lower")) == "slim"))
		{
			return "classic";
		}
		return "slim";
	}

	public static void RemoveSkin(PyDict account)
	{
		string text = SkinFileFor(account);
		if (text != null)
		{
			try
			{
				File.Delete(PPath.Abs(text));
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
			{
			}
		}
	}
}
