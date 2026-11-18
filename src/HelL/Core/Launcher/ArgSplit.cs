using System.Collections.Generic;
using System.Text;

namespace HelL.Core.Launcher;

public static class ArgSplit
{
	public static List<string> ShlexSplit(string s, bool escape = true)
	{
		List<string> list = new List<string>();
		StringBuilder stringBuilder = new StringBuilder();
		char c = ' ';
		char c2 = 'a';
		bool flag = false;
		bool flag2 = false;
		int num = 0;
		while (true)
		{
			char? c3 = ((num < s.Length) ? new char?(s[num]) : ((char?)null));
			num++;
			switch (c)
			{
			case ' ':
			{
				if (!c3.HasValue)
				{
					break;
				}
				char value2 = c3.Value;
				if (" \t\r\n".IndexOf(value2) < 0)
				{
					if (escape && value2 == '\\')
					{
						c2 = 'a';
						c = '\\';
						flag2 = true;
					}
					else if (value2 == '\'' || value2 == '"')
					{
						c = value2;
						flag2 = true;
					}
					else
					{
						stringBuilder.Append(value2);
						c = 'a';
						flag2 = true;
					}
				}
				continue;
			}
			case '"':
			case '\'':
			{
				flag = true;
				if (!c3.HasValue)
				{
					throw new ValueError("No closing quotation");
				}
				char value4 = c3.Value;
				if (value4 == c)
				{
					c = 'a';
				}
				else if (escape && value4 == '\\' && c == '"')
				{
					c2 = c;
					c = '\\';
				}
				else
				{
					stringBuilder.Append(value4);
				}
				continue;
			}
			case '\\':
			{
				if (!c3.HasValue)
				{
					throw new ValueError("No escaped character");
				}
				char value3 = c3.Value;
				if ((c2 == '\'' || c2 == '"') && value3 != c && value3 != c2)
				{
					stringBuilder.Append(c);
				}
				stringBuilder.Append(value3);
				c = c2;
				continue;
			}
			default:
			{
				if (!c3.HasValue)
				{
					break;
				}
				char value = c3.Value;
				if (" \t\r\n".IndexOf(value) >= 0)
				{
					c = ' ';
					if (stringBuilder.Length > 0 || flag)
					{
						list.Add(stringBuilder.ToString());
					}
					stringBuilder.Clear();
					flag = false;
					flag2 = false;
				}
				else if (value == '\'' || value == '"')
				{
					c = value;
				}
				else if (escape && value == '\\')
				{
					c2 = 'a';
					c = '\\';
				}
				else
				{
					stringBuilder.Append(value);
				}
				continue;
			}
			}
			break;
		}
		if (flag2 && (stringBuilder.Length > 0 || flag))
		{
			list.Add(stringBuilder.ToString());
		}
		return list;
	}

	public static List<string> SplitArgs(object text)
	{
		string text2 = Py.Strip(Py.S(text));
		if (text2.Length == 0)
		{
			return new List<string>();
		}
		try
		{
			List<string> list = new List<string>();
			foreach (string item in ShlexSplit(text2))
			{
				if (item.Length > 0)
				{
					list.Add(item);
				}
			}
			return list;
		}
		catch (ValueError)
		{
			return Py.SplitWs(text2);
		}
	}
}
