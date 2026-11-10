using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace HelL.Core.Launcher;

public static class VersionOps
{
	private static readonly Regex _ILLEGAL = new Regex("[\\\\/:*?\"<>|\\x00-\\x1f]");

	public static string SanitizeId(object raw)
	{
		string input = _ILLEGAL.Replace(Py.SS(raw), "-");
		input = Py.Strip(Regex.Replace(input, "\\s+", " "), " .");
		if (input.Length == 0)
		{
			throw new VersionOpError("版本名不能为空");
		}
		if (input.Length > 64)
		{
			input = Py.RStrip(input.Substring(0, 64), " .");
		}
		return input;
	}

	public static string UniqueId(Instance instance, object raw)
	{
		string text = SanitizeId(raw);
		string text2 = instance.VersionsDir();
		string text3 = text;
		int num = 2;
		while (LocalFs.Exists(PPath.Join(text2, text3)))
		{
			string text4 = "-" + num;
			string text5 = Py.RStrip(text.Substring(0, Math.Min(text.Length, Math.Max(1, 64 - text4.Length))), " .");
			if (text5.Length == 0)
			{
				text5 = text;
			}
			text3 = text5 + text4;
			num++;
		}
		return text3;
	}

	private static string _vdir(Instance instance, object version_id)
	{
		string text;
		try
		{
			text = Utils.SafeChildPath(instance.VersionsDir(), Utils.SafeVersionId(version_id));
		}
		catch (PathEscapeError pathEscapeError)
		{
			throw new VersionOpError(pathEscapeError.Message);
		}
		if (!LocalFs.IsDir(text))
		{
			throw new VersionOpError(Py.Fmt("版本不存在: {0}", version_id));
		}
		return text;
	}

	private static string _checked_id(object raw)
	{
		try
		{
			return Utils.SafeVersionId(raw);
		}
		catch (PathEscapeError pathEscapeError)
		{
			throw new VersionOpError(pathEscapeError.Message);
		}
	}

	public static string RenameVersion(Instance instance, object old_id0, object new_id0)
	{
		string text = SanitizeId(new_id0);
		string text2 = _checked_id(old_id0);
		if (text == text2)
		{
			return text2;
		}
		string text3 = _vdir(instance, text2);
		string text4 = PPath.Join(instance.VersionsDir(), text);
		if (LocalFs.Exists(text4))
		{
			throw new VersionOpError(Py.Fmt("已存在版本 {0}", text));
		}
		LocalFs.CopyTree(text3, text4);
		try
		{
			_rewrite_ids(text4, text2, text);
			Utils.RemoveTree(text3);
			return text;
		}
		catch (Exception)
		{
			Utils.RemoveTree(text4);
			throw;
		}
	}

	public static string CopyVersion(Instance instance, object old_id, object new_id0)
	{
		string text = SanitizeId(new_id0);
		string src = _vdir(instance, old_id);
		string text2 = PPath.Join(instance.VersionsDir(), text);
		if (LocalFs.Exists(text2))
		{
			throw new VersionOpError(Py.Fmt("已存在版本 {0}", text));
		}
		LocalFs.CopyTree(src, text2);
		_rewrite_ids(text2, Py.Str(old_id), text);
		return text;
	}

	private static void _rewrite_ids(string dest, string old_id, string new_id)
	{
		foreach (string item in LocalFs.Children(dest))
		{
			PPath pPath = new PPath(item);
			if (LocalFs.IsFile(item) && pPath.Stem == old_id)
			{
				File.Move(PPath.Abs(item), PPath.Abs(PPath.Join(dest, new_id + pPath.Suffix)));
			}
		}
		string text = PPath.Join(dest, new_id + ".json");
		if (!LocalFs.IsFile(text))
		{
			string text2 = PPath.Join(dest, old_id + ".json");
			if (LocalFs.IsFile(text2))
			{
				File.Move(PPath.Abs(text2), PPath.Abs(text));
			}
		}
		if (Utils.ReadJson(text) is PyDict pyDict)
		{
			pyDict["id"] = new_id;
			Utils.WriteJson(text, pyDict);
		}
		string text3 = PPath.Join(dest, "tipcl.json");
		if (LocalFs.IsFile(text3))
		{
			object obj = Utils.ReadJson(text3, new PyDict());
			object obj2 = (Py.Truthy(obj) ? obj : new PyDict());
			((obj2 as PyDict) ?? throw new AttributeError("'" + Py.TypeName(obj2) + "' object has no attribute 'pop'")).Pop("hidden");
			Utils.WriteJson(text3, obj2);
		}
	}

