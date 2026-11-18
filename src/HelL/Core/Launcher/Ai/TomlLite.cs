using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace HelL.Core.Launcher.Ai;

internal sealed class TomlLite
{
	private readonly string s;

	private int i;

	private char Cur
	{
		get
		{
			if (i >= s.Length)
			{
				return '\0';
			}
			return s[i];
		}
	}

	private TomlLite(string s)
	{
		this.s = s;
	}

	public static PyDict Parse(string text)
	{
		return new TomlLite(text.Replace("\r\n", "\n")).Doc();
	}

	private Exception Err(string m)
	{
		return new FormatException(m + " at " + i);
	}

	private void Ws()
	{
		while (i < s.Length && (s[i] == ' ' || s[i] == '\t'))
		{
			i++;
		}
	}

	private void WsNlComments()
	{
		while (i < s.Length)
		{
			if (s[i] == ' ' || s[i] == '\t' || s[i] == '\n')
			{
				i++;
				continue;
			}
			if (s[i] == '#')
			{
				while (i < s.Length && s[i] != '\n')
				{
					i++;
				}
				continue;
			}
			break;
		}
	}

	private void EndLine()
	{
		Ws();
		if (Cur == '#')
		{
			while (i < s.Length && s[i] != '\n')
			{
				i++;
			}
		}
		if (i < s.Length && s[i] != '\n')
		{
			throw Err("expected newline");
		}
		if (i < s.Length)
		{
			i++;
		}
	}

	private PyDict Doc()
	{
		PyDict pyDict = new PyDict();
		PyDict t = pyDict;
		HashSet<string> hashSet = new HashSet<string>();
		while (true)
		{
			WsNlComments();
			if (this.i >= s.Length)
			{
				break;
			}
			if (Cur == '[')
			{
				bool flag = this.i + 1 < s.Length && s[this.i + 1] == '[';
				this.i += ((!flag) ? 1 : 2);
				Ws();
				List<string> list = Key();
				Ws();
				if (flag)
				{
					if (Cur != ']' || this.i + 1 >= s.Length || s[this.i + 1] != ']')
					{
						throw Err("expected ]]");
					}
					this.i += 2;
				}
				else
				{
					if (Cur != ']')
					{
						throw Err("expected ]");
					}
					this.i++;
				}
				EndLine();
				PyDict pyDict2 = pyDict;
				for (int i = 0; i < list.Count - 1; i++)
				{
					pyDict2 = Descend(pyDict2, list[i]);
				}
				string key = list[list.Count - 1];
				if (flag)
				{
					object obj = pyDict2.Get(key);
					List<object> list3;
					if (obj == null)
					{
						object obj2 = (pyDict2[key] = new List<object>());
						list3 = (List<object>)obj2;
					}
					else
					{
						if (!(obj is List<object> list4) || !list4.All((object x) => x is PyDict))
						{
							throw Err("not an array of tables");
						}
						list3 = list4;
					}
					PyDict pyDict3 = new PyDict();
					list3.Add(pyDict3);
					t = pyDict3;
				}
				else
				{
					string item = string.Join("\u0001", list);
					if (!hashSet.Add(item))
					{
						throw Err("table redefined");
					}
					object obj3 = pyDict2.Get(key);
					if (obj3 == null)
					{
						object obj2 = (pyDict2[key] = new PyDict());
						t = (PyDict)obj2;
					}
					else
					{
						t = (obj3 as PyDict) ?? throw Err("key conflict");
					}
				}
			}
			else
			{
				List<string> keys = Key();
				Ws();
				if (Cur != '=')
				{
					throw Err("expected =");
				}
				this.i++;
				Ws();
				object v = Value();
				Assign(t, keys, v);
				EndLine();
			}
		}
		return pyDict;
	}

	private static PyDict Descend(PyDict t, string k)
	{
		object obj = t.Get(k);
		if (obj == null)
		{
			object obj2 = (t[k] = new PyDict());
			return (PyDict)obj2;
		}
		if (obj is PyDict result)
		{
			return result;
		}
		if (obj is List<object> { Count: >0 } list && list[list.Count - 1] is PyDict result2)
		{
			return result2;
		}
		throw new FormatException("key conflict");
	}

	private void Assign(PyDict t, List<string> keys, object v)
	{
		for (int i = 0; i < keys.Count - 1; i++)
		{
			t = Descend(t, keys[i]);
		}
		string key = keys[keys.Count - 1];
		if (t.ContainsKey(key))
		{
			throw Err("duplicate key");
		}
		t[key] = v;
	}

	private List<string> Key()
	{
		List<string> list = new List<string>();
		while (true)
		{
			Ws();
			if (Cur == '"')
			{
				list.Add(BasicString());
			}
			else if (Cur == '\'')
			{
				list.Add(LiteralString());
			}
			else
			{
				int num = i;
				while (i < s.Length && ((char.IsLetterOrDigit(s[i]) && s[i] < '\u0080') || s[i] == '_' || s[i] == '-'))
				{
					i++;
				}
				if (i == num)
				{
					throw Err("bad key");
				}
				list.Add(s.Substring(num, i - num));
			}
			Ws();
			if (Cur != '.')
			{
				break;
			}
			i++;
		}
		return list;
	}

