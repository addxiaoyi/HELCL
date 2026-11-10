using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text;

namespace HelL.Core;

public static class Py
{
	public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

	private static readonly char[] PyWs = new char[29]
	{
		' ', '\t', '\n', '\r', '\v', '\f', '\u001c', '\u001d', '\u001e', '\u001f',
		'\u0085', '\u00a0', '\u1680', '\u2000', '\u2001', '\u2002', '\u2003', '\u2004', '\u2005', '\u2006',
		'\u2007', '\u2008', '\u2009', '\u200a', '\u2028', '\u2029', '\u202f', '\u205f', '\u3000'
	};

	private static readonly Stopwatch Mono = Stopwatch.StartNew();

	public static object Box(object o)
	{
		if (o != null)
		{
			if (!(o is int num))
			{
				if (!(o is short num2))
				{
					if (!(o is byte b))
					{
						if (!(o is uint num3))
						{
							if (!(o is float num4))
							{
								if (!(o is decimal num5))
								{
									if (!(o is string) && !(o is long) && !(o is double) && !(o is bool) && !(o is PyDict) && !(o is List<object>))
									{
										if (!(o is Enum obj))
										{
											if (!(o is IDictionary<string, object> dictionary))
											{
												if (o is IEnumerable enumerable && !(o is string))
												{
													List<object> list = new List<object>();
													{
														foreach (object item in enumerable)
														{
															list.Add(Box(item));
														}
														return list;
													}
												}
												return o;
											}
											PyDict pyDict = new PyDict();
											{
												foreach (KeyValuePair<string, object> item2 in dictionary)
												{
													pyDict[item2.Key] = Box(item2.Value);
												}
												return pyDict;
											}
										}
										return obj.ToString();
									}
									return o;
								}
								return (double)num5;
							}
							return (double)num4;
						}
						return (long)num3;
					}
					return (long)b;
				}
				return (long)num2;
			}
			return (long)num;
		}
		return null;
	}

	public static List<object> L(params object[] items)
	{
		List<object> list = new List<object>();
		foreach (object o in items)
		{
			list.Add(Box(o));
		}
		return list;
	}

	public static string TypeName(object o)
	{
		if (o != null)
		{
			if (!(o is bool))
			{
				if (!(o is long) && !(o is int) && !(o is BigInteger))
				{
					if (!(o is double) && !(o is float))
					{
						if (!(o is string))
						{
							if (!(o is PyDict))
							{
								if (!(o is byte[]))
								{
									if (o is IList)
									{
										return "list";
									}
									return o.GetType().Name;
								}
								return "bytes";
							}
							return "dict";
						}
						return "str";
					}
					return "float";
				}
				return "int";
			}
			return "bool";
		}
		return "NoneType";
	}

	public static bool IsInt(object o)
	{
		if (!(o is long))
		{
			return o is int;
		}
		return true;
	}

	public static bool IsNum(object o)
	{
		if (!(o is long) && !(o is int) && !(o is double))
		{
			return o is float;
		}
		return true;
	}

	public static bool IsStr(object o)
	{
		return o is string;
	}

	public static bool IsDict(object o)
	{
		return o is PyDict;
	}

	public static bool IsList(object o)
	{
		return o is List<object>;
	}

	public static bool Truthy(object o)
	{
		if (o != null)
		{
			if (o is bool)
			{
				return (bool)o;
			}
			if (!(o is long num))
			{
				if (!(o is int num2))
				{
					if (!(o is double num3))
					{
						if (!(o is BigInteger bigInteger))
						{
							if (!(o is string text))
							{
								if (!(o is PyDict pyDict))
								{
									if (o is ICollection collection)
									{
										return collection.Count > 0;
									}
									return true;
								}
								return pyDict.Count > 0;
							}
							return text.Length > 0;
						}
						return !bigInteger.IsZero;
					}
					return num3 != 0.0;
				}
				return num2 != 0;
			}
			return num != 0;
		}
		return false;
	}

	public static object Or(object a, object b)
	{
		if (!Truthy(a))
		{
			return b;
		}
		return a;
	}

	public static object Or(object a, object b, object c)
	{
		if (!Truthy(a))
		{
			if (!Truthy(b))
			{
				return c;
			}
			return b;
		}
		return a;
	}

	public static string S(object o)
	{
		if (!Truthy(o))
		{
			return "";
		}
		return Str(o);
	}

	public static string SS(object o)
	{
		return Strip(S(o));
	}

	public static PyDict D(object o)
	{
		return o as PyDict;
	}

	public static List<object> Lst(object o)
	{
		return o as List<object>;
	}

