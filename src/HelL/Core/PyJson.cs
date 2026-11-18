using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace HelL.Core;

public static class PyJson
{
	private sealed class Parser
	{
		private readonly string _s;

		public Parser(string s)
		{
			_s = s;
		}

		public int SkipWs(int i)
		{
			while (i < _s.Length && (_s[i] == ' ' || _s[i] == '\t' || _s[i] == '\n' || _s[i] == '\r'))
			{
				i++;
			}
			return i;
		}

		public (object, int) Value(int i)
		{
			if (i >= _s.Length)
			{
				throw new JSONDecodeError("Expecting value", _s, i);
			}
			char c = _s[i];
			switch (c)
			{
			case '{':
				return Obj(i + 1);
			case '[':
				return Arr(i + 1);
			case '"':
				return Str(i + 1);
			case 'n':
				if (Match(i, "null"))
				{
					return (null, i + 4);
				}
				break;
			case 't':
				if (Match(i, "true"))
				{
					return (true, i + 4);
				}
				break;
			case 'f':
				if (Match(i, "false"))
				{
					return (false, i + 5);
				}
				break;
			case 'N':
				if (Match(i, "NaN"))
				{
					return (double.NaN, i + 3);
				}
				break;
			case 'I':
				if (Match(i, "Infinity"))
				{
					return (double.PositiveInfinity, i + 8);
				}
				break;
			case '-':
				if (Match(i, "-Infinity"))
				{
					return (double.NegativeInfinity, i + 9);
				}
				break;
			}
			switch (c)
			{
			case '-':
			case '0':
			case '1':
			case '2':
			case '3':
			case '4':
			case '5':
			case '6':
			case '7':
			case '8':
			case '9':
			{
				(object, int) result = Num(i);
				if (result.Item2 > i)
				{
					return result;
				}
				break;
			}
			}
			throw new JSONDecodeError("Expecting value", _s, i);
		}

		private bool Match(int i, string w)
		{
			if (string.CompareOrdinal(_s, i, w, 0, w.Length) == 0)
			{
				return i + w.Length <= _s.Length;
			}
			return false;
		}

