using System;

namespace HelL.Core.Launcher;

public static class DownloadStatus
{
	public static readonly PyDict PHASE_LABELS = PyDict.Of("idle", "就绪", "prepare", "准备下载", "dns", "正在解析 DNS", "dns_ok", "DNS 解析成功", "dns_fail", "DNS 解析失败", "tcp", "正在 TCP 握手", "tcp_ok", "TCP 握手成功", "tcp_fail", "TCP 握手失败", "tls", "正在 TLS 握手", "tls_ok", "TLS 握手成功", "tls_fail", "TLS 握手失败", "reuse", "复用已有 TCP 连接", "http", "正在发送 HTTP 请求", "http_ok", "已收到 HTTP 响应", "transfer", "下载中", "verify", "正在校验文件", "skip", "已存在，跳过", "done", "下载完成", "fail", "下载失败");

	public static string FormatSpeed(object bps)
	{
		double num;
		try
		{
			num = Py.Float(bps);
		}
		catch (PyException)
		{
			return "0 B/s";
		}
		if (num < 0.0)
		{
			num = 0.0;
		}
		return Utils.FormatSize(num) + "/s";
	}

	public static string FormatEta(double? seconds)
	{
		if (!seconds.HasValue || seconds < 0.0 || double.IsInfinity(seconds.Value) || double.IsNaN(seconds.Value))
		{
			return "";
		}
		long num = (long)Math.Truncate(seconds.Value);
		if (num < 60)
		{
			return Py.Fmt("剩余 {0} 秒", num);
		}
		long num2 = num / 60;
		long num3 = num % 60;
		if (num2 < 60)
		{
			return Py.Fmt("剩余 {0}:{1:02d}", num2, num3);
		}
		long num4 = num2 / 60;
		num2 %= 60;
		return Py.Fmt("剩余 {0}:{1:02d}:{2:02d}", num4, num2, num3);
	}
}