	public static PyDict DictOr(object o)
	{
		return (o as PyDict) ?? new PyDict();
	}

	public static List<object> ListOr(object o)
	{
		if (!(o is List<object> collection))
		{
			return new List<object>();
		}
		return new List<object>(collection);
	}

	public static string Str(object o)
	{
		if (o != null)
		{
			if (!(o is string result))
			{
				if (!(o is bool))
				{
					if (!(o is long num))
					{
						if (!(o is int num2))
						{
							if (!(o is double d))
							{
								if (!(o is float num3))
								{
									if (!(o is PyDict) && !(o is IList))
									{
										if (!(o is PyException ex))
										{
											if (o is Exception e)
											{
												return ExcStr(e);
											}
											return Convert.ToString(o, Inv) ?? "";
										}
										return ex.Message;
									}
									return Repr(o);
								}
								return FloatRepr(num3);
							}
							return FloatRepr(d);
						}
						return num2.ToString(Inv);
					}
					return num.ToString(Inv);
				}
				if (!(bool)o)
				{
					return "False";
				}
				return "True";
			}
			return result;
		}
		return "None";
	}

	public static string Repr(object o)
	{
		if (o != null)
		{
			if (!(o is string s))
			{
				if (!(o is PyDict source))
				{
					if (o is IList list)
					{
						List<string> list2 = new List<string>();
						foreach (object item in list)
						{
							list2.Add(Repr(item));
						}
						return "[" + string.Join(", ", list2) + "]";
					}
					return Str(o);
				}
				return "{" + string.Join(", ", source.Select((KeyValuePair<string, object> kv) => ReprStr(kv.Key) + ": " + Repr(kv.Value))) + "}";
			}
			return ReprStr(s);
		}
		return "None";
	}