		private (object, int) Num(int i)
		{
			int num = i;
			if (_s[i] == '-')
			{
				i++;
			}
			if (i >= _s.Length)
			{
				return (null, num);
			}
			if (_s[i] == '0')
			{
				i++;
			}
			else
			{
				if (_s[i] < '1' || _s[i] > '9')
				{
					return (null, num);
				}
				while (i < _s.Length && char.IsDigit(_s[i]) && _s[i] < '\u0080')
				{
					i++;
				}
			}
			bool flag = false;
			if (i + 1 < _s.Length && _s[i] == '.' && _s[i + 1] >= '0' && _s[i + 1] <= '9')
			{
				flag = true;
				i += 2;
				while (i < _s.Length && _s[i] >= '0' && _s[i] <= '9')
				{
					i++;
				}
			}
			if (i < _s.Length && (_s[i] == 'e' || _s[i] == 'E'))
			{
				int j = i + 1;
				if (j < _s.Length && (_s[j] == '+' || _s[j] == '-'))
				{
					j++;
				}
				if (j < _s.Length && _s[j] >= '0' && _s[j] <= '9')
				{
					for (; j < _s.Length && _s[j] >= '0' && _s[j] <= '9'; j++)
					{
					}
					flag = true;
					i = j;
				}
			}
			string text = _s.Substring(num, i - num);
			if (flag)
			{
				return (double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture), i);
			}
			if (long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var result))
			{
				return (result, i);
			}
			return (BigInteger.Parse(text, CultureInfo.InvariantCulture), i);
		}

		private (object, int) Str(int i)
		{
			int pos = i - 1;
			StringBuilder stringBuilder = new StringBuilder();
			while (true)
			{
				if (i >= _s.Length)
				{
					throw new JSONDecodeError("Unterminated string starting at", _s, pos);
				}
				char c = _s[i];
				if (c == '"')
				{
					return (stringBuilder.ToString(), i + 1);
				}
				if (c == '\\')
				{
					if (i + 1 >= _s.Length)
					{
						throw new JSONDecodeError("Unterminated string starting at", _s, pos);
					}
					switch (_s[i + 1])
					{
					case '"':
						stringBuilder.Append('"');
						break;
					case '\\':
						stringBuilder.Append('\\');
						break;
					case '/':
						stringBuilder.Append('/');
						break;
					case 'b':
						stringBuilder.Append('\b');
						break;
					case 'f':
						stringBuilder.Append('\f');
						break;
					case 'n':
						stringBuilder.Append('\n');
						break;
					case 'r':
						stringBuilder.Append('\r');
						break;
					case 't':
						stringBuilder.Append('\t');
						break;
					case 'u':
					{
						if (i + 6 > _s.Length || !int.TryParse(_s.Substring(i + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var result))
						{
							throw new JSONDecodeError("Invalid \\uXXXX escape", _s, i + 1);
						}
						stringBuilder.Append((char)result);
						i += 4;
						break;
					}
					default:
						throw new JSONDecodeError("Invalid \\escape", _s, i);
					}
					i += 2;
				}
				else
				{
					if (c < ' ')
					{
						break;
					}
					stringBuilder.Append(c);
					i++;
				}
			}
			throw new JSONDecodeError("Invalid control character at", _s, i);
		}

		private (object, int) Obj(int i)
		{
			PyDict pyDict = new PyDict();
			i = SkipWs(i);
			if (i < _s.Length && _s[i] == '}')
			{
				return (pyDict, i + 1);
			}
			int i2;
			do
			{
				if (i >= _s.Length || _s[i] != '"')
				{
					throw new JSONDecodeError("Expecting property name enclosed in double quotes", _s, i);
				}
				(object, int) tuple = Str(i + 1);
				object item = tuple.Item1;
				int item2 = tuple.Item2;
				item2 = SkipWs(item2);
				if (item2 >= _s.Length || _s[item2] != ':')
				{
					throw new JSONDecodeError("Expecting ':' delimiter", _s, item2);
				}
				item2 = SkipWs(item2 + 1);
				object value;
				(value, i2) = Value(item2);
				pyDict[(string)item] = value;
				i2 = SkipWs(i2);
				if (i2 < _s.Length && _s[i2] == '}')
				{
					return (pyDict, i2 + 1);
				}
				if (i2 >= _s.Length || _s[i2] != ',')
				{
					throw new JSONDecodeError("Expecting ',' delimiter", _s, i2);
				}
				i = SkipWs(i2 + 1);
			}
			while (i >= _s.Length || _s[i] != '}');
			throw new JSONDecodeError("Illegal trailing comma before end of object", _s, i2);
		}

		private (object, int) Arr(int i)
		{
			List<object> list = new List<object>();
			i = SkipWs(i);
			if (i < _s.Length && _s[i] == ']')
			{
				return (list, i + 1);
			}
			int i2;
			do
			{
				object item;
				(item, i2) = Value(i);
				list.Add(item);
				i2 = SkipWs(i2);
				if (i2 < _s.Length && _s[i2] == ']')
				{
					return (list, i2 + 1);
				}
				if (i2 >= _s.Length || _s[i2] != ',')
				{
					throw new JSONDecodeError("Expecting ',' delimiter", _s, i2);
				}
				i = SkipWs(i2 + 1);
			}
			while (i >= _s.Length || _s[i] != ']');
			throw new JSONDecodeError("Illegal trailing comma before end of array", _s, i2);
		}
	}

	public static string Dumps(object obj, int? indent = null, bool ensureAscii = true, bool sortKeys = false, string itemSep = null, string keySep = null)
	{
		StringBuilder stringBuilder = new StringBuilder();
		string isep = itemSep ?? (indent.HasValue ? "," : ", ");
		string ksep = keySep ?? ": ";
		Write(stringBuilder, obj, indent, ensureAscii, sortKeys, isep, ksep, 0);
		return stringBuilder.ToString();
	}

	public static string DumpsU(object obj, int? indent = null)
	{
		return Dumps(obj, indent, ensureAscii: false);
	}

	private static void Write(StringBuilder sb, object o, int? indent, bool ascii, bool sortKeys, string isep, string ksep, int level)
	{
		if (o != null)
		{
			if (!(o is bool flag))
			{
				if (!(o is long num))
				{
					if (!(o is int num2))
					{
						if (!(o is BigInteger bigInteger))
						{
							if (!(o is double d))
							{
								if (!(o is float num3))
								{
									if (!(o is string s))
									{
										if (!(o is PyDict pyDict))
										{
											if (!(o is IDictionary<string, object> pairs))
											{
												IEnumerable obj = (o as IEnumerable) ?? throw new TypeError("Object of type " + o.GetType().Name + " is not JSON serializable");
												List<object> list = new List<object>();
												foreach (object item in obj)
												{
													list.Add(item);
												}
												if (list.Count == 0)
												{
													sb.Append("[]");
													return;
												}
												sb.Append('[');
												for (int i = 0; i < list.Count; i++)
												{
													if (i > 0)
													{
														sb.Append(isep);
													}
													NewLine(sb, indent, level + 1);
													Write(sb, list[i], indent, ascii, sortKeys, isep, ksep, level + 1);
												}
												NewLine(sb, indent, level);
												sb.Append(']');
											}
											else
											{
												Write(sb, new PyDict(pairs), indent, ascii, sortKeys, isep, ksep, level);
											}
											return;
										}
										if (pyDict.Count == 0)
										{
											sb.Append("{}");
											return;
										}
										sb.Append('{');
										IEnumerable<string> enumerable = pyDict.Keys;
										if (sortKeys)
										{
											List<string> list2 = new List<string>(pyDict.Keys);
											list2.Sort(string.CompareOrdinal);
											enumerable = list2;
										}
										bool flag2 = true;
										foreach (string item2 in enumerable)
										{
											if (!flag2)
											{
												sb.Append(isep);
											}
											flag2 = false;
											NewLine(sb, indent, level + 1);
											WriteStr(sb, item2, ascii);
											sb.Append(ksep);
											Write(sb, pyDict[item2], indent, ascii, sortKeys, isep, ksep, level + 1);
										}
										NewLine(sb, indent, level);
										sb.Append('}');
									}
									else
									{
										WriteStr(sb, s, ascii);
									}
								}
								else
								{
									sb.Append(FloatStr(num3));
								}
							}
							else
							{
								sb.Append(FloatStr(d));
							}
						}
						else
						{
							sb.Append(bigInteger.ToString(CultureInfo.InvariantCulture));
						}
					}
					else
					{
						sb.Append(num2.ToString(CultureInfo.InvariantCulture));
					}
				}
				else
				{
					sb.Append(num.ToString(CultureInfo.InvariantCulture));
				}
			}
			else
			{
				sb.Append(flag ? "true" : "false");
			}
		}
		else
		{
			sb.Append("null");
		}
	}

	private static void NewLine(StringBuilder sb, int? indent, int level)
	{
		if (indent.HasValue)
		{
			sb.Append('\n');
			sb.Append(' ', indent.Value * level);
		}
	}

	public static string FloatStr(double d)
	{
		if (double.IsNaN(d))
		{
			return "NaN";
		}
		if (double.IsPositiveInfinity(d))
		{
			return "Infinity";
		}
		if (double.IsNegativeInfinity(d))
		{
			return "-Infinity";
		}
		return Py.FloatRepr(d);
	}

	public static void WriteStr(StringBuilder sb, string s, bool ascii)
	{
		sb.Append('"');
		foreach (char c in s)
		{
			switch (c)
			{
			case '"':
				sb.Append("\\\"");
				continue;
			case '\\':
				sb.Append("\\\\");
				continue;
			case '\n':
				sb.Append("\\n");
				continue;
			case '\r':
				sb.Append("\\r");
				continue;
			case '\t':
				sb.Append("\\t");
				continue;
			case '\b':
				sb.Append("\\b");
				continue;
			case '\f':
				sb.Append("\\f");
				continue;
			}
			if (c < ' ' || (ascii && c > '~'))
			{
				StringBuilder stringBuilder = sb.Append("\\u");
				int num = c;
				stringBuilder.Append(num.ToString("x4"));
			}
			else
			{
				sb.Append(c);
			}
		}
		sb.Append('"');
	}

	public static object Loads(string s)
	{
		if (s == null)
		{
			throw new TypeError("the JSON object must be str, bytes or bytearray, not NoneType");
		}
		if (s.Length > 0 && s[0] == '\ufeff')
		{
			throw new JSONDecodeError("Unexpected UTF-8 BOM (decode using utf-8-sig)", s, 0);
		}
		Parser parser = new Parser(s);
		int i = parser.SkipWs(0);
		(object, int) tuple = parser.Value(i);
		object item = tuple.Item1;
		int item2 = tuple.Item2;
		item2 = parser.SkipWs(item2);
		if (item2 != s.Length)
		{
			throw new JSONDecodeError("Extra data", s, item2);
		}
		return item;
	}

	public static object LoadsOr(string s, object dflt)
	{
		try
		{
			return Loads(s);
		}
		catch (PyException)
		{
			return dflt;
		}
	}
}
