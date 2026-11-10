using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace HelL.Core.Launcher.Ai;

public static class Artifacts
{
	public static string ARTIFACTS_DIR;

	public const int HEAD_LINES = 120;

	public const int KEEP_DAYS = 7;

	public const int KEEP_FILES = 300;

	private static bool _pruned;

	private static string _dir()
	{
		string obj = ARTIFACTS_DIR ?? PPath.Join(Utils.ROOT, "cache", "ai_results");
		Utils.EnsureDir(obj);
		return obj;
	}

	public static long Prune(int days = 7, int keep = 300)
	{
		long num = 0L;
		try
		{
			List<string> list = (from p in Directory.EnumerateFiles(PPath.Abs(_dir()), "*.txt")
				orderby File.GetLastWriteTimeUtc(p) descending
				select p).ToList();
			DateTime dateTime = DateTime.UtcNow.AddSeconds((double)(-days) * 86400.0);
			for (int num2 = 0; num2 < list.Count; num2++)
			{
				try
				{
					if (num2 >= keep || File.GetLastWriteTimeUtc(list[num2]) < dateTime)
					{
						File.Delete(list[num2]);
						num++;
					}
				}
				catch (Exception)
				{
				}
			}
		}
		catch (Exception)
		{
		}
		return num;
	}

	private static void _prune_once()
	{
		if (!_pruned)
		{
			_pruned = true;
			Prune();
		}
	}

	private static string _fmt_size(long n)
	{
		if (n >= 1048576)
		{
			return Py.RoundHalfEven((double)n / 1024.0 / 1024.0, 1) + "MB";
		}
		if (n >= 1024)
		{
			return Py.RoundHalfEven((double)n / 1024.0, 1) + "KB";
		}
		return n + "B";
	}

	public static string ArtifactId(string text)
	{
		using SHA1 sHA = SHA1.Create();
		return string.Concat(sHA.ComputeHash(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text ?? "")).Select(delegate(byte b)
		{
			byte b2 = b;
			return b2.ToString("x2");
		})).Substring(0, 8);
	}

	public static string Save(string text, string toolName = "")
	{
		try
		{
			_prune_once();
			string text2 = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + ArtifactId(text) + ".txt";
			AiFs.WriteText(PPath.Join(_dir(), text2), text ?? "");
			return "cache/ai_results/" + text2;
		}
		catch (Exception)
		{
			return "";
		}
	}

	public static string Store(string text, string toolName = "")
	{
		string text2 = Save(text, toolName);
		List<string> list = Py.SplitLines(text ?? "");
		int count = list.Count;
		long n = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetByteCount(text ?? "");
		if (text2.Length == 0)
		{
			return Py.Slice(text, null, 4000) + "\n…(已截断，落盘失败)";
		}
		string text3 = string.Join("\n", list.Take(120));
		return "[结果过长，已存文件] " + text2 + "\n总行数 " + count + " / 总字节 " + _fmt_size(n) + "。以下为前 " + Math.Min(120, count) + " 行：\n" + text3;
	}

	public static string ReadArtifact(object relOrName, object offset = null, object limit = null)
	{
		string[] array = Py.Strip(Py.S(relOrName)).Replace("\\", "/").Split('/');
		string text = array[array.Length - 1];
		if (text.Length == 0 || text.Contains("/") || text.Contains("..") || text.Contains(":"))
		{
			return "无效的 artifact 名";
		}
		PPath pPath = new PPath(_dir()).Resolve();
		PPath pPath2 = (pPath / text).Resolve();
		if (!pPath2.Parent.Equals(pPath))
		{
			return "无效的 artifact 名";
		}
		string s;
		try
		{
			s = AiFs.ReadText(pPath2.ToString());
		}
		catch (Exception)
		{
			return "找不到 artifact: " + text;
		}
		List<string> list = Py.SplitLines(s);
		long num = Math.Max(0L, Py.Int(Py.Or(offset, 0L)));
		long num2 = Math.Min(list.Count, num + Math.Max(1L, Py.Int(Py.Or(limit, 200L))));
		if (num >= list.Count)
		{
			return "起点超出范围（共 " + list.Count + " 行）";
		}
		string text2 = string.Join("\n", list.Skip((int)num).Take((int)(num2 - num)));
		return "[" + text + "] 第 " + (num + 1) + "-" + num2 + "/" + list.Count + " 行：\n" + text2;
	}
}
