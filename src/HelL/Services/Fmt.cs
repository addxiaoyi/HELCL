using System;
using System.Globalization;

namespace HelL.Services;

public static class Fmt
{
	public static string Downloads(long n)
	{
		CultureInfo invariantCulture = CultureInfo.InvariantCulture;
		if (I18n.Current != "zh_CN")
		{
			if (n >= 1000)
			{
				if (n < 1000000000)
				{
					if (n >= 1000000)
					{
						return ((double)n / 1000000.0).ToString("0.#", invariantCulture) + "M";
					}
					return ((double)n / 1000.0).ToString("0.#", invariantCulture) + "K";
				}
				return ((double)n / 1000000000.0).ToString("0.#", invariantCulture) + "B";
			}
			if (n > 0)
			{
				return n.ToString(invariantCulture);
			}
			return "—";
		}
		if (n >= 10000)
		{
			if (n >= 100000000)
			{
				return ((double)n / 100000000.0).ToString("0.#", invariantCulture) + " 亿";
			}
			return ((double)n / 10000.0).ToString("0.#", invariantCulture) + " 万";
		}
		if (n > 0)
		{
			return n.ToString(invariantCulture);
		}
		return "—";
	}

	public static string Size(long bytes)
	{
		if (bytes >= 1048576)
		{
			if (bytes >= 1073741824)
			{
				return ((double)bytes / 1073741824.0).ToString("0.##") + " GB";
			}
			return ((double)bytes / 1048576.0).ToString("0.#") + " MB";
		}
		if (bytes >= 1024)
		{
			return ((double)bytes / 1024.0).ToString("0.#") + " KB";
		}
		return bytes + " B";
	}

	public static string Duration(long seconds)
	{
		if (seconds <= 0)
		{
			return I18n.L("0 分钟");
		}
		long num = seconds / 3600;
		long num2 = seconds % 3600 / 60;
		if (num >= 24)
		{
			return I18n.L("{0} 天 {1} 小时", num / 24, num % 24);
		}
		if (num > 0)
		{
			return I18n.L("{0} 小时 {1} 分钟", num, num2);
		}
		return I18n.L("{0} 分钟", Math.Max(1L, num2));
	}

	public static void SplitMsg(string? msg, out string status, out string speed)
	{
		status = msg ?? "";
		speed = "";
		if (!string.IsNullOrEmpty(status))
		{
			int num = status.IndexOf("  |  ", StringComparison.Ordinal);
			if (num >= 0)
			{
				speed = status.Substring(num + 5).Trim();
				status = status.Substring(0, num).Trim(' ', '·');
			}
		}
	}
}