	private object Value()
	{
		switch (Cur)
		{
		case '"':
			if (s.Length - i >= 3 && s.Substring(i, 3) == "\"\"\"")
			{
				return MultiBasic();
			}
			return BasicString();
		case '\'':
			if (s.Length - i >= 3 && s.Substring(i, 3) == "'''")
			{
				return MultiLiteral();
			}
			return LiteralString();
		case '[':
			return Array();
		case '{':
			return Inline();
		default:
		{
			if (Starts("true"))
			{
				i += 4;
				return true;
			}
			if (Starts("false"))
			{
				i += 5;
				return false;
			}
			int num = i;
			while (i < s.Length && "0123456789+-_.eExobabcdfABCDEF:TZ".IndexOf(s[i]) >= 0)
			{
				i++;
			}
			string text = s.Substring(num, i - num);
			if (text.Length == 0)
			{
				throw Err("bad value");
			}
			if (Regex.IsMatch(text, "^[+-]?(0|[1-9](_?\\d)*)$"))
			{
				return long.Parse(text.Replace("_", ""), CultureInfo.InvariantCulture);
			}
			if (Regex.IsMatch(text, "^[+-]?(0|[1-9](_?\\d)*)(\\.\\d(_?\\d)*)?([eE][+-]?\\d(_?\\d)*)?$"))
			{
				return double.Parse(text.Replace("_", ""), CultureInfo.InvariantCulture);
			}
			throw Err("unsupported value");
		}
		}
	}

	private bool Starts(string w)
	{
		if (s.Length - i >= w.Length)
		{
			return string.CompareOrdinal(s, i, w, 0, w.Length) == 0;
		}
		return false;
	}

	private string BasicString()
	{
		i++;
		StringBuilder stringBuilder = new StringBuilder();
		while (true)
		{
			if (i >= s.Length || s[i] == '\n')
			{
				throw Err("unterminated string");
			}
			char c = s[i++];
			if (c == '"')
			{
				return stringBuilder.ToString();
			}
			if (c == '\\')
			{
				stringBuilder.Append(Escape());
				continue;
			}
			if ((c < ' ' && c != '\t') || c == '\u007f')
			{
				break;
			}
			stringBuilder.Append(c);
		}
		throw Err("control char");
	}

	private string Escape()
	{
		if (i >= s.Length)
		{
			throw Err("bad escape");
		}
		char c = s[i++];
		switch (c)
		{
		case 'b':
			return "\b";
		case 't':
			return "\t";
		case 'n':
			return "\n";
		case 'f':
			return "\f";
		case 'r':
			return "\r";
		case '"':
			return "\"";
		case '\\':
			return "\\";
		case 'U':
		case 'u':
		{
			int num = ((c == 'u') ? 4 : 8);
			if (s.Length - i < num)
			{
				throw Err("bad unicode");
			}
			int utf = int.Parse(s.Substring(i, num), NumberStyles.HexNumber);
			i += num;
			return char.ConvertFromUtf32(utf);
		}
		default:
			throw Err("bad escape");
		}
	}

	private string LiteralString()
	{
		i++;
		int num = i;
		while (i < s.Length && s[i] != '\'' && s[i] != '\n')
		{
			i++;
		}
		if (Cur != '\'')
		{
			throw Err("unterminated literal");
		}
		string result = s.Substring(num, i - num);
		i++;
		return result;
	}

	private string MultiBasic()
	{
		this.i += 3;
		if (Cur == '\n')
		{
			this.i++;
		}
		StringBuilder stringBuilder = new StringBuilder();
		while (true)
		{
			if (this.i >= s.Length)
			{
				throw Err("unterminated");
			}
			if (Starts("\"\"\""))
			{
				break;
			}
			char c = s[this.i++];
			if (c == '\\')
			{
				int num = this.i;
				Ws();
				if (Cur == '\n')
				{
					while (this.i < s.Length && (s[this.i] == ' ' || s[this.i] == '\t' || s[this.i] == '\n'))
					{
						this.i++;
					}
				}
				else
				{
					this.i = num;
					stringBuilder.Append(Escape());
				}
			}
			else
			{
				stringBuilder.Append(c);
			}
		}
		int i;
		for (i = 0; this.i + i < s.Length && s[this.i + i] == '"'; i++)
		{
		}
		if (i > 5)
		{
			throw Err("too many quotes");
		}
		stringBuilder.Append('"', i - 3);
		this.i += i;
		return stringBuilder.ToString();
	}

	private string MultiLiteral()
	{
		i += 3;
		if (Cur == '\n')
		{
			i++;
		}
		int num = i;
		int num2 = s.IndexOf("'''", i, StringComparison.Ordinal);
		if (num2 < 0)
		{
			throw Err("unterminated");
		}
		i = num2 + 3;
		return s.Substring(num, num2 - num);
	}

	private List<object> Array()
	{
		i++;
		List<object> list = new List<object>();
		while (true)
		{
			WsNlComments();
			if (Cur == ']')
			{
				i++;
				return list;
			}
			list.Add(Value());
			WsNlComments();
			if (Cur != ',')
			{
				break;
			}
			i++;
		}
		if (Cur == ']')
		{
			i++;
			return list;
		}
		throw Err("bad array");
	}

	private PyDict Inline()
	{
		i++;
		PyDict pyDict = new PyDict();
		Ws();
		if (Cur == '}')
		{
			i++;
			return pyDict;
		}
		while (true)
		{
			List<string> keys = Key();
			Ws();
			if (Cur != '=')
			{
				throw Err("expected =");
			}
			i++;
			Ws();
			Assign(pyDict, keys, Value());
			Ws();
			if (Cur != ',')
			{
				break;
			}
			i++;
		}
		if (Cur == '}')
		{
			i++;
			return pyDict;
		}
		throw Err("bad inline table");
	}
}
