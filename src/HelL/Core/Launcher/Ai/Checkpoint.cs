using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace HelL.Core.Launcher.Ai;

public static class Checkpoint
{
	public static string CHECKPOINTS_DIR = null;

	private static readonly object _LOCK = new object();

	public const long KEEP_CHATS = 20L;

	public const long MAX_SNAPSHOT_BYTES = 536870912L;

	public const long MAX_CHAT_BYTES = 1073741824L;

	public const int MAX_DIR_FILES = 20000;

	private static string _root()
	{
		string obj = CHECKPOINTS_DIR ?? PPath.Join(Utils.ROOT, "cache", "ai_checkpoints");
		Utils.EnsureDir(obj);
		return obj;
	}

	private static string _chat_dir(string chatId)
	{
		string text = string.Concat(from ch in EnumCodepoints(Py.Str(Py.Truthy(chatId) ? chatId : "active"))
			where IsAlnum(ch) || ch == "-" || ch == "_"
			select ch);
		if (text.Length == 0)
		{
			text = "active";
		}
		string text2 = PPath.Join(_root(), text);
		Utils.EnsureDir(text2);
		return text2;
	}

	private static IEnumerable<string> EnumCodepoints(string s)
	{
		for (int i = 0; i < s.Length; i++)
		{
			if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
			{
				yield return s.Substring(i, 2);
				i++;
			}
			else
			{
				yield return s[i].ToString();
			}
		}
	}

	private static bool IsAlnum(string cp)
	{
		UnicodeCategory unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(cp, 0);
		if ((uint)unicodeCategory <= 4u || (uint)(unicodeCategory - 8) <= 2u)
		{
			return true;
		}
		return false;
	}

	private static string _journal_path(string chatId)
	{
		return PPath.Join(_chat_dir(chatId), "journal.json");
	}

	private static string _files_dir(string chatId)
	{
		string text = PPath.Join(_chat_dir(chatId), "files");
		Utils.EnsureDir(text);
		return text;
	}

	public static List<object> _load_journal(string chatId)
	{
		return (Utils.ReadJson(_journal_path(chatId)) as List<object>) ?? new List<object>();
	}

	private static void _save_journal(string chatId, List<object> ops)
	{
		Utils.WriteJson(_journal_path(chatId), ops);
	}

	private static string Abs(string p)
	{
		return Utils.AbsPath(p);
	}

