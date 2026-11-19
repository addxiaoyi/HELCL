using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HelL.Services;

/// <summary>
/// 几处 DWM 属性在不同系统版本上的差异：
///   38 DWMWA_SYSTEMBACKDROP_TYPE        Win11 22H2+，1=None 2=Mica 3=Acrylic 4=Tabbed
///   33 DWMWA_WINDOW_CORNER_PREFERENCE Win11，1=不圆 2=圆 3=小圆
/// 旧系统返回 false，由窗口保留不透明底色。
/// </summary>
internal static class WinVer
{
	private const int NavMica = 38;

	private const int NavCorner = 33;

	[DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

	private static bool Set(IntPtr hwnd, int attr, int value)
	{
		try
		{
			int status = DwmSetWindowAttribute(hwnd, attr, ref value, Marshal.SizeOf<int>());
			if (status != 0) Trace.WriteLine($"[WinVer] DWM attribute {attr} is unavailable: 0x{status:X8}");
			return status == 0;
		}
		catch (DllNotFoundException error)
		{
			Trace.WriteLine("[WinVer] DWM is unavailable: " + error.Message);
			return false;
		}
		catch (EntryPointNotFoundException error)
		{
			Trace.WriteLine("[WinVer] DWM attributes are unavailable: " + error.Message);
			return false;
		}
	}

	public static bool SystemBackdrop(IntPtr hwnd, int type)
	{
		return Set(hwnd, NavMica, type);
	}

	public static void Corner(IntPtr hwnd, bool round)
	{
		Set(hwnd, NavCorner, round ? 2 : 1);
	}
}