	public static PyDict SetHidden(Instance instance, object version_id, object hidden)
	{
		_vdir(instance, version_id);
		return VersionSettings.Save(instance, version_id, PyDict.Of("hidden", Py.Truthy(hidden)));
	}

	public static bool IsHidden(Instance instance, object version_id)
	{
		return Py.Truthy(VersionSettings.Load(instance, version_id)["hidden"]);
	}

	public static PyDict FolderMap(Instance instance, object version_id0 = null)
	{
		object obj = version_id0;
		if (Py.Truthy(obj))
		{
			obj = _checked_id(obj);
		}
		bool flag = Py.Truthy(obj);
		PyDict settings = (flag ? VersionSettings.Load(instance, obj) : null);
		string text = (flag ? VersionSettings.GameDir(instance, obj, settings) : instance.path);
		string text2 = (flag ? PPath.Join(instance.VersionsDir(), obj) : instance.VersionsDir());
		return PyDict.Of("root", instance.path, "game", text, "mods", flag ? VersionSettings.ModsDir(instance, obj, settings) : PPath.Join(instance.path, "mods"), "saves", PPath.Join(text, "saves"), "screenshots", PPath.Join(text, "screenshots"), "resourcepacks", PPath.Join(text, "resourcepacks"), "shaderpacks", PPath.Join(text, "shaderpacks"), "datapacks", PPath.Join(text, "datapacks"), "logs", PPath.Join(text, "logs"), "crash-reports", PPath.Join(text, "crash-reports"), "version", text2);
	}

	public static string OpenFolder(Instance instance, object version_id = null, object which = null)
	{
		PyDict pyDict = FolderMap(instance, version_id);
		if (which is PyDict || which is List<object>)
		{
			throw new TypeError("unhashable type: '" + Py.TypeName(which) + "'");
		}
		string text = (string)Py.Or((which is string key) ? pyDict[key] : null, pyDict["root"]);
		PPath pPath = new PPath(instance.path).Resolve();
		PPath pPath2 = new PPath(text).Resolve();
		if (!pPath2.Equals(pPath) && !pPath2.IsRelativeTo(pPath.ToString()))
		{
			throw new VersionOpError(Py.Fmt("非法路径: {0}", text));
		}
		Utils.EnsureDir(text);
		if (!Crash.OpenPath(text))
		{
			throw new VersionOpError(Py.Fmt("无法打开: {0}", text));
		}
		return text;
	}

	public static string ExportLaunchBat(string dest, IEnumerable<object> cmd, object cwd)
	{
		Utils.EnsureDir(new PPath(dest).Parent.ToString());
		List<string> list = new List<string>
		{
			"@echo off",
			"chcp 65001 >nul",
			"cd /d \"" + Py.Str(cwd) + "\""
		};
		List<string> list2 = new List<string>();
		foreach (object item in cmd)
		{
			string text = Py.Str(item);
			if (text.IndexOfAny(" \t&|<>^\"".ToCharArray()) >= 0)
			{
				list2.Add("\"" + text.Replace("\"", "\\\"") + "\"");
			}
			else
			{
				list2.Add(text);
			}
		}
		list.Add(string.Join(" ", list2));
		list.Add("pause");
		string text2 = string.Join("\r\n", list) + "\r\n";
		if (Utils.IS_WINDOWS)
		{
			text2 = text2.Replace("\n", "\r\n");
		}
		File.WriteAllBytes(PPath.Abs(dest), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text2));
		return new PPath(dest).ToString();
	}
}