	private static void _restore_file(string chatId, PyDict entry)
	{
		string p = Py.Str(entry.Req("path"));
		byte[] bytes = File.ReadAllBytes(Abs(PPath.Join(_files_dir(chatId), Py.Str(entry.Req("sha256")) + ".bin")));
		Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(Abs(p))));
		File.WriteAllBytes(Abs(p), bytes);
		try
		{
			double num = Py.Float(Py.Or(entry["mtime"], 0L));
			if (num > 0.0)
			{
				File.SetLastWriteTimeUtc(Abs(p), new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks((long)(num * 10000000.0)));
			}
		}
		catch (Exception)
		{
		}
	}

	private static bool IsSymlink(string abs)
	{
		try
		{
			return (File.GetAttributes(abs) & FileAttributes.ReparsePoint) != 0;
		}
		catch (Exception)
		{
			return false;
		}
	}

	private static bool _remove_file(string path)
	{
		try
		{
			string text = Abs(path);
			if (Directory.Exists(text) && !IsSymlink(text))
			{
				Directory.Delete(text, recursive: true);
				return true;
			}
			if (File.Exists(text) || IsSymlink(text))
			{
				File.Delete(text);
			}
			return true;
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
		{
			return false;
		}
	}

	private static void _sweep_new_entries(string bas, HashSet<string> knownFiles, HashSet<string> knownDirs)
	{
		string path = Abs(bas);
		if (!Directory.Exists(path))
		{
			return;
		}
		try
		{
			string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
			List<string> list = (from e in Directory.EnumerateFileSystemEntries(full, "*", SearchOption.AllDirectories)
				select PPath.Join(bas, e.Substring(full.Length + 1))).ToList();
			list.Sort(string.CompareOrdinal);
			list.Reverse();
			foreach (string item in list)
			{
				string path2 = Abs(item);
				if (File.Exists(path2))
				{
					if (!knownFiles.Contains(item))
					{
						_remove_file(item);
					}
				}
				else if (Directory.Exists(path2) && !knownDirs.Contains(item))
				{
					try
					{
						Directory.Delete(path2, recursive: true);
					}
					catch (Exception)
					{
					}
				}
			}
		}
		catch (Exception ex2) when (ex2 is IOException || ex2 is UnauthorizedAccessException)
		{
		}
	}

	private static long _apply_op(string chatId, PyDict op)
	{
		List<PyDict> list = Py.ListOr(Py.Or(op["files"], new List<object>())).Cast<PyDict>().ToList();
		HashSet<string> knownFiles = new HashSet<string>(list.Select((PyDict f) => Py.Str(f.Req("path"))));
		HashSet<string> hashSet = ((!(op["known_dirs"] is List<object> { Count: >0 } list2)) ? new HashSet<string>(from f in list
			where Py.Truthy(f["sha256"])
			select new PPath(Py.Str(f["path"])).Parent.ToString()) : new HashSet<string>(list2.Select(Py.Str)));
		foreach (PyDict item in list.Where((PyDict f) => Py.Truthy(f["sha256"])))
		{
			hashSet.Add(new PPath(Py.Str(item["path"])).Parent.ToString());
		}
		long num = 0L;
		foreach (object item2 in Py.ListOr(Py.Or(op["dirs"], new List<object>())))
		{
			_sweep_new_entries(Py.Str(item2), knownFiles, hashSet);
		}
		foreach (PyDict item3 in list)
		{
			if (!Py.Truthy(item3["sha256"]))
			{
				if (!_remove_file(Py.Str(item3.Req("path"))))
				{
					num++;
				}
				continue;
			}
			try
			{
				_restore_file(chatId, item3);
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is KeyError || ex is ValueError)
			{
				num++;
			}
		}
		return num;
	}

	private static object Seq(object op)
	{
		return (op as PyDict)?["seq"];
	}

	public static PyDict Rollback(string chatId, string turnId = "", bool allOps = false)
	{
		chatId = (Py.Truthy(chatId) ? chatId : "active");
		lock (_LOCK)
		{
			List<object> list = _load_journal(chatId);
			if (list.Count == 0)
			{
				return PyDict.Of("ok", true, "restored_files", 0L, "restored_bytes", 0L);
			}
			List<object> targetSeqs;
			if (allOps)
			{
				targetSeqs = list.Select(Seq).ToList();
			}
			else if (Py.Truthy(turnId))
			{
				targetSeqs = list.Where((object o) => Py.Eq((o as PyDict)?["turn_id"], turnId)).Select(Seq).ToList();
			}
			else
			{
				object lastTurn = Py.Or(((PyDict)list[list.Count - 1])["turn_id"], "");
				targetSeqs = list.Where((object o) => Py.Eq((o as PyDict)?["turn_id"], lastTurn)).Select(Seq).ToList();
			}
			if (targetSeqs.Count == 0)
			{
				return PyDict.Of("ok", true, "restored_files", 0L, "restored_bytes", 0L);
			}
			List<object> items = list.Where(InT).ToList();
			List<object> first = list.Where((object o) => !InT(o)).ToList();
			long num = 0L;
			long num2 = 0L;
			long num3 = 0L;
			List<object> list2 = new List<object>();
			foreach (PyDict item in Py.Sorted(items, (object o) => Py.Or(Seq(o), 0L), reverse: true))
			{
				try
				{
					long num4 = _apply_op(chatId, item);
					num3 += num4;
					if (num4 != 0L)
					{
						list2.Add(item);
						continue;
					}
					num += Py.ListOr(Py.Or(item["files"], new List<object>())).Count;
					num2 += Py.Int(Py.Or(item["bytes"], 0L));
				}
				catch (Exception)
				{
					num3++;
					list2.Add(item);
				}
			}
			first = Py.Sorted(first.Concat(list2), (object o) => Py.Or(Seq(o), 0L));
			_save_journal(chatId, first);
			_gc_blobs(chatId, first);
			return PyDict.Of("ok", num3 == 0, "restored_files", num, "restored_bytes", num2, "failures", num3);
			bool InT(object o)
			{
				return targetSeqs.Any((object s) => Py.Eq(s, Seq(o)));
			}
		}
	}

	private static void _gc_blobs(string chatId, List<object> ops)
	{
		HashSet<string> hashSet = new HashSet<string>(from f in ops.SelectMany((object o) => Py.ListOr(Py.Or(((PyDict)o)["files"], new List<object>())))
			select Py.Str(((PyDict)f)["sha256"]));
		try
		{
			foreach (string item in Directory.EnumerateFiles(Abs(_files_dir(chatId)), "*.bin"))
			{
				if (!hashSet.Contains(Path.GetFileNameWithoutExtension(item)))
				{
					File.Delete(item);
				}
			}
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
		{
		}
	}

	private static (List<string> files, bool truncated) _iter_files(string path)
	{
		if (LocalFs.IsFile(path))
		{
			return (files: new List<string> { path }, truncated: false);
		}
		if (!LocalFs.IsDir(path))
		{
			return (files: new List<string>(), truncated: false);
		}
		List<string> list = new List<string>();
		foreach (string item in AiFs.RglobSorted(path))
		{
			if (LocalFs.IsFile(item))
			{
				list.Add(item);
				if (list.Count >= 20000)
				{
					return (files: list, truncated: true);
				}
			}
		}
		return (files: list, truncated: false);
	}

	private static List<string> _iter_dirs(string path)
	{
		if (!LocalFs.IsDir(path))
		{
			return new List<string>();
		}
		return AiFs.RglobSorted(path).Where(LocalFs.IsDir).ToList();
	}

	private static bool _store_blob(string chatId, byte[] data, string sha)
	{
		try
		{
			string text = _files_dir(chatId);
			string p = PPath.Join(text, sha + ".bin");
			if (!LocalFs.Exists(p))
			{
				string p2 = PPath.Join(text, ".tmp-" + sha.Substring(0, 12) + "-" + Py.FloatRepr(Py.Time()));
				File.WriteAllBytes(Abs(p2), data);
				Utils.OsReplace(Abs(p2), Abs(p));
			}
			return true;
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
		{
			return false;
		}
	}

	private static PyDict Fail(long seq, string reason)
	{
		return PyDict.Of("ok", false, "seq", seq, "files", 0L, "bytes", 0L, "reason", reason);
	}

	public static PyDict Snapshot(string chatId, string turnId, IEnumerable<string> paths)
	{
		chatId = (string.IsNullOrEmpty(chatId) ? "active" : chatId);
		try
		{
			lock (_LOCK)
			{
				List<object> list = _load_journal(chatId);
				List<object> list2 = new List<object>();
				List<object> list3 = new List<object>();
				List<string> list4 = new List<string>();
				long num = 0L;
				foreach (string item in paths ?? Enumerable.Empty<string>())
				{
					string text;
					try
					{
						text = new PPath(item).Resolve().ToString();
					}
					catch (Exception)
					{
						continue;
					}
					bool num2 = LocalFs.IsDir(text);
					if (num2)
					{
						list3.Add(text);
						list4.Add(text);
						list4.AddRange(_iter_dirs(text));
					}
					if (!num2 && !LocalFs.Exists(text))
					{
						list2.Add(PyDict.Of("path", text, "sha256", "", "size", 0L, "mtime", 0L));
						continue;
					}
					(List<string> files, bool truncated) tuple = _iter_files(text);
					var (list5, _) = tuple;
					if (tuple.truncated)
					{
						_gc_blobs(chatId, list);
						return Fail(list.Count, "目录文件数超过 " + 20000 + "，本轮不可回滚");
					}
					foreach (string item2 in list5)
					{
						byte[] array;
						try
						{
							if (!LocalFs.IsFile(item2))
							{
								continue;
							}
							array = File.ReadAllBytes(Abs(item2));
							goto IL_01e2;
						}
						catch (Exception ex2) when (ex2 is IOException || ex2 is UnauthorizedAccessException)
						{
							_gc_blobs(chatId, list);
							return Fail(list.Count, "无法读取 " + item2);
						}
						IL_01e2:
						string text2;
						using (SHA256 sHA = SHA256.Create())
						{
							text2 = string.Concat(sHA.ComputeHash(array).Select(delegate(byte b)
							{
								byte b2 = b;
								return b2.ToString("x2");
							}));
						}
						if (!_store_blob(chatId, array, text2))
						{
							_gc_blobs(chatId, list);
							return Fail(list.Count, "检查点写入失败（磁盘满或权限不足）");
						}
						num += array.Length;
						list2.Add(PyDict.Of("path", item2, "sha256", text2, "size", (long)array.Length, "mtime", AiFs.MTime(item2)));
						if (num > 536870912)
						{
							_gc_blobs(chatId, list);
							return Fail(list.Count, "变更体积超过检查点上限");
						}
					}
				}
				long num3 = list.Count;
				if (list2.Count > 0 || list3.Count > 0)
				{
					List<string> list6 = list4.Distinct().ToList();
					list6.Sort(string.CompareOrdinal);
					list.Add(PyDict.Of("seq", num3, "turn_id", turnId ?? "", "ts", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"), "files", list2, "dirs", list3, "known_dirs", list6.Cast<object>().ToList(), "bytes", num));
					_save_journal(chatId, list);
				}
				return PyDict.Of("ok", true, "seq", num3, "files", (long)list2.Count, "bytes", num, "reason", "");
			}
		}
		catch (Exception)
		{
			return Fail(-1L, "检查点写入失败");
		}
	}

	public static void BeginChat(string chatId)
	{
		try
		{
			chatId = (string.IsNullOrEmpty(chatId) ? "active" : chatId);
			lock (_LOCK)
			{
				List<object> list = _load_journal(chatId);
				long num = list.Sum((object o) => Py.Int(Py.Or(((PyDict)o).Get("bytes"), 0L)));
				List<object> list2 = new List<object>();
				for (int num2 = list.Count - 1; num2 >= 0; num2--)
				{
					if (num > 1073741824)
					{
						num -= Py.Int(Py.Or(((PyDict)list[num2]).Get("bytes"), 0L));
					}
					else
					{
						list2.Insert(0, list[num2]);
					}
				}
				if (list2.Count != list.Count)
				{
					_save_journal(chatId, list2);
					_gc_blobs(chatId, list2);
				}
				foreach (string item in (from d in LocalFs.Children(_root()).Where(LocalFs.IsDir).ToList()
					orderby AiFs.MTime(d) descending
					select d).ToList().Skip(20))
				{
					try
					{
						Directory.Delete(Abs(item), recursive: true);
					}
					catch (Exception)
					{
					}
				}
			}
		}
		catch (Exception)
		{
		}
	}

	public static string LastTurnId(string chatId)
	{
		List<object> list = _load_journal(Py.Truthy(chatId) ? chatId : "active");
		if (list.Count <= 0)
		{
			return "";
		}
		return Py.Str(Py.Or(((PyDict)list[list.Count - 1])["turn_id"], ""));
	}

	public static bool TurnHasOps(string chatId, string turnId)
	{
		string tid = turnId ?? "";
		if (tid.Length > 0)
		{
			return _load_journal(Py.Truthy(chatId) ? chatId : "active").Any((object o) => Py.Eq(((PyDict)o)["turn_id"], tid));
		}
		return false;
	}
}
