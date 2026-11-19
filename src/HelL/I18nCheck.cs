using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using HelL.Services;

namespace HelL;

public static class I18nCheck
{
	public sealed record Literal(string File, int Line, string Value, bool Wrapped, bool Ignored);

	public static int Run(string[] args)
	{
		UTF8Encoding encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
		using StreamWriter streamWriter = new StreamWriter(Console.OpenStandardOutput(), encoding);
		bool flag = args.Contains<string>("--json");
		string text;
		try
		{
			text = Path.Combine(BridgeHost.FindRoot(), "wpf", "HelL");
		}
		catch (Exception ex)
		{
			streamWriter.WriteLine(flag ? JsonSerializer.Serialize(new
			{
				error = ex.Message
			}) : ("找不到仓库根目录：" + ex.Message));
			streamWriter.Flush();
			return 2;
		}
		if (!Directory.Exists(text))
		{
			streamWriter.WriteLine(flag ? JsonSerializer.Serialize(new
			{
				error = "no sources",
				dir = text
			}) : ("没有源码树：" + text));
			streamWriter.Flush();
			return 2;
		}
		List<Literal> list = new List<Literal>();
		int num = 0;
		List<string> list2 = new List<string>();
		foreach (string item in Directory.EnumerateFiles(text, "*.cs", SearchOption.AllDirectories).OrderBy<string, string>((string f) => f, StringComparer.Ordinal))
		{
			string rel = PyPath.GetRelativePath(text, item).Replace('\\', '/');
			if (rel.StartsWith("obj/", StringComparison.Ordinal) || rel.StartsWith("bin/", StringComparison.Ordinal))
			{
				continue;
			}
			num++;
			string text2 = File.ReadAllText(item);
			if (IsFileIgnored(text2))
			{
				list2.Add(rel);
				continue;
			}
			list.AddRange(from l in Scan(text2)
				select l with
				{
					File = rel
				});
		}
		List<Literal> list3 = list.Where((Literal l) => !l.Wrapped && !l.Ignored).ToList();
		List<string> list4 = (from l in list
			where l.Wrapped
			select l.Value).Distinct<string>(StringComparer.Ordinal).OrderBy<string, string>((string k) => k, StringComparer.Ordinal).ToList();
		List<string> list5 = list4.Where((string k) => !I18n.Has("en", k)).ToList();
		List<string> list6 = list4.Where((string k) => !I18n.Has("zh_CN", k)).ToList();
		Dictionary<string, object> dictionary = new Dictionary<string, object>
		{
			["src_dir"] = text,
			["locales_dir"] = I18n.LocalesDir,
			["files"] = num,
			["ignored_files"] = list2,
			["ignored_lines"] = list.Count((Literal l) => l.Ignored && !l.Wrapped),
			["wrapped"] = list.Count((Literal l) => l.Wrapped),
			["keys"] = list4.Count,
			["unwrapped_count"] = list3.Count,
			["unwrapped"] = list3.Select((Literal l) => new
			{
				file = l.File,
				line = l.Line,
				text = l.Value
			}).ToList(),
			["missing_en_count"] = list5.Count,
			["missing_en"] = list5,
			["missing_zh_count"] = list6.Count,
			["missing_zh"] = list6
		};
		int num2 = ((list3.Count != 0 || list5.Count != 0) ? 1 : 0);
		dictionary["exit_code"] = num2;
		if (flag)
		{
			streamWriter.WriteLine(JsonSerializer.Serialize(dictionary, new JsonSerializerOptions
			{
				WriteIndented = true,
				Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
			}));
		}
		else
		{
			streamWriter.WriteLine(string.Format("源码：{0}（{1} 个 .cs，整文件忽略 {2} 个：{3}）", text, num, list2.Count, string.Join(", ", list2)));
			streamWriter.WriteLine("词表：" + ((I18n.LocalesDir.Length > 0) ? I18n.LocalesDir : "（没找到）"));
			streamWriter.WriteLine(string.Format("已包裹 L() {0} 处，去重 {1} 个 key；按行忽略 {2} 处", dictionary["wrapped"], list4.Count, dictionary["ignored_lines"]));
			streamWriter.WriteLine($"未包裹的中文字面量：{list3.Count}");
			foreach (Literal item2 in list3)
			{
				streamWriter.WriteLine($"  {item2.File}:{item2.Line}  {Shorten(item2.Value)}");
			}
			streamWriter.WriteLine($"L() 用到、en 词表缺的 key：{list5.Count}");
			foreach (string item3 in list5)
			{
				streamWriter.WriteLine("  " + Shorten(item3));
			}
			streamWriter.WriteLine($"L() 用到、zh_CN 词表缺的 key：{list6.Count}");
			foreach (string item4 in list6)
			{
				streamWriter.WriteLine("  " + Shorten(item4));
			}
			streamWriter.WriteLine((num2 == 0) ? "OK" : "FAIL");
		}
		streamWriter.Flush();
		return num2;
	}

