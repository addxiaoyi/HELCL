using System;
using System.Collections.Generic;
using System.Linq;

namespace HelL.Core.Launcher;

public sealed class DownloadSnapshot
{
	public string phase;

	public string phase_label;

	public string detail;

	public string filename;

	public string host;

	public string ip;

	public string tls_version;

	public string handshake;

	public string error;

	public long port;

	public long http_status;

	public long bytes_done;

	public long bytes_total;

	public long files_done;

	public long files_total;

	public double speed_bps;

	public bool active;

	private static readonly string[] TlsPhases = new string[7] { "tls_ok", "http", "http_ok", "reuse", "transfer", "verify", "done" };

	private static readonly string[] HttpPhases = new string[4] { "http_ok", "transfer", "verify", "done" };

	public double percent
	{
		get
		{
			if (bytes_total > 0)
			{
				return Math.Min(100.0, (double)bytes_done * 100.0 / (double)bytes_total);
			}
			if (files_total > 0)
			{
				return Math.Min(100.0, (double)files_done * 100.0 / (double)files_total);
			}
			return 0.0;
		}
	}

	public string status_line
	{
		get
		{
			List<string> list = new List<string>();
			if (!string.IsNullOrEmpty(handshake))
			{
				list.Add(handshake);
			}
			if (!string.IsNullOrEmpty(tls_version) && TlsPhases.Contains<string>(phase))
			{
				list.Add(tls_version);
			}
			if (http_status != 0L && HttpPhases.Contains<string>(phase))
			{
				list.Add("HTTP " + http_status);
			}
			string text = phase_label;
			if (phase == "transfer" && !string.IsNullOrEmpty(filename))
			{
				text = "下载中 " + filename;
			}
			else if (!string.IsNullOrEmpty(detail) && phase != "transfer" && phase != "idle")
			{
				text = detail;
			}
			if (!string.IsNullOrEmpty(text) && !list.Contains(text))
			{
				list.Add(text);
			}
			if (!string.IsNullOrEmpty(error) && phase == "fail")
			{
				list.Add(error);
			}
			string text2 = string.Join(" · ", list.Where((string p) => !string.IsNullOrEmpty(p)));
			if (text2.Length <= 0)
			{
				return "就绪";
			}
			return text2;
		}
	}

	public string meta_line
	{
		get
		{
			List<string> list = new List<string> { "速度 " + DownloadStatus.FormatSpeed(speed_bps) };
			if (bytes_total > 0)
			{
				list.Add(Utils.FormatSize(bytes_done) + " / " + Utils.FormatSize(bytes_total));
			}
			else if (bytes_done > 0)
			{
				list.Add(Utils.FormatSize(bytes_done));
			}
			if (files_total > 0)
			{
				list.Add(files_done + "/" + files_total + " 个文件");
			}
			if (bytes_total > bytes_done && speed_bps > 256.0)
			{
				list.Add(DownloadStatus.FormatEta((double)(bytes_total - bytes_done) / speed_bps));
			}
			return string.Join("    ", list);
		}
	}
}
