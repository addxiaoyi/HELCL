using System;
using System.Collections.Generic;

namespace HelL.Pages.Ai;

internal static class AiUiText
{
	private static readonly string[] StatePrefixes = new string[10] { "准备：", "执行中：", "完成：", "已跳过：", "失败：", "准备:", "执行中:", "完成:", "已跳过:", "失败:" };

	public static string StripState(string? text)
	{
		string text2 = (text ?? "").Trim();
		string[] statePrefixes = StatePrefixes;
		foreach (string text3 in statePrefixes)
		{
			if (text2.StartsWith(text3, StringComparison.Ordinal))
			{
				return text2.Substring(text3.Length).Trim();
			}
		}
		return text2;
	}

	public static (string Label, string Arg) SplitLabel(string? text)
	{
		string text2 = StripState(text);
		int num = text2.IndexOfAny(new char[2] { '：', ':' });
		if (num > 0 && num <= 16 && num < text2.Length - 1)
		{
			string text3 = text2.Substring(0, num).Trim();
			string text4 = text2.Substring(num + 1).Trim();
			if (text3.Length > 0 && text4.Length > 0 && !text3.Contains("http"))
			{
				return (Label: text3, Arg: text4);
			}
		}
		return (Label: text2, Arg: "");
	}

	public static bool IsMcp(string? tool)
	{
		string text = tool ?? "";
		if (!text.StartsWith("mcp__", StringComparison.Ordinal) && !text.StartsWith("mcp.", StringComparison.Ordinal))
		{
			return text.StartsWith("mcp_", StringComparison.Ordinal);
		}
		return true;
	}

	public static AiRisk RiskOf(string? tool, string? sideEffect)
	{
		string text = sideEffect ?? "";
		if (text == "delete")
		{
			return AiRisk.Delete;
		}
		if (text.StartsWith("write", StringComparison.Ordinal))
		{
			return AiRisk.Write;
		}
		if (text == "network")
		{
			return AiRisk.Network;
		}
		string text2 = tool ?? "";
		if (text2.StartsWith("delete_", StringComparison.Ordinal) || text2.StartsWith("remove_", StringComparison.Ordinal))
		{
			return AiRisk.Delete;
		}
		if (text2.StartsWith("install_", StringComparison.Ordinal) || text2.StartsWith("write_", StringComparison.Ordinal) || text2.StartsWith("create_", StringComparison.Ordinal) || text2.StartsWith("enable_", StringComparison.Ordinal) || text2.StartsWith("disable_", StringComparison.Ordinal) || text2 == "launch_game" || text2 == "download_java")
		{
			return AiRisk.Write;
		}
		if (IsMcp(text2))
		{
			return AiRisk.Write;
		}
		if (text.Length == 0 || text == "none" || text == "read")
		{
			return AiRisk.Read;
		}
		return AiRisk.Write;
	}

	public static string RiskText(AiRisk r)
	{
		return r switch
		{
			AiRisk.Delete => "删除", 
			AiRisk.Write => "写入", 
			AiRisk.Network => "联网", 
			_ => "只读", 
		};
	}

	public static char DiffKind(string? line)
	{
		string text = line ?? "";
		if (text.StartsWith("@@", StringComparison.Ordinal) || text.StartsWith("---", StringComparison.Ordinal) || text.StartsWith("+++", StringComparison.Ordinal))
		{
			return '@';
		}
		if (text.StartsWith("-", StringComparison.Ordinal))
		{
			return '-';
		}
		if (text.StartsWith("+", StringComparison.Ordinal))
		{
			return '+';
		}
		return ' ';
	}

	public static bool LooksLikeDiff(IList<string> lines)
	{
		int num = 0;
		foreach (string line in lines)
		{
			char c = DiffKind(line);
			if (c == '-' || c == '+')
			{
				num++;
			}
		}
		return num > 0;
	}

	public static string Tokens(long n)
	{
		if (n < 1000)
		{
			return n.ToString();
		}
		if (n < 1000000)
		{
			return ((double)n / 1000.0).ToString("0.#") + "k";
		}
		return ((double)n / 1000000.0).ToString("0.##") + "M";
	}

	public static double ContextRatio(long input, long requests, long window)
	{
		if (window <= 0 || input <= 0)
		{
			return 0.0;
		}
		double num = (double)((requests > 0) ? (input / requests) : input) / (double)window;
		if (num < 0.0)
		{
			return 0.0;
		}
		if (!(num > 1.0))
		{
			return num;
		}
		return 1.0;
	}

	public static string StepSummary(int count, double seconds, bool running, string current)
	{
		if (running)
		{
			if (current.Length <= 0)
			{
				return "正在执行";
			}
			return "正在" + Trim(current, 24);
		}
		string arg = ((seconds >= 0.1) ? (" · 用时 " + ((seconds < 60.0) ? (seconds.ToString("0.0") + "s") : ((int)(seconds / 60.0) + "m" + (int)(seconds % 60.0) + "s"))) : "");
		return $"执行了 {count} 步{arg}";
	}

	public static string Trim(string s, int max)
	{
		if (s == null)
		{
			return "";
		}
		if (s.Length <= max)
		{
			return s;
		}
		return s.Substring(0, Math.Max(1, max - 1)) + "…";
	}

	public static (int Done, int Total) PlanProgress(IList<(string Title, string Status)> items)
	{
		int num = 0;
		foreach (var item in items)
		{
			if (item.Status == "completed" || item.Status == "done")
			{
				num++;
			}
		}
		return (Done: num, Total: items.Count);
	}
}
