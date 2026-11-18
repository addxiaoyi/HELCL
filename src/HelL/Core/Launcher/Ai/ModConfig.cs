using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HelL.Core.Launcher.Ai;

public static class ModConfig
{
	private static readonly HashSet<string> _OK_SUFFIX = new HashSet<string> { ".toml", ".json", ".json5", ".cfg", ".txt", ".properties", ".conf", ".ini", ".snbt" };

	private const long _MAX_BYTES = 524288L;

	private const int _MAX_LIST = 80;

	private static PPath _config_root(Instance instance)
	{
		PPath pPath = new PPath(PPath.Join(instance.path, "config")).Resolve();
		Directory.CreateDirectory(pPath.ToString());
		return pPath;
	}

	private static PPath _safe(Instance instance, string rel)
	{
		PPath pPath = _config_root(instance);
		rel = Py.LStrip((rel ?? "").Replace("\\", "/"), "/");
		if (rel.Length == 0 || new PPath(rel).Parts.Contains(".."))
		{
			throw new ConfigEditError("非法配置路径");
		}
		PPath pPath2 = (pPath / rel).Resolve();
		if (!pPath2.Equals(pPath) && !pPath2.IsRelativeTo(pPath.ToString()))
		{
			throw new ConfigEditError("路径必须在 config 目录内");
		}
		return pPath2;
	}

	public static List<object> ListConfigs(Instance instance, string prefix = "")
	{
		PPath pPath = _config_root(instance);
		PPath pPath2 = ((!string.IsNullOrEmpty(prefix)) ? _safe(instance, prefix) : pPath);
		if (pPath2.IsFile())
		{
			List<object> list = new List<object>();
			list.Add(PyDict.Of("path", pPath2.RelativeTo(pPath.ToString()).ToString().Replace("\\", "/"), "bytes", new FileInfo(pPath2.Abs()).Length));
			return list;
		}
		List<object> list2 = new List<object>();
		if (!pPath2.IsDir())
		{
			return list2;
		}
		foreach (string item in AiFs.RglobSorted(pPath2.ToString()))
		{
			PPath pPath3 = new PPath(item);
			if (pPath3.IsFile() && _OK_SUFFIX.Contains(pPath3.Suffix.ToLowerInvariant()))
			{
				list2.Add(PyDict.Of("path", pPath3.RelativeTo(pPath.ToString()).ToString().Replace("\\", "/"), "bytes", new FileInfo(pPath3.Abs()).Length));
				if (list2.Count >= 80)
				{
					break;
				}
			}
		}
		return list2;
	}

	public static string ReadConfig(Instance instance, object rel, int maxChars = 8000)
	{
		PPath pPath = _safe(instance, Py.AsStr(rel ?? "", "replace"));
		if (!pPath.IsFile())
		{
			throw new ConfigEditError("配置不存在: " + Py.Str(rel));
		}
		if (new FileInfo(pPath.Abs()).Length > 524288)
		{
			throw new ConfigEditError("配置文件过大");
		}
		string text = AiFs.ReadText(pPath.ToString(), replace: true);
		if (text.Length > maxChars)
		{
			return Py.Slice(text, null, maxChars) + "\n…(已截断)";
		}
		return text;
	}

	public static string WriteConfig(Instance instance, object rel, string content)
	{
		PPath pPath = _safe(instance, Py.AsStr(rel ?? "", "replace"));
		if (!_OK_SUFFIX.Contains(pPath.Suffix.ToLowerInvariant()))
		{
			throw new ConfigEditError("不允许写这种后缀: " + pPath.Suffix);
		}
		byte[] bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(content);
		if ((long)bytes.Length > 524288L)
		{
			throw new ConfigEditError("写入内容过大");
		}
		Directory.CreateDirectory(pPath.Parent.Abs());
		PPath pPath2 = pPath.WithName(pPath.Name + ".bak");
		if (pPath.IsFile())
		{
			File.WriteAllBytes(pPath2.Abs(), File.ReadAllBytes(pPath.Abs()));
		}
		File.WriteAllBytes(pPath.Abs(), bytes);
		return "已写入 " + Py.Str(rel);
	}
}
