using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace HelL.Core.Launcher.Ai;

/// <summary>
/// AI 长期记忆：跨对话保存玩家的电脑配置、偏好和修好过的问题。
/// 存在数据目录的 ai_memory.md，一行一条（"- 日期 内容"），玩家可以直接用记事本改。
/// </summary>
public class FileMemory : NoopMemory
{
	public const int MaxEntries = 80;

	public const int MaxEntryChars = 300;

	public const int MaxLoadChars = 4000;

	private static readonly object _lock = new object();

	public static string FilePath => Path.Combine(Utils.ResolveRoot(), "ai_memory.md");

	public static List<string> Entries()
	{
		lock (_lock)
		{
			try
			{
				if (!File.Exists(FilePath)) return new List<string>();
				return File.ReadAllLines(FilePath, Encoding.UTF8)
					.Select(l => l.Trim())
					.Where(l => l.StartsWith("- "))
					.Select(l => l.Substring(2).Trim())
					.Where(l => l.Length > 0)
					.ToList();
			}
			catch (Exception)
			{
				return new List<string>();
			}
		}
	}

	private static void WriteAll(List<string> entries)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
		var sb = new StringBuilder("# HelL AI 记忆（一行一条，可直接编辑或删除）\n\n");
		foreach (string e in entries) sb.Append("- ").Append(e).Append('\n');
		File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
	}

	public override string Load(string chatId)
	{
		List<string> all = Entries();
		if (all.Count == 0) return "";
		// 新的优先，超出长度时丢掉最旧的
		var picked = new List<string>();
		int total = 0;
		for (int i = all.Count - 1; i >= 0; i--)
		{
			total += all[i].Length + 3;
			if (total > MaxLoadChars) break;
			picked.Insert(0, all[i]);
		}
		return string.Join("\n", picked.Select(e => "- " + e)) + "\n（以上是你之前记下的，按需参考；过时或错误的用 forget 删掉）";
	}

	public override void Save(string chatId, string text)
	{
		Remember(text);
	}

	public override List<object> Search(string chatId, string query, int k = 3)
	{
		string q = (query ?? "").Trim();
		return Entries().Where(e => q.Length == 0 || e.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
			.Reverse().Take(k).Cast<object>().ToList();
	}

	public static string Remember(string text)
	{
		string t = (text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
		if (t.Length == 0) return "没有要记的内容";
		if (t.Length > MaxEntryChars) t = t.Substring(0, MaxEntryChars);
		lock (_lock)
		{
			List<string> all = Entries();
			// 同样内容已经记过就不重复记
			if (all.Any(e => e.EndsWith(t, StringComparison.OrdinalIgnoreCase))) return "已经记过了: " + t;
			all.Add(DateTime.Now.ToString("yyyy-MM-dd") + " " + t);
			while (all.Count > MaxEntries) all.RemoveAt(0);
			WriteAll(all);
		}
		return "已记住: " + t;
	}

	/// <summary>去掉条目开头的 "yyyy-MM-dd " 日期，只留内容。</summary>
	public static string TextOf(string entry)
	{
		string e = (entry ?? "").Trim();
		if (e.Length > 11 && e[4] == '-' && e[7] == '-' && e[10] == ' ' && char.IsDigit(e[0]) && char.IsDigit(e[3]) && char.IsDigit(e[9]))
		{
			return e.Substring(11).Trim();
		}
		return e;
	}

	/// <summary>从 remember 工具的结果里取出记下的内容；不是"已记住"返回 null。</summary>
	public static string RememberedOf(string toolResult)
	{
		string r = (toolResult ?? "").Trim();
		foreach (string p in new[] { "已记住: ", "已记住：" })
		{
			if (r.StartsWith(p, StringComparison.Ordinal)) return r.Substring(p.Length).Trim();
		}
		return null;
	}

	/// <summary>撤销一次 remember：删掉最后一条内容完全相同的记忆。</summary>
	public static bool Undo(string text)
	{
		string t = (text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
		if (t.Length == 0) return false;
		lock (_lock)
		{
			List<string> all = Entries();
			for (int i = all.Count - 1; i >= 0; i--)
			{
				if (string.Equals(TextOf(all[i]), t, StringComparison.OrdinalIgnoreCase))
				{
					all.RemoveAt(i);
					WriteAll(all);
					return true;
				}
			}
			return false;
		}
	}

	/// <summary>改第 index 条的内容（保留原日期）；空内容等于删除。</summary>
	public static bool Update(int index, string text)
	{
		string t = (text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
		if (t.Length > MaxEntryChars) t = t.Substring(0, MaxEntryChars);
		lock (_lock)
		{
			List<string> all = Entries();
			if (index < 0 || index >= all.Count) return false;
			if (t.Length == 0)
			{
				all.RemoveAt(index);
			}
			else
			{
				string old = all[index];
				string body = TextOf(old);
				string date = old.Length > body.Length ? old.Substring(0, old.Length - body.Length).Trim() : "";
				all[index] = date.Length > 0 ? date + " " + t : t;
			}
			WriteAll(all);
			return true;
		}
	}

	public static bool RemoveAt(int index)
	{
		lock (_lock)
		{
			List<string> all = Entries();
			if (index < 0 || index >= all.Count) return false;
			all.RemoveAt(index);
			WriteAll(all);
			return true;
		}
	}

	/// <summary>清空全部记忆，返回删掉的条数。</summary>
	public static int Clear()
	{
		lock (_lock)
		{
			int n = Entries().Count;
			if (n > 0) WriteAll(new List<string>());
			return n;
		}
	}

	public static string Forget(string keyword)
	{
		string k = (keyword ?? "").Trim();
		if (k.Length == 0) return "需要关键词";
		lock (_lock)
		{
			List<string> all = Entries();
			List<string> removed = all.Where(e => e.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
			if (removed.Count == 0) return "没有包含「" + k + "」的记忆";
			WriteAll(all.Except(removed).ToList());
			return "已删除 " + removed.Count + " 条: " + string.Join("；", removed);
		}
	}
}