	private static string Shorten(string s)
	{
		s = s.Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
		if (s.Length <= 80)
		{
			return s;
		}
		return s.Substring(0, 80) + "…";
	}

	public static bool HasCjk(string s)
	{
		foreach (char c in s)
		{
			if ((c >= '一' && c <= '鿿') || (c >= '㐀' && c <= '䶿') || (c >= '\u3000' && c <= '〿') || (c >= '\uff00' && c <= '\uffef'))
			{
				return true;
			}
		}
		return false;
	}

	private static bool IsFileIgnored(string text)
	{
		int num = 0;
		string[] array = text.Split('\n');
		for (int i = 0; i < array.Length; i++)
		{
			if (array[i].Contains("i18n:ignore-file", StringComparison.Ordinal))
			{
				return true;
			}
			if (++num >= 10)
			{
				break;
			}
		}
		return false;
	}

	public static List<Literal> Scan(string text)
	{
		List<Literal> result = new List<Literal>();
		string[] lines = text.Split('\n');
		int[] lineStarts = new int[lines.Length];
		int num = 0;
		for (int i = 0; i < lines.Length; i++)
		{
			lineStarts[i] = num;
			num += lines[i].Length + 1;
		}
		ScanCode(text, 0, text.Length, Emit, stopAtBrace: false);
		return result;
		void Emit(int start, string value)
		{
			if (HasCjk(value))
			{
				result.Add(new Literal("", LineOf(start) + 1, value, IsWrapped(text, start), LineIgnored(start)));
			}
		}
		bool LineIgnored(int offset)
		{
			return lines[LineOf(offset)].Contains("i18n:ignore", StringComparison.Ordinal);
		}
		int LineOf(int offset)
		{
			int num2 = 0;
			int num3 = lineStarts.Length - 1;
			while (num2 < num3)
			{
				int num4 = (num2 + num3 + 1) / 2;
				if (lineStarts[num4] <= offset)
				{
					num2 = num4;
				}
				else
				{
					num3 = num4 - 1;
				}
			}
			return num2;
		}
	}

	private static int ScanCode(string s, int i, int end, Action<int, string> emit, bool stopAtBrace)
	{
		int num = 0;
		while (i < end)
		{
			char c = s[i];
			if (c == '/' && i + 1 < end && s[i + 1] == '/')
			{
				while (i < end && s[i] != '\n')
				{
					i++;
				}
				continue;
			}
			if (c == '/' && i + 1 < end && s[i + 1] == '*')
			{
				int num2 = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
				i = ((num2 < 0) ? end : (num2 + 2));
				continue;
			}
			if (c == '\'')
			{
				i++;
				while (i < end && s[i] != '\'')
				{
					if (s[i] == '\\')
					{
						i++;
					}
					i++;
				}
				i++;
				continue;
			}
			if (c == '"' || (c == '@' && i + 1 < end && (s[i + 1] == '"' || (s[i + 1] == '$' && i + 2 < end && s[i + 2] == '"'))) || (c == '$' && i + 1 < end && (s[i + 1] == '"' || (s[i + 1] == '@' && i + 2 < end && s[i + 2] == '"'))))
			{
				i = ScanString(s, i, end, emit);
				continue;
			}
			if (stopAtBrace)
			{
				switch (c)
				{
				case '(':
				case '[':
				case '{':
					num++;
					break;
				case ')':
				case ']':
					num--;
					break;
				case '}':
					if (num == 0)
					{
						return i;
					}
					num--;
					break;
				}
			}
			i++;
		}
		return i;
	}

