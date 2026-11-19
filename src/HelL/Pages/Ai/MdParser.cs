using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace HelL.Pages.Ai;

internal static class MdParser
{
	private static readonly Regex Ordered = new Regex("^(\\s*)(\\d{1,9})[.)]\\s+(.*)$", RegexOptions.Compiled);

	private static readonly Regex Bullet = new Regex("^(\\s*)[-*+]\\s+(.*)$", RegexOptions.Compiled);

	private static readonly Regex Heading = new Regex("^(#{1,6})\\s+(.*?)\\s*#*\\s*$", RegexOptions.Compiled);

	private static readonly Regex RuleRx = new Regex("^\\s{0,3}([-*_])(\\s*\\1){2,}\\s*$", RegexOptions.Compiled);

	private static readonly Regex TableSep = new Regex("^\\s*\\|?\\s*:?-+:?\\s*(\\|\\s*:?-+:?\\s*)*\\|?\\s*$", RegexOptions.Compiled);

	public static List<MdBlock> Parse(string? text)
	{
		string[] array = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
		List<MdBlock> list = new List<MdBlock>();
		int i = 0;
		while (i < array.Length)
		{
			string text2 = array[i];
			string text3 = text2.Trim();
			if (text3.Length == 0)
			{
				i++;
				continue;
			}
			if (text3.StartsWith("```", StringComparison.Ordinal) || text3.StartsWith("~~~", StringComparison.Ordinal))
			{
				string value = text3.Substring(0, 3);
				string lang = text3.Substring(3).Trim();
				StringBuilder stringBuilder = new StringBuilder();
				for (i++; i < array.Length && !array[i].Trim().StartsWith(value, StringComparison.Ordinal); i++)
				{
					if (stringBuilder.Length > 0)
					{
						stringBuilder.Append('\n');
					}
					stringBuilder.Append(array[i]);
				}
				i++;
				list.Add(new MdBlock
				{
					Kind = MdBlockKind.Code,
					Lang = lang,
					Raw = stringBuilder.ToString()
				});
				continue;
			}
			Match match = Heading.Match(text3);
			if (match.Success)
			{
				list.Add(new MdBlock
				{
					Kind = MdBlockKind.Heading,
					Level = match.Groups[1].Value.Length,
					Inlines = ParseInline(match.Groups[2].Value)
				});
				i++;
				continue;
			}
			if (RuleRx.IsMatch(text2))
			{
				list.Add(new MdBlock
				{
					Kind = MdBlockKind.Rule
				});
				i++;
				continue;
			}
			if (text3.Contains("|") && i + 1 < array.Length && TableSep.IsMatch(array[i + 1]))
			{
				MdBlock mdBlock = new MdBlock
				{
					Kind = MdBlockKind.Table
				};
				foreach (string item in SplitRow(text3))
				{
					mdBlock.Header.Add(ParseInline(item));
				}
				foreach (string item2 in SplitRow(array[i + 1].Trim()))
				{
					string text4 = item2.Trim();
					mdBlock.Align.Add((text4.StartsWith(":") && text4.EndsWith(":")) ? "center" : (text4.EndsWith(":") ? "right" : "left"));
				}
				for (i += 2; i < array.Length && array[i].Trim().Length > 0 && array[i].Contains("|"); i++)
				{
					List<List<MdInline>> list2 = new List<List<MdInline>>();
					foreach (string item3 in SplitRow(array[i].Trim()))
					{
						list2.Add(ParseInline(item3));
					}
					while (list2.Count < mdBlock.Header.Count)
					{
						list2.Add(new List<MdInline>());
					}
					if (list2.Count > mdBlock.Header.Count)
					{
						list2.RemoveRange(mdBlock.Header.Count, list2.Count - mdBlock.Header.Count);
					}
					mdBlock.Rows.Add(list2);
				}
				list.Add(mdBlock);
				continue;
			}
			if (text3.StartsWith(">", StringComparison.Ordinal))
			{
				StringBuilder stringBuilder2 = new StringBuilder();
				for (; i < array.Length && array[i].Trim().StartsWith(">", StringComparison.Ordinal); i++)
				{
					string text5 = array[i].Trim().Substring(1);
					if (text5.StartsWith(" "))
					{
						text5 = text5.Substring(1);
					}
					stringBuilder2.Append(text5).Append('\n');
				}
				list.Add(new MdBlock
				{
					Kind = MdBlockKind.Quote,
					Children = Parse(stringBuilder2.ToString())
				});
				continue;
			}
			Match match2 = Ordered.Match(text2);
			Match match3 = Bullet.Match(text2);
			if (match2.Success || match3.Success)
			{
				bool success = match2.Success;
				MdBlock mdBlock2 = new MdBlock
				{
					Kind = (success ? MdBlockKind.Ordered : MdBlockKind.Bullet)
				};
				if (success)
				{
					int.TryParse(match2.Groups[2].Value, out mdBlock2.Start);
				}
				while (i < array.Length)
				{
					Match match4 = Ordered.Match(array[i]);
					Match match5 = Bullet.Match(array[i]);
					Match match6 = (success ? match4 : match5);
					if (match6.Success)
					{
						int length = match6.Groups[1].Value.Replace("\t", "    ").Length;
						string text6 = (success ? match6.Groups[3].Value : match6.Groups[2].Value);
						mdBlock2.Items.Add(ParseInline(text6));
						mdBlock2.ItemDepth.Add(Math.Min(length / 2, 4));
						i++;
						continue;
					}
					if ((success ? match5 : match4).Success && array[i].Length - array[i].TrimStart().Length >= 2)
					{
						Match match7 = (success ? match5 : match4);
						int length2 = match7.Groups[1].Value.Replace("\t", "    ").Length;
						mdBlock2.Items.Add(ParseInline(success ? match7.Groups[2].Value : match7.Groups[3].Value));
						mdBlock2.ItemDepth.Add(Math.Max(1, Math.Min(length2 / 2, 4)));
						i++;
						continue;
					}
					if (array[i].Trim().Length <= 0 || !array[i].StartsWith("  ", StringComparison.Ordinal) || mdBlock2.Items.Count <= 0)
					{
						break;
					}
					List<MdInline> list3 = mdBlock2.Items[mdBlock2.Items.Count - 1];
					list3.Add(new MdInline
					{
						Kind = MdInlineKind.Text,
						Text = " "
					});
					list3.AddRange(ParseInline(array[i].Trim()));
					i++;
				}
				list.Add(mdBlock2);
				continue;
			}
			List<string> list4 = new List<string>();
			for (; i < array.Length; i++)
			{
				string text7 = array[i];
				string text8 = text7.Trim();
				if (text8.Length == 0 || text8.StartsWith("```", StringComparison.Ordinal) || text8.StartsWith("~~~", StringComparison.Ordinal) || Heading.IsMatch(text8) || text8.StartsWith(">", StringComparison.Ordinal) || (list4.Count > 0 && (Ordered.IsMatch(text7) || Bullet.IsMatch(text7))) || (text8.Contains("|") && i + 1 < array.Length && TableSep.IsMatch(array[i + 1])) || (list4.Count > 0 && RuleRx.IsMatch(text7)))
				{
					break;
				}
				list4.Add(text8);
			}
			if (list4.Count == 0)
			{
				list4.Add(text3);
				i++;
			}
			MdBlock mdBlock3 = new MdBlock
			{
				Kind = MdBlockKind.Paragraph
			};
			for (int j = 0; j < list4.Count; j++)
			{
				if (j > 0)
				{
					mdBlock3.Inlines.Add(new MdInline
					{
						Kind = MdInlineKind.Break
					});
				}
				mdBlock3.Inlines.AddRange(ParseInline(list4[j]));
			}
			list.Add(mdBlock3);
		}
		return list;
	}