	public static string ReprStr(string s)
	{
		char c = ((s.IndexOf('\'') >= 0 && s.IndexOf('"') < 0) ? '"' : '\'');
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append(c);
		for (int i = 0; i < s.Length; i++)
		{
			char c2 = s[i];
			if (c2 == c || c2 == '\\')
			{
				stringBuilder.Append('\\').Append(c2);
				continue;
			}
			if (c2 == '\n')
			{
				stringBuilder.Append("\\n");
				continue;
			}
			if (c2 == '\r')
			{
				stringBuilder.Append("\\r");
				continue;
			}
			if (c2 == '\t')
			{
				stringBuilder.Append("\\t");
				continue;
			}
			int num;
			if (c2 >= ' ')
			{
				switch (c2)
				{
				case '\u0080':
				case '\u0081':
				case '\u0082':
				case '\u0083':
				case '\u0084':
				case '\u0085':
				case '\u0086':
				case '\u0087':
				case '\u0088':
				case '\u0089':
				case '\u008a':
				case '\u008b':
				case '\u008c':
				case '\u008d':
				case '\u008e':
				case '\u008f':
				case '\u0090':
				case '\u0091':
				case '\u0092':
				case '\u0093':
				case '\u0094':
				case '\u0095':
				case '\u0096':
				case '\u0097':
				case '\u0098':
				case '\u0099':
				case '\u009a':
				case '\u009b':
				case '\u009c':
				case '\u009d':
				case '\u009e':
				case '\u009f':
				case '\u00a0':
				{
					StringBuilder stringBuilder4 = stringBuilder.Append("\\x");
					num = c2;
					stringBuilder4.Append(num.ToString("x2"));
					continue;
				}
				default:
				{
					if (char.IsHighSurrogate(c2) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
					{
						stringBuilder.Append(c2).Append(s[i + 1]);
						i++;
						continue;
					}
					if (char.IsSurrogate(c2))
					{
						StringBuilder stringBuilder2 = stringBuilder.Append("\\u");
						num = c2;
						stringBuilder2.Append(num.ToString("x4"));
						continue;
					}
					switch (c2)
					{
					default:
						if (c2 != '\ufeff')
						{
							stringBuilder.Append(c2);
							continue;
						}
						break;
					case '\u00ad':
					case '\u200b':
					case '\u200c':
					case '\u200d':
					case '\u200e':
					case '\u200f':
					case '\u2028':
					case '\u2029':
						break;
					}
					StringBuilder stringBuilder3 = stringBuilder.Append("\\u");
					num = c2;
					stringBuilder3.Append(num.ToString("x4"));
					continue;
				}
				case '\u007f':
					break;
				}
			}
			StringBuilder stringBuilder5 = stringBuilder.Append("\\x");
			num = c2;
			stringBuilder5.Append(num.ToString("x2"));
		}
		stringBuilder.Append(c);
		return stringBuilder.ToString();
	}

	public static string FloatRepr(double d)
	{
		if (double.IsNaN(d))
		{
			return "nan";
		}
		if (double.IsPositiveInfinity(d))
		{
			return "inf";
		}
		if (double.IsNegativeInfinity(d))
		{
			return "-inf";
		}
		if (d == 0.0)
		{
			if (!(1.0 / d < 0.0))
			{
				return "0.0";
			}
			return "-0.0";
		}
		string text = d.ToString("G15", Inv);
		if (double.Parse(text, Inv) != d)
		{
			text = d.ToString("G16", Inv);
		}
		if (double.Parse(text, Inv) != d)
		{
			text = d.ToString("G17", Inv);
		}
		bool flag = text.StartsWith("-");
		if (flag)
		{
			text = text.Substring(1);
		}
		int num = 0;
		int num2 = text.IndexOfAny(new char[2] { 'E', 'e' });
		if (num2 >= 0)
		{
			num = int.Parse(text.Substring(num2 + 1), Inv);
			text = text.Substring(0, num2);
		}
		int num3 = text.IndexOf('.');
		string text2 = ((num3 >= 0) ? text.Substring(0, num3) : text);
		string text3 = ((num3 >= 0) ? text.Substring(num3 + 1) : "");
		string text4 = (text2 + text3).TrimStart('0');
		int num4 = (text2 + text3).Length - text4.Length;
		int num5 = text2.Length + num - num4;
		text4 = text4.TrimEnd('0');
		if (text4.Length == 0)
		{
			text4 = "0";
		}
		int num6 = num5 - 1;
		string text5 = ((num6 < -4 || num6 >= 16) ? (((text4.Length > 1) ? (text4[0] + "." + text4.Substring(1)) : text4) + "e" + ((num6 < 0) ? "-" : "+") + Math.Abs(num6).ToString("00", Inv)) : ((num5 <= 0) ? ("0." + new string('0', -num5) + text4) : ((num5 < text4.Length) ? (text4.Substring(0, num5) + "." + text4.Substring(num5)) : (text4 + new string('0', num5 - text4.Length) + ".0"))));
		if (!flag)
		{
			return text5;
		}
		return "-" + text5;
	}

	public static string ExcStr(Exception e)
	{
		if (!(e is PyException ex))
		{
			if (!(e is TargetInvocationException ex2))
			{
				if (!(e is AggregateException ex3))
				{
					if (e is FileNotFoundException ex4)
					{
						return "[Errno 2] No such file or directory: " + ReprStr(ex4.FileName ?? "");
					}
					if (e is DirectoryNotFoundException ex5)
					{
						return "[Errno 2] No such file or directory: " + ReprStr(ExtractQuoted(ex5.Message));
					}
					if (e is UnauthorizedAccessException ex6)
					{
						return "[Errno 13] Permission denied: " + ReprStr(ExtractQuoted(ex6.Message));
					}
					if (e is NotImplementedException ex7)
					{
						return ex7.Message;
					}
				}
				else if (ex3.InnerExceptions.Count == 1)
				{
					return ExcStr(ex3.InnerExceptions[0]);
				}
			}
			else if (ex2.InnerException != null)
			{
				return ExcStr(ex2.InnerException);
			}
			return e.Message;
		}
		return ex.Message;
	}

	private static string ExtractQuoted(string msg)
	{
		if (msg == null)
		{
			return "";
		}
		int num = msg.IndexOf('\'');
		int num2 = msg.LastIndexOf('\'');
		if (num < 0 || num2 <= num)
		{
			return msg;
		}
		return msg.Substring(num + 1, num2 - num - 1);
	}

	public static long Int(object o)
	{
		if (o is long)
		{
			return (long)o;
		}
		if (!(o is int num))
		{
			if (!(o is bool flag))
			{
				if (!(o is BigInteger bigInteger))
				{
					if (!(o is double d))
					{
						if (!(o is string text))
						{
							if (o == null)
							{
								throw new TypeError("int() argument must be a string, a bytes-like object or a real number, not 'NoneType'");
							}
							throw new TypeError("int() argument must be a string, a bytes-like object or a real number, not '" + TypeName(o) + "'");
						}
						string text2 = text.Trim();
						string text3 = text2.Replace("_", "");
						if (text2.Length > 0 && !text2.StartsWith("_") && !text2.EndsWith("_") && !text2.Contains("__") && long.TryParse(text3, NumberStyles.AllowLeadingSign, Inv, out var result) && !text3.Contains(" "))
						{
							return result;
						}
						throw new ValueError("invalid literal for int() with base 10: " + ReprStr(text));
					}
					if (double.IsNaN(d))
					{
						throw new ValueError("cannot convert float NaN to integer");
					}
					if (double.IsInfinity(d))
					{
						throw new PyException("OverflowError", "cannot convert float infinity to integer");
					}
					return (long)Math.Truncate(d);
				}
				if (bigInteger >= long.MinValue && bigInteger <= long.MaxValue)
				{
					return (long)bigInteger;
				}
				throw new PyException("OverflowError", "Python int too large to convert to C long");
			}
			return flag ? 1 : 0;
		}
		return num;
	}

	public static long IntOr(object o, long fallback)
	{
		try
		{
			return Int(o);
		}
		catch (ValueError)
		{
			return fallback;
		}
		catch (TypeError)
		{
			return fallback;
		}
		catch (PyException)
		{
			return fallback;
		}
	}

	public static double Float(object o)
	{
		if (o is double)
		{
			return (double)o;
		}
		if (!(o is long num))
		{
			if (!(o is int num2))
			{
				if (!(o is bool flag))
				{
					if (!(o is string text))
					{
						if (o == null)
						{
							throw new TypeError("float() argument must be a string or a real number, not 'NoneType'");
						}
						throw new TypeError("float() argument must be a string or a real number, not '" + TypeName(o) + "'");
					}
					string text2 = text.Trim().Replace("_", "").ToLowerInvariant();
					switch (text2)
					{
					case "inf":
					case "+inf":
					case "+infinity":
					case "infinity":
						return double.PositiveInfinity;
					case "-inf":
					case "-infinity":
						return double.NegativeInfinity;
					case "nan":
					case "+nan":
					case "-nan":
						return double.NaN;
					default:
					{
						if (text2.Length > 0 && double.TryParse(text2, NumberStyles.Float, Inv, out var result))
						{
							return result;
						}
						throw new ValueError("could not convert string to float: " + ReprStr(text));
					}
					}
				}
				return flag ? 1 : 0;
			}
			return num2;
		}
		return num;
	}

	public static double FloatOr(object o, double fallback)
	{
		try
		{
			return Float(o);
		}
		catch (PyException)
		{
			return fallback;
		}
	}

	public static bool Eq(object a, object b)
	{
		if (a == null || b == null)
		{
			if (a == null)
			{
				return b == null;
			}
			return false;
		}
		if (IsNumOrBool(a) && IsNumOrBool(b))
		{
			if (ToDouble(a) == ToDouble(b))
			{
				if (a is long num && b is long num2)
				{
					return num == num2;
				}
				return true;
			}
			return false;
		}
		if (a is string a2 && b is string b2)
		{
			return string.Equals(a2, b2, StringComparison.Ordinal);
		}
		if (a is PyDict pyDict && b is PyDict pyDict2)
		{
			if (pyDict.Count != pyDict2.Count)
			{
				return false;
			}
			foreach (KeyValuePair<string, object> item in pyDict)
			{
				if (!pyDict2.TryGetValue(item.Key, out object value) || !Eq(item.Value, value))
				{
					return false;
				}
			}
			return true;
		}
		if (a is IList list && b is IList list2)
		{
			if (list.Count != list2.Count)
			{
				return false;
			}
			for (int i = 0; i < list.Count; i++)
			{
				if (!Eq(list[i], list2[i]))
				{
					return false;
				}
			}
			return true;
		}
		return a.Equals(b);
	}

	private static bool IsNumOrBool(object o)
	{
		if (!(o is long) && !(o is int) && !(o is double) && !(o is bool))
		{
			return o is float;
		}
		return true;
	}

	private static double ToDouble(object o)
	{
		if (!(o is bool flag))
		{
			if (!(o is long num))
			{
				if (!(o is int num2))
				{
					if (o is double)
					{
						return (double)o;
					}
					if (o is float num3)
					{
						return num3;
					}
					return 0.0;
				}
				return num2;
			}
			return num;
		}
		return flag ? 1 : 0;
	}

	public static int Cmp(object a, object b)
	{
		if (IsNumOrBool(a) && IsNumOrBool(b))
		{
			return ToDouble(a).CompareTo(ToDouble(b));
		}
		if (a is string strA && b is string strB)
		{
			int num = string.CompareOrdinal(strA, strB);
			if (num >= 0)
			{
				if (num > 0)
				{
					return 1;
				}
				return 0;
			}
			return -1;
		}
		if (a is IList list && b is IList list2)
		{
			for (int i = 0; i < Math.Min(list.Count, list2.Count); i++)
			{
				int num2 = Cmp(list[i], list2[i]);
				if (num2 != 0)
				{
					return num2;
				}
			}
			return list.Count.CompareTo(list2.Count);
		}
		if (a == null && b == null)
		{
			return 0;
		}
		throw new TypeError("'<' not supported between instances of '" + TypeName(a) + "' and '" + TypeName(b) + "'");
	}

	public static List<T> Sorted<T>(IEnumerable<T> items, Func<T, object> key = null, bool reverse = false)
	{
		List<T> list = items.ToList();
		List<int> list2 = Enumerable.Range(0, list.Count).ToList();
		List<object> keys = Enumerable.Select<T, object>(selector: key ?? ((Func<T, object>)((T x) => x)), source: list).ToList();
		list2.Sort(delegate(int i, int j)
		{
			int num = Cmp(keys[i], keys[j]);
			if (reverse)
			{
				num = -num;
			}
			return (num != 0) ? num : i.CompareTo(j);
		});
		return list2.Select((int i) => list[i]).ToList();
	}

	public static int StrCmp(string a, string b)
	{
		return string.CompareOrdinal(a, b);
	}

	public static string Strip(string s)
	{
		return s?.Trim(PyWs) ?? "";
	}

	public static string LStrip(string s)
	{
		return s?.TrimStart(PyWs) ?? "";
	}

	public static string RStrip(string s)
	{
		return s?.TrimEnd(PyWs) ?? "";
	}

	public static string Strip(string s, string chars)
	{
		return s?.Trim(chars.ToCharArray()) ?? "";
	}

	public static string RStrip(string s, string chars)
	{
		return s?.TrimEnd(chars.ToCharArray()) ?? "";
	}

	public static string LStrip(string s, string chars)
	{
		return s?.TrimStart(chars.ToCharArray()) ?? "";
	}

	public static bool IsPyWs(char c)
	{
		return Array.IndexOf(PyWs, c) >= 0;
	}

	public static List<string> SplitWs(string s)
	{
		List<string> list = new List<string>();
		if (s == null)
		{
			return list;
		}
		StringBuilder stringBuilder = new StringBuilder();
		foreach (char c in s)
		{
			if (IsPyWs(c))
			{
				if (stringBuilder.Length > 0)
				{
					list.Add(stringBuilder.ToString());
					stringBuilder.Clear();
				}
			}
			else
			{
				stringBuilder.Append(c);
			}
		}
		if (stringBuilder.Length > 0)
		{
			list.Add(stringBuilder.ToString());
		}
		return list;
	}

	public static List<string> Split(string s, string sep, int maxsplit = -1)
	{
		List<string> list = new List<string>();
		int num = 0;
		while (maxsplit != 0)
		{
			int num2 = s.IndexOf(sep, num, StringComparison.Ordinal);
			if (num2 < 0)
			{
				break;
			}
			list.Add(s.Substring(num, num2 - num));
			num = num2 + sep.Length;
			maxsplit--;
		}
		list.Add(s.Substring(num));
		return list;
	}

	public static List<string> RSplit(string s, string sep, int maxsplit = -1)
	{
		List<string> list = new List<string>();
		int num = s.Length;
		while (maxsplit != 0)
		{
			int num2 = ((num - sep.Length < 0) ? (-1) : s.LastIndexOf(sep, num - 1, StringComparison.Ordinal));
			if (num2 < 0 || num2 + sep.Length > num)
			{
				break;
			}
			list.Insert(0, s.Substring(num2 + sep.Length, num - num2 - sep.Length));
			num = num2;
			maxsplit--;
		}
		list.Insert(0, s.Substring(0, num));
		return list;
	}

	public static List<string> SplitLines(string s)
	{
		List<string> list = new List<string>();
		if (string.IsNullOrEmpty(s))
		{
			return list;
		}
		int num = 0;
		for (int i = 0; i < s.Length; i++)
		{
			char c = s[i];
			if (c == '\n' || c == '\r' || c == '\v' || c == '\f' || c == '\u001c' || c == '\u001d' || c == '\u001e' || c == '\u0085' || c == '\u2028' || c == '\u2029')
			{
				list.Add(s.Substring(num, i - num));
				if (c == '\r' && i + 1 < s.Length && s[i + 1] == '\n')
				{
					i++;
				}
				num = i + 1;
			}
		}
		if (num < s.Length)
		{
			list.Add(s.Substring(num));
		}
		return list;
	}

	public static string Lower(string s)
	{
		return s?.ToLowerInvariant() ?? "";
	}

	public static string Upper(string s)
	{
		return s?.ToUpperInvariant() ?? "";
	}

	public static string Casefold(string s)
	{
		if (s != null)
		{
			return s.ToLowerInvariant().Replace("ß", "ss");
		}
		return "";
	}

	public static string Slice(string s, int? start = null, int? end = null)
	{
		if (s == null)
		{
			return "";
		}
		int length = s.Length;
		int num = start.GetValueOrDefault();
		int num2 = end ?? length;
		if (num < 0)
		{
			num = Math.Max(0, length + num);
		}
		if (num2 < 0)
		{
			num2 = Math.Max(0, length + num2);
		}
		num = Math.Min(num, length);
		num2 = Math.Min(num2, length);
		if (num2 <= num)
		{
			return "";
		}
		return s.Substring(num, num2 - num);
	}

	public static List<T> Slice<T>(IList<T> s, int? start = null, int? end = null)
	{
		int count = s.Count;
		int num = start.GetValueOrDefault();
		int num2 = end ?? count;
		if (num < 0)
		{
			num = Math.Max(0, count + num);
		}
		if (num2 < 0)
		{
			num2 = Math.Max(0, count + num2);
		}
		num = Math.Min(num, count);
		num2 = Math.Min(num2, count);
		List<T> list = new List<T>();
		for (int i = num; i < num2; i++)
		{
			list.Add(s[i]);
		}
		return list;
	}

	public static string Mul(string s, int n)
	{
		if (n > 0)
		{
			return new StringBuilder(s.Length * n).Insert(0, s, n).ToString();
		}
		return "";
	}

	public static bool IsDigit(string s)
	{
		if (!string.IsNullOrEmpty(s))
		{
			return s.All(char.IsDigit);
		}
		return false;
	}

	public static bool IsAlnum(string s)
	{
		if (!string.IsNullOrEmpty(s))
		{
			return s.All(char.IsLetterOrDigit);
		}
		return false;
	}

	public static string Fmt(string fmt, params object[] args)
	{
		if (fmt == null)
		{
			return "";
		}
		StringBuilder stringBuilder = new StringBuilder();
		int num = 0;
		for (int i = 0; i < fmt.Length; i++)
		{
			char c = fmt[i];
			switch (c)
			{
			case '{':
			{
				if (i + 1 < fmt.Length && fmt[i + 1] == '{')
				{
					stringBuilder.Append('{');
					i++;
					break;
				}
				int num2 = fmt.IndexOf('}', i);
				if (num2 < 0)
				{
					throw new ValueError("Single '{' encountered in format string");
				}
				string text = fmt.Substring(i + 1, num2 - i - 1);
				string spec = "";
				string text2 = "";
				int num3 = text.IndexOf(':');
				if (num3 >= 0)
				{
					spec = text.Substring(num3 + 1);
					text = text.Substring(0, num3);
				}
				int num4 = text.IndexOf('!');
				if (num4 >= 0)
				{
					text2 = text.Substring(num4 + 1);
					text = text.Substring(0, num4);
				}
				int num5 = ((text.Length == 0) ? num++ : int.Parse(text, Inv));
				if (num5 >= args.Length)
				{
					throw new IndexError($"Replacement index {num5} out of range for positional args tuple");
				}
				object obj = args[num5];
				string value = ((text2 == "r") ? Repr(obj) : FormatSpec(obj, spec));
				stringBuilder.Append(value);
				i = num2;
				break;
			}
			case '}':
				if (i + 1 < fmt.Length && fmt[i + 1] == '}')
				{
					stringBuilder.Append('}');
					i++;
					break;
				}
				throw new ValueError("Single '}' encountered in format string");
			default:
				stringBuilder.Append(c);
				break;
			}
		}
		return stringBuilder.ToString();
	}

	public static string FormatSpec(object v, string spec)
	{
		if (string.IsNullOrEmpty(spec))
		{
			return Str(v);
		}
		int i = 0;
		char c = ' ';
		char c2 = '\0';
		if (spec.Length >= 2 && "<>^=".IndexOf(spec[1]) >= 0)
		{
			c = spec[0];
			c2 = spec[1];
			i = 2;
		}
		else if (spec.Length >= 1 && "<>^=".IndexOf(spec[0]) >= 0)
		{
			c2 = spec[0];
			i = 1;
		}
		bool flag = false;
		if (i < spec.Length && spec[i] == '0')
		{
			flag = true;
			i++;
		}
		int num = 0;
		for (; i < spec.Length && char.IsDigit(spec[i]); i++)
		{
			num = num * 10 + (spec[i] - 48);
		}
		bool flag2 = false;
		if (i < spec.Length && spec[i] == ',')
		{
			flag2 = true;
			i++;
		}
		int num2 = -1;
		if (i < spec.Length && spec[i] == '.')
		{
			i++;
			num2 = 0;
			for (; i < spec.Length && char.IsDigit(spec[i]); i++)
			{
				num2 = num2 * 10 + (spec[i] - 48);
			}
		}
		string text;
		switch ((i < spec.Length) ? spec[i] : '\0')
		{
		case 'F':
		case 'f':
			text = RoundHalfEven(Float(v), (num2 < 0) ? 6 : num2, flag2);
			break;
		case '%':
			text = RoundHalfEven(Float(v) * 100.0, (num2 < 0) ? 6 : num2, flag2) + "%";
			break;
		case 'd':
			text = (flag2 ? Int(v).ToString("#,0", Inv) : Int(v).ToString(Inv));
			break;
		case 'x':
			text = Int(v).ToString("x", Inv);
			break;
		case 'X':
			text = Int(v).ToString("X", Inv);
			break;
		case 's':
			text = Str(v);
			if (num2 >= 0 && text.Length > num2)
			{
				text = text.Substring(0, num2);
			}
			break;
		default:
			text = ((v is double num3 && num2 >= 0) ? num3.ToString("G" + num2, Inv) : ((!((v is long || v is int) && flag2)) ? Str(v) : Int(v).ToString("#,0", Inv)));
			break;
		}
		if (num > text.Length)
		{
			bool flag3 = IsNum(v);
			if (flag && c2 == '\0')
			{
				c = '0';
				c2 = '=';
			}
			if (c2 == '\0')
			{
				c2 = (flag3 ? '>' : '<');
			}
			int num4 = num - text.Length;
			switch (c2)
			{
			case '<':
				text += new string(c, num4);
				break;
			case '>':
				text = new string(c, num4) + text;
				break;
			case '^':
				text = new string(c, num4 / 2) + text + new string(c, num4 - num4 / 2);
				break;
			case '=':
				text = ((!text.StartsWith("-") && !text.StartsWith("+")) ? (new string(c, num4) + text) : (text[0] + new string(c, num4) + text.Substring(1)));
				break;
			}
		}
		return text;
	}

	public static string RoundHalfEven(double d, int prec, bool comma = false)
	{
		if (double.IsNaN(d))
		{
			return "nan";
		}
		if (double.IsInfinity(d))
		{
			if (!(d > 0.0))
			{
				return "-inf";
			}
			return "inf";
		}
		string text = ExactFixed(d, prec);
		if (comma)
		{
			bool num = text.StartsWith("-");
			if (num)
			{
				text = text.Substring(1);
			}
			int num2 = text.IndexOf('.');
			string s = ((num2 >= 0) ? text.Substring(0, num2) : text);
			text = string.Concat(str2: (num2 >= 0) ? text.Substring(num2) : "", str1: long.Parse(s, Inv).ToString("#,0", Inv), str0: num ? "-" : "");
		}
		return text;
	}

	private static string ExactFixed(double d, int prec)
	{
		long num = BitConverter.DoubleToInt64Bits(d);
		bool flag = num < 0;
		int num2 = (int)((num >> 52) & 0x7FF);
		long num3 = num & 0xFFFFFFFFFFFFFL;
		if (num2 == 0)
		{
			num2++;
		}
		else
		{
			num3 |= 0x10000000000000L;
		}
		num2 -= 1075;
		BigInteger bigInteger = new BigInteger(num3);
		BigInteger one = BigInteger.One;
		if (num2 > 0)
		{
			bigInteger <<= num2;
		}
		else
		{
			one <<= -num2;
		}
		BigInteger bigInteger2 = BigInteger.Pow(10, prec);
		BigInteger remainder;
		BigInteger bigInteger3 = BigInteger.DivRem(bigInteger * bigInteger2, one, out remainder);
		int num4 = (remainder * 2).CompareTo(one);
		if (num4 > 0 || (num4 == 0 && !bigInteger3.IsEven))
		{
			bigInteger3 += (BigInteger)1;
		}
		string text = bigInteger3.ToString(Inv);
		if (prec > 0)
		{
			if (text.Length <= prec)
			{
				text = new string('0', prec - text.Length + 1) + text;
			}
			text = text.Substring(0, text.Length - prec) + "." + text.Substring(text.Length - prec);
		}
		return (flag ? "-" : "") + text;
	}

	public static double Round(double d, int n)
	{
		return double.Parse(ExactFixed(d, Math.Max(0, n)), Inv);
	}

	public static long RoundInt(double d)
	{
		return (long)Math.Round(d, MidpointRounding.ToEven);
	}

	public static string Join(string sep, IEnumerable<object> items)
	{
		return string.Join(sep, items.Select(Str));
	}

	public static string Join(string sep, IEnumerable<string> items)
	{
		return string.Join(sep, items);
	}

	public static bool In(object item, IEnumerable seq)
	{
		foreach (object item2 in seq)
		{
			if (Eq(item2, item))
			{
				return true;
			}
		}
		return false;
	}

	public static object Dumps(object o)
	{
		return PyJson.Dumps(o);
	}

	public static object DeepCopy(object o)
	{
		if (!(o is PyDict pyDict))
		{
			if (o is List<object> source)
			{
				return source.Select(DeepCopy).ToList();
			}
			return o;
		}
		PyDict pyDict2 = new PyDict();
		foreach (KeyValuePair<string, object> item in pyDict)
		{
			pyDict2[item.Key] = DeepCopy(item.Value);
		}
		return pyDict2;
	}

	public static string AsStr(object o, string method)
	{
		if (o is string result)
		{
			return result;
		}
		throw new AttributeError("'" + TypeName(o) + "' object has no attribute '" + method + "'");
	}

	public static PyDict DictCtor(object o)
	{
		if (o != null)
		{
			if (!(o is PyDict other))
			{
				if (!(o is string text))
				{
					if (o is List<object> list)
					{
						PyDict pyDict = new PyDict();
						for (int i = 0; i < list.Count; i++)
						{
							object obj = list[i];
							if (obj is List<object> list2)
							{
								if (list2.Count != 2)
								{
									throw new ValueError($"dictionary update sequence element #{i} has length {list2.Count}; 2 is required");
								}
								if (list2[0] is PyDict || list2[0] is List<object>)
								{
									throw new TypeError("unhashable type: '" + TypeName(list2[0]) + "'");
								}
								pyDict[HashKey(list2[0])] = list2[1];
							}
							else if (obj is string text2)
							{
								if (text2.Length != 2)
								{
									throw new ValueError($"dictionary update sequence element #{i} has length {text2.Length}; 2 is required");
								}
								pyDict[text2[0].ToString()] = text2[1].ToString();
							}
							else
							{
								if (!(obj is PyDict pyDict2))
								{
									throw new TypeError($"cannot convert dictionary update sequence element #{i} to a sequence");
								}
								if (pyDict2.Count != 2)
								{
									throw new ValueError($"dictionary update sequence element #{i} has length {pyDict2.Count}; 2 is required");
								}
								pyDict[pyDict2.Keys[0]] = pyDict2.Keys[1];
							}
						}
						return pyDict;
					}
					throw new TypeError("'" + TypeName(o) + "' object is not iterable");
				}
				if (text.Length == 0)
				{
					return new PyDict();
				}
				throw new ValueError("dictionary update sequence element #0 has length 1; 2 is required");
			}
			return new PyDict(other);
		}
		throw new TypeError("'NoneType' object is not iterable");
	}

	public static string HashKey(object k)
	{
		if (!(k is string result))
		{
			return Str(k);
		}
		return result;
	}

	public static List<object> ListCtor(object o)
	{
		if (o != null)
		{
			if (!(o is List<object> collection))
			{
				if (!(o is PyDict pyDict))
				{
					if (o is string text)
					{
						List<object> list = new List<object>();
						for (int i = 0; i < text.Length; i++)
						{
							if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
							{
								list.Add(text.Substring(i, 2));
								i++;
							}
							else
							{
								list.Add(text[i].ToString());
							}
						}
						return list;
					}
					throw new TypeError("'" + TypeName(o) + "' object is not iterable");
				}
				return new List<object>(pyDict.Keys);
			}
			return new List<object>(collection);
		}
		throw new TypeError("'NoneType' object is not iterable");
	}

	public static double Min(double a, double b)
	{
		if (!(b < a))
		{
			return a;
		}
		return b;
	}

	public static double Max(double a, double b)
	{
		if (!(b > a))
		{
			return a;
		}
		return b;
	}

	public static TypeError NotCallable(object o)
	{
		return new TypeError("'" + TypeName(o) + "' object is not callable");
	}

	public static double Time()
	{
		return (double)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).Ticks / 10000000.0;
	}

	public static double Monotonic()
	{
		return Mono.Elapsed.TotalSeconds;
	}
}
