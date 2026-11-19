using System;
using System.Text;

namespace HelL.Services;

public sealed class TaskRow
{
	public int LogLines;

	public string Id { get; init; } = "";

	public string Title { get; set; } = "";

	public string Status { get; set; } = I18n.L("排队中…");

	public string Speed { get; set; } = "";

	public double Progress { get; set; }

	public bool Indeterminate { get; set; } = true;

	public bool Finished { get; set; }

	public bool Success { get; set; }

	public bool HasCrash { get; set; }

	public bool Cancelled => Finished && !Success && (Status == "已取消" || Status == I18n.L("已取消"));

	public DateTime Started { get; } = DateTime.Now;

	public StringBuilder Log { get; } = new StringBuilder();

	public void Append(string line)
	{
		if (!string.IsNullOrEmpty(line))
		{
			Log.AppendLine(line);
			LogLines++;
			if (LogLines > 600)
			{
				string text = Log.ToString();
				int num = text.IndexOf('\n', text.Length / 3);
				Log.Clear();
				Log.Append((num > 0) ? text.Substring(num + 1) : text);
				LogLines = 400;
			}
		}
	}
}