	internal static List<string> SplitRow(string row)
	{
		string text = row.Trim();
		if (text.StartsWith("|"))
		{
			text = text.Substring(1);
		}
		if (text.EndsWith("|") && !text.EndsWith("\\|"))
		{
			text = text.Substring(0, text.Length - 1);
		}
		List<string> list = new List<string>();
		StringBuilder stringBuilder = new StringBuilder();
		bool flag = false;
		for (int i = 0; i < text.Length; i++)
		{
			char c = text[i];
			if (c == '\\' && i + 1 < text.Length && text[i + 1] == '|')
			{
				stringBuilder.Append('|');
				i++;
				continue;
			}
			switch (c)
			{
			case '`':
				flag = !flag;
				stringBuilder.Append(c);
				continue;
			case '|':
				if (!flag)
				{
					list.Add(stringBuilder.ToString().Trim());
					stringBuilder.Clear();
					continue;
				}
				break;
			}
			stringBuilder.Append(c);
		}
		list.Add(stringBuilder.ToString().Trim());
		return list;
	}

	public static List<MdInline> ParseInline(string? text)
	{
		List<MdInline> list = new List<MdInline>();
		ParseInto(text ?? "", list, 0);
		return Merge(list);
	}

	private static void ParseInto(string s, List<MdInline> outList, int depth)
	{
		StringBuilder buf = new StringBuilder();
		int num = 0;
		while (num < s.Length)
		{
			char c = s[num];
			if (c == '\\' && num + 1 < s.Length && "\\`*_[]()#+-.!|~>".IndexOf(s[num + 1]) >= 0)
			{
				buf.Append(s[num + 1]);
				num += 2;
				continue;
			}
			if (c == '`')
			{
				int i;
				for (i = 1; num + i < s.Length && s[num + i] == '`'; i++)
				{
				}
				string value = new string('`', i);
				int num2 = s.IndexOf(value, num + i, StringComparison.Ordinal);
				if (num2 > 0)
				{
					Flush();
					string text = s.Substring(num + i, num2 - num - i);
					if (text.Length >= 2 && text[0] == ' ' && text[text.Length - 1] == ' ')
					{
						text = text.Substring(1, text.Length - 2);
					}
					outList.Add(new MdInline
					{
						Kind = MdInlineKind.Code,
						Text = text
					});
					num = num2 + i;
					continue;
				}
			}
			if (depth < 6 && (c == '*' || c == '_') && num + 1 < s.Length && s[num + 1] == c)
			{
				string mark = new string(c, 2);
				int num3 = FindClose(s, mark, num + 2);
				if (num3 > num + 2)
				{
					Flush();
					MdInline mdInline = new MdInline
					{
						Kind = MdInlineKind.Bold
					};
					ParseInto(s.Substring(num + 2, num3 - num - 2), mdInline.Children, depth + 1);
					outList.Add(mdInline);
					num = num3 + 2;
					continue;
				}
			}
			if (depth < 6 && c == '~' && num + 1 < s.Length && s[num + 1] == '~')
			{
				int num4 = FindClose(s, "~~", num + 2);
				if (num4 > num + 2)
				{
					Flush();
					MdInline mdInline2 = new MdInline
					{
						Kind = MdInlineKind.Strike
					};
					ParseInto(s.Substring(num + 2, num4 - num - 2), mdInline2.Children, depth + 1);
					outList.Add(mdInline2);
					num = num4 + 2;
					continue;
				}
			}
			if (depth < 6 && (c == '*' || c == '_') && num + 1 < s.Length && s[num + 1] != ' ' && s[num + 1] != c)
			{
				int num5 = ((c == '*' || num == 0 || !char.IsLetterOrDigit(s[num - 1])) ? FindClose(s, c.ToString(), num + 1) : (-1));
				if (num5 > num + 1 && s[num5 - 1] != ' ' && (c == '*' || num5 + 1 >= s.Length || !char.IsLetterOrDigit(s[num5 + 1])))
				{
					Flush();
					MdInline mdInline3 = new MdInline
					{
						Kind = MdInlineKind.Italic
					};
					ParseInto(s.Substring(num + 1, num5 - num - 1), mdInline3.Children, depth + 1);
					outList.Add(mdInline3);
					num = num5 + 1;
					continue;
				}
			}
			if (c == '[')
			{
				int num6 = s.IndexOf("](", num + 1, StringComparison.Ordinal);
				if (num6 > num)
				{
					int num7 = s.IndexOf(')', num6 + 2);
					if (num7 > num6)
					{
						string text2 = s.Substring(num + 1, num6 - num - 1);
						string text3 = s.Substring(num6 + 2, num7 - num6 - 2).Trim();
						if (text2.IndexOf('\n') < 0 && text3.IndexOf(' ') < 0)
						{
							Flush();
							MdInline mdInline4 = new MdInline
							{
								Kind = MdInlineKind.Link,
								Url = text3
							};
							ParseInto(text2, mdInline4.Children, depth + 1);
							outList.Add(mdInline4);
							num = num7 + 1;
							continue;
						}
					}
				}
			}
			buf.Append(c);
			num++;
		}
		Flush();
		void Flush()
		{
			if (buf.Length > 0)
			{
				outList.Add(new MdInline
				{
					Kind = MdInlineKind.Text,
					Text = buf.ToString()
				});
				buf.Clear();
			}
		}
	}

