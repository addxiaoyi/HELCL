using System;
using System.Runtime.InteropServices;
using System.Windows.Media;
using Microsoft.Win32;

namespace HelL.Services;

/// <summary>
/// 读 Windows 的"强调色"。两级来源，取到哪个用哪个：
///   1. HKCU\Software\Microsoft\Windows\DWM\AccentColor —— 用户在设置里挑的颜色。
///      存的是 ABGR（低字节 R、然后 G、B、高字节 A），不是 ARGB，读的时候要反过来拼。
///      绝大多数机器这里是 0xFF 不透明；低于阈值视为没设置。
///   2. DwmGetColorizationColor —— 1 拿不到时兜底，返回 0xAARRGGBB 加一个"是否混合了模糊"的标记。
/// 两个都拿不到返回默认绿 #2E9B6B（和 Palette 里的 C.Accent 一致）。
/// 每次读都直接进注册表、不缓存：调用点就在用户切回窗口的那一刻。
/// </summary>
internal static class Accent
{
	private const string DwmKey = @"Software\Microsoft\Windows\DWM";

	public const string Default = "#A99AFF";

	/// <summary>低于这个 alpha 基本等于"没有强调色"。</summary>
	private const uint MinAlpha = 0x40;

	[DllImport("dwmapi.dll")]
	private static extern int DwmGetColorizationColor(out uint colorization, [MarshalAs(UnmanagedType.Bool)] out bool opaqueBlend);

	public static string Hex()
	{
		return Registry() ?? Colorization() ?? Default;
	}

	public static Color Rgb => HexToColor(Hex());

	private static Color HexToColor(string? hex)
	{
		try
		{
			if (hex != null && ColorConverter.ConvertFromString(hex) is Color color)
			{
				return color;
			}
		}
		catch
		{
		}
		return System.Windows.Media.Color.FromRgb(169, 154, 255);
	}

	private static string? Registry()
	{
		try
		{
			using RegistryKey? key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(DwmKey);
			if (key?.GetValue("AccentColor") is int abgr)
			{
				return AbgrToHex(abgr);
			}
		}
		catch
		{
		}
		return null;
	}

	private static string? Colorization()
	{
		try
		{
			if (DwmGetColorizationColor(out uint colorization, out _) != 0)
			{
				return null;
			}
			if (colorization >> 24 < MinAlpha)
			{
				return null;
			}
			return $"#{colorization & 0xFFFFFF:x6}";
		}
		catch (DllNotFoundException)
		{
			return null;
		}
		catch (EntryPointNotFoundException)
		{
			return null;
		}
	}

	/// <summary>注册表里的整数是 0xAABBGGRR（ABGR 顺序），转 #RRGGBB。</summary>
	private static string? AbgrToHex(int value)
	{
		uint num = (uint)value;
		if (num >> 24 < MinAlpha)
		{
			return null;
		}
		uint num2 = num & 0xFF;
		uint num3 = (num >> 8) & 0xFF;
		uint num4 = (num >> 16) & 0xFF;
		return $"#{num2:x2}{num3:x2}{num4:x2}";
	}
}