	private static int ScanString(string s, int start, int end, Action<int, string> emit)
	{
		int i = start;
		bool flag = false;
		bool flag2 = false;
		for (; i < end && (s[i] == '@' || s[i] == '$'); i++)
		{
			if (s[i] == '@')
			{
				flag = true;
			}
			else
			{
				flag2 = true;
			}
		}
		if (i + 2 < end && s[i] == '"' && s[i + 1] == '"' && s[i + 2] == '"')
		{
			int j;
			for (j = i; j < end && s[j] == '"'; j++)
			{
			}
			int num = j - i;
			string value = new string('"', num);
			int num2 = s.IndexOf(value, j, StringComparison.Ordinal);
			if (num2 < 0)
			{
				num2 = end;
			}
			string text = s.Substring(j, num2 - j);
			if (flag2)
			{
				text = text.Replace("{{", "{").Replace("}}", "}");
			}
			emit(start, text.Trim('\r', '\n', ' '));
			return Math.Min(end, num2 + num);
		}
		i++;
		StringBuilder stringBuilder = new StringBuilder();
		while (i < end)
		{
			char c = s[i];
			if (c == '"')
			{
				if (!flag || i + 1 >= end || s[i + 1] != '"')
				{
					i++;
					break;
				}
				stringBuilder.Append('"');
				i += 2;
			}
			else if (!flag && c == '\\' && i + 1 < end)
			{
				i = Unescape(s, i, stringBuilder);
			}
			else if (flag2 && c == '{')
			{
				if (i + 1 < end && s[i + 1] == '{')
				{
					stringBuilder.Append('{');
					i += 2;
				}
				else
				{
					int num3 = ScanCode(s, i + 1, end, emit, stopAtBrace: true);
					stringBuilder.Append("{}");
					i = Math.Min(end, num3 + 1);
				}
			}
			else if (flag2 && c == '}' && i + 1 < end && s[i + 1] == '}')
			{
				stringBuilder.Append('}');
				i += 2;
			}
			else
			{
				stringBuilder.Append(c);
				i++;
			}
		}
		emit(start, stringBuilder.ToString());
		return i;
	}

	private static int Unescape(string s, int i, StringBuilder sb)
	{
		char c = s[i + 1];
		switch (c)
		{
		case 'n':
			sb.Append('\n');
			return i + 2;
		case 'r':
			sb.Append('\r');
			return i + 2;
		case 't':
			sb.Append('\t');
			return i + 2;
		case '0':
			sb.Append('\0');
			return i + 2;
		case 'a':
			sb.Append('\a');
			return i + 2;
		case 'b':
			sb.Append('\b');
			return i + 2;
		case 'f':
			sb.Append('\f');
			return i + 2;
		case 'v':
			sb.Append('\v');
			return i + 2;
		case 'u':
		{
			if (i + 6 <= s.Length && int.TryParse(s.Substring(i + 2, 4), NumberStyles.HexNumber, null, out var result2))
			{
				sb.Append((char)result2);
				return i + 6;
			}
			break;
		}
		case 'U':
		{
			if (i + 10 <= s.Length && int.TryParse(s.Substring(i + 2, 8), NumberStyles.HexNumber, null, out var result))
			{
				sb.Append(char.ConvertFromUtf32(result));
				return i + 10;
			}
			break;
		}
		case 'x':
		{
			int num = i + 2;
			int num2 = 0;
			while (num < s.Length && num2 < 4 && Uri.IsHexDigit(s[num]))
			{
				num++;
				num2++;
			}
			if (num2 > 0)
			{
				sb.Append((char)Convert.ToInt32(s.Substring(i + 2, num2), 16));
				return num;
			}
			break;
		}
		}
		sb.Append(c);
		return i + 2;
	}

	private static bool IsWrapped(string s, int start)
	{
		int num = start - 1;
		while (num >= 0 && char.IsWhiteSpace(s[num]))
		{
			num--;
		}
		if (num < 0 || s[num] != '(')
		{
			return false;
		}
		num--;
		while (num >= 0 && char.IsWhiteSpace(s[num]))
		{
			num--;
		}
		if (num < 0 || s[num] != 'L')
		{
			return false;
		}
		if (num == 0)
		{
			return true;
		}
		char c = s[num - 1];
		if (char.IsLetterOrDigit(c) || c == '_')
		{
			return c == '.';
		}
		return true;
	}
}