	private static int FindClose(string s, string mark, int from)
	{
		int num = from;
		while (num <= s.Length - mark.Length)
		{
			if (s[num] == '`')
			{
				int num2 = s.IndexOf('`', num + 1);
				if (num2 < 0)
				{
					return -1;
				}
				num = num2 + 1;
			}
			else
			{
				if (string.CompareOrdinal(s, num, mark, 0, mark.Length) == 0)
				{
					return num;
				}
				num++;
			}
		}
		return -1;
	}

	private static List<MdInline> Merge(List<MdInline> list)
	{
		List<MdInline> list2 = new List<MdInline>();
		foreach (MdInline item in list)
		{
			if (item.Kind == MdInlineKind.Text && list2.Count > 0 && list2[list2.Count - 1].Kind == MdInlineKind.Text)
			{
				list2[list2.Count - 1].Text += item.Text;
			}
			else
			{
				list2.Add(item);
			}
		}
		return list2;
	}

	public static string PlainText(IEnumerable<MdInline> inlines)
	{
		StringBuilder stringBuilder = new StringBuilder();
		foreach (MdInline inline in inlines)
		{
			switch (inline.Kind)
			{
			case MdInlineKind.Text:
			case MdInlineKind.Code:
				stringBuilder.Append(inline.Text);
				break;
			case MdInlineKind.Break:
				stringBuilder.Append('\n');
				break;
			default:
				stringBuilder.Append(PlainText(inline.Children));
				break;
			}
		}
		return stringBuilder.ToString();
	}
}
