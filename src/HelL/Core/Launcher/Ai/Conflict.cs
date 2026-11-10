using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace HelL.Core.Launcher.Ai;

public static class Conflict
{
	private static readonly Regex _DEP_RE = new Regex("\\[\\[dependencies\\.([^\\]]+)\\]\\]", RegexOptions.CultureInvariant);

	private static readonly HashSet<string> _SKIP_DEP = new HashSet<string> { "minecraft", "java", "forge", "neoforge", "fabricloader", "fabric-loader", "quilt_loader", "quilt-loader" };

	private static string _read_zip_text(ZipArchive zf, string name)
	{
		try
		{
			foreach (ZipArchiveEntry entry in zf.Entries)
			{
				if (Utils.ZipEntryName(entry) != name)
				{
					continue;
				}
				using Stream stream = entry.Open();
				using MemoryStream memoryStream = new MemoryStream();
				stream.CopyTo(memoryStream);
				return Encoding.UTF8.GetString(memoryStream.ToArray());
			}
		}
		catch (Exception)
		{
		}
		return "";
	}

	private static PyDict _load_toml(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return null;
		}
		try
		{
			return TomlLite.Parse(text);
		}
		catch (Exception)
		{
			return null;
		}
	}

	private static object[] _kv_line(string line)
	{
		if (!line.Contains("="))
		{
			return new object[2];
		}
		List<string> list = Py.Split(line, "=", 1);
		string text = Py.Strip(list[0]);
		object obj = Py.Strip(Py.Strip(Py.Strip(list[1]), "\""), "'");
		string s = (string)obj;
		if (Py.Lower(s) == "true" || Py.Lower(s) == "false")
		{
			obj = Py.Lower(s) == "true";
		}
		return new object[2] { text, obj };
	}

	private static PyDict _parse_mods_toml_fallback(string text)
	{
		List<object> list = new List<object>();
		List<object> list2 = new List<object>();
		string text2 = null;
		PyDict pyDict = null;
		string text3 = "";
		foreach (string item in Py.SplitLines(text))
		{
			string text4 = Py.Strip(item);
			if (text4.Length == 0 || text4.StartsWith("#"))
			{
				continue;
			}
			if (text4.StartsWith("[[mods]]"))
			{
				if (pyDict != null && text2 == "mod")
				{
					list.Add(pyDict);
				}
				pyDict = new PyDict();
				text2 = "mod";
				continue;
			}
			Match match = _DEP_RE.Match(text4);
			if (match.Success && match.Index == 0)
			{
				if (pyDict != null && text2 == "mod")
				{
					list.Add(pyDict);
					pyDict = null;
				}
				list2.Add(PyDict.Of("owner", match.Groups[1].Value));
				text2 = "dep";
				continue;
			}
			object[] array = _kv_line(text4);
			if (array[0] != null)
			{
				string text5 = (string)array[0];
				object obj = array[1];
				if (text5 == "modLoader" && text2 != "dep")
				{
					text3 = Py.Str(obj);
				}
				if (text2 == "mod" && pyDict != null)
				{
					pyDict[text5] = obj;
				}
				else if (text2 == "dep" && list2.Count > 0)
				{
					((PyDict)list2[list2.Count - 1])[text5] = obj;
				}
			}
		}
		if (pyDict != null && text2 == "mod")
		{
			list.Add(pyDict);
		}
		return PyDict.Of("modLoader", text3, "mods", list, "deps", list2);
	}

	private static PyDict _from_forge_toml(string text, string flavor)
	{
		PyDict pyDict = _load_toml(text);
		List<PyDict> list = new List<PyDict>();
		List<PyDict> list2 = new List<PyDict>();
		object obj = flavor;
		if (Py.Truthy(pyDict))
		{
			obj = Py.Or(pyDict.Get("modLoader"), flavor);
			foreach (object item2 in (Py.Or(pyDict.Get("mods"), new List<object>()) as IEnumerable) ?? new List<object>())
			{
				if (item2 is PyDict item)
				{
					list.Add(item);
				}
			}
			if (Py.Or(pyDict.Get("dependencies"), new PyDict()) is PyDict pyDict2)
			{
				foreach (KeyValuePair<string, object> item3 in pyDict2)
				{
					foreach (object item4 in (item3.Value as List<object>) ?? new List<object> { item3.Value })
					{
						if (item4 is PyDict other)
						{
							PyDict pyDict3 = new PyDict(other);
							pyDict3["owner"] = item3.Key;
							list2.Add(pyDict3);
						}
					}
				}
			}
		}
		else
		{
			PyDict pyDict4 = _parse_mods_toml_fallback(text);
			obj = Py.Or(pyDict4.Get("modLoader"), flavor);
			list = ((List<object>)pyDict4["mods"]).Cast<PyDict>().ToList();
			list2 = ((List<object>)pyDict4["deps"]).Cast<PyDict>().ToList();
		}
		PyDict pyDict5 = ((list.Count > 0) ? list[0] : new PyDict());
		List<object> list3 = new List<object>();
		List<object> list4 = new List<object>();
		string text2 = Py.Str(Py.Or(pyDict5.Get("modId"), ""));
		foreach (PyDict item5 in list2)
		{
			string text3 = Py.Str(Py.Or(item5.Get("owner"), text2));
			if (text2.Length > 0 && text3 != text2 && text3 != "")
			{
				continue;
			}
			PyDict pyDict6 = PyDict.Of("id", Py.Str(Py.Or(item5.Get("modId"), "")), "mandatory", Py.Truthy(item5.Get("mandatory", true)), "version", Py.Str(Py.Or(item5.Get("versionRange"), item5.Get("version"), "*")));
			switch (Py.Lower(Py.Str(Py.Or(item5.Get("type"), ""))))
			{
			case "incompatible":
			case "broke":
			case "break":
			case "breaks":
				list4.Add(pyDict6);
				continue;
			}
			if ((bool)pyDict6["mandatory"])
			{
				list3.Add(pyDict6);
			}
		}
		return PyDict.Of("id", Py.Str(Py.Or(pyDict5.Get("modId"), "")), "name", Py.Str(Py.Or(pyDict5.Get("displayName"), pyDict5.Get("modId"), "")), "version", Py.Str(Py.Or(pyDict5.Get("version"), "")), "loader", Py.Lower(Py.Str(obj)).Contains("neo") ? "neoforge" : "forge", "depends", list3, "breaks", list4, "conflicts", new List<object>(), "provides", new List<object>());
	}

	private static List<object> _as_map(object val)
	{
		if (val is PyDict source)
		{
			return ((IEnumerable<KeyValuePair<string, object>>)source).Select((Func<KeyValuePair<string, object>, object>)((KeyValuePair<string, object> kv) => PyDict.Of("id", kv.Key, "version", Py.Str(kv.Value)))).ToList();
		}
		if (val is List<object> list)
		{
			List<object> list2 = new List<object>();
			{
				foreach (object item in list)
				{
					if (item is PyDict pyDict)
					{
						list2.Add(PyDict.Of("id", Py.Str(Py.Or(pyDict.Get("id"), pyDict.Get("mod"), "")), "version", Py.Str(Py.Or(pyDict.Get("versions"), pyDict.Get("version"), "*"))));
					}
					else if (Py.Truthy(item))
					{
						list2.Add(PyDict.Of("id", Py.Str(item), "version", "*"));
					}
				}
				return list2;
			}
		}
		return new List<object>();
	}

	private static PyDict _from_fabric(PyDict data, string loader)
	{
		return PyDict.Of("id", Py.Str(Py.Or(data.Get("id"), "")), "name", Py.Str(Py.Or(data.Get("name"), data.Get("id"), "")), "version", Py.Str(Py.Or(data.Get("version"), "")), "loader", loader, "depends", _as_map(Py.Or(data.Get("depends"), new PyDict())), "breaks", _as_map(Py.Or(data.Get("breaks"), new PyDict())), "conflicts", _as_map(Py.Or(data.Get("conflicts"), new PyDict())), "provides", Py.ListCtor(Py.Or(data.Get("provides"), new List<object>())));
	}

	private static PyDict _from_quilt(PyDict data)
	{
		PyDict pyDict = (data.Get("quilt_loader") as PyDict) ?? data;
		List<object> list = new List<object>();
		List<object> list2 = new List<object>();
		foreach (object item in (Py.Or(pyDict.Get("depends"), new List<object>()) as IEnumerable) ?? new List<object>())
		{
			if (item is PyDict pyDict2)
			{
				list.Add(PyDict.Of("id", Py.Str(Py.Or(pyDict2.Get("id"), "")), "version", Py.Str(Py.Or(pyDict2.Get("versions"), "*"))));
			}
		}
		foreach (object item2 in (Py.Or(pyDict.Get("breaks"), new List<object>()) as IEnumerable) ?? new List<object>())
		{
			if (item2 is PyDict pyDict3)
			{
				list2.Add(PyDict.Of("id", Py.Str(Py.Or(pyDict3.Get("id"), "")), "version", Py.Str(Py.Or(pyDict3.Get("versions"), "*"))));
			}
		}
		return PyDict.Of("id", Py.Str(Py.Or(pyDict.Get("id"), data.Get("id"), "")), "name", Py.Str(Py.Or(Py.DictOr(Py.Or(pyDict.Get("metadata"), new PyDict())).Get("name"), pyDict.Get("id"), "")), "version", Py.Str(Py.Or(pyDict.Get("version"), "")), "loader", "quilt", "depends", list, "breaks", list2, "conflicts", new List<object>(), "provides", Py.ListCtor(Py.Or(pyDict.Get("provides"), new List<object>())));
	}

	public static PyDict InspectJar(string path)
	{
		PPath pPath = new PPath(path);
		PyDict pyDict = PyDict.Of("file", pPath.Name, "id", pPath.Stem, "name", pPath.Stem, "version", "", "loader", "unknown", "depends", new List<object>(), "breaks", new List<object>(), "conflicts", new List<object>(), "provides", new List<object>(), "enabled", !Py.Lower(pPath.Name).EndsWith(".disabled"));
		try
		{
			using ZipArchive zipArchive = Utils.OpenZip(path);
			HashSet<string> hashSet = new HashSet<string>(zipArchive.Entries.Select(Utils.ZipEntryName));
			if (hashSet.Contains("fabric.mod.json"))
			{
				pyDict.Update(_from_fabric(AsDictOrAttr(PyJson.Loads(Py.Or(_read_zip_text(zipArchive, "fabric.mod.json"), "{}") as string)), "fabric"));
			}
			else if (hashSet.Contains("quilt.mod.json"))
			{
				pyDict.Update(_from_quilt(AsDictOrAttr(PyJson.Loads(Py.Or(_read_zip_text(zipArchive, "quilt.mod.json"), "{}") as string))));
			}
			else if (hashSet.Contains("META-INF/neoforge.mods.toml"))
			{
				pyDict.Update(_from_forge_toml(_read_zip_text(zipArchive, "META-INF/neoforge.mods.toml"), "neoforge"));
			}
			else if (hashSet.Contains("META-INF/mods.toml"))
			{
				pyDict.Update(_from_forge_toml(_read_zip_text(zipArchive, "META-INF/mods.toml"), "forge"));
			}
			else if (hashSet.Contains("mcmod.info"))
			{
				object obj = PyJson.Loads(Py.Or(_read_zip_text(zipArchive, "mcmod.info"), "[]") as string);
				PyDict pyDict2 = ((obj is List<object> { Count: >0 } list) ? AsDictOrAttr(list[0]) : ((obj as PyDict) ?? new PyDict()));
				pyDict.Update(PyDict.Of("id", Py.Str(Py.Or(pyDict2.Get("modid"), pyDict["id"])), "name", Py.Str(Py.Or(pyDict2.Get("name"), pyDict["name"])), "version", Py.Str(Py.Or(pyDict2.Get("version"), "")), "loader", "forge", "depends", StrIter(pyDict2.Get("requiredMods")).Select<string, object>((Func<string, object>)((string x) => PyDict.Of("id", x, "version", "*"))).ToList()));
			}
		}
		catch (Exception e)
		{
			pyDict["error"] = Py.ExcStr(e);
		}
		pyDict["file"] = pPath.Name;
		pyDict["enabled"] = !Py.Lower(pPath.Name).EndsWith(".disabled");
		return pyDict;
	}

	private static IEnumerable<string> StrIter(object o)
	{
		foreach (object item in (Py.Or(o, new List<object>()) as IEnumerable) ?? new List<object>())
		{
			yield return Py.Str(item);
		}
	}

	private static PyDict AsDictOrAttr(object o)
	{
		if (o is PyDict result)
		{
			return result;
		}
		throw new AttributeError("'" + Py.TypeName(o) + "' object has no attribute 'get'");
	}

	private static List<string> _mod_files(Instance instance)
	{
		string text = PPath.Join(instance.path, "mods");
		if (!LocalFs.IsDir(text))
		{
			return new List<string>();
		}
		List<string> list = new List<string>();
		foreach (string item in LocalFs.SortedChildren(text))
		{
			string text2 = Py.Lower(new PPath(item).Name);
			if ((text2.EndsWith(".jar") || text2.EndsWith(".jar.disabled") || text2.EndsWith(".disabled")) && LocalFs.IsFile(item))
			{
				list.Add(item);
			}
		}
		if (list.Count == 0)
		{
			list = Mods.ListInstanceMods(instance);
		}
		return list;
	}

	private static string L(object o)
	{
		return Py.Lower(Py.S(o));
	}

	public static PyDict ScanConflicts(Instance instance)
	{
		string text = Mods.DetectLoader(instance);
		string text2 = Mods.DetectMcVersion(instance);
		List<PyDict> list = _mod_files(instance).Select(InspectJar).ToList();
		PyDict pyDict = new PyDict();
		List<object> list2 = new List<object>();
		foreach (PyDict item in list)
		{
			string text3 = L(item.Get("id"));
			if (text3.Length != 0)
			{
				if (!pyDict.ContainsKey(text3))
				{
					pyDict[text3] = new List<object>();
				}
				((List<object>)pyDict[text3]).Add(item);
			}
		}
		foreach (KeyValuePair<string, object> item2 in pyDict)
		{
			List<PyDict> list3 = (from PyDict g in (List<object>)item2.Value
				where Py.Truthy(g.Get("enabled"))
				select g).ToList();
			if (list3.Count > 1)
			{
				list2.Add(PyDict.Of("type", "duplicate_id", "severity", "error", "id", item2.Key, "files", list3.Select((PyDict g) => g.Req("file")).ToList(), "message", "模组 " + item2.Key + " 装了 " + list3.Count + " 份"));
			}
		}
		HashSet<string> hashSet = new HashSet<string>(pyDict.Keys);
		foreach (PyDict item3 in list)
		{
			if (!Py.Truthy(item3.Get("enabled")))
			{
				continue;
			}
			string text4 = Py.Lower(Py.Str(Py.Or(item3.Get("loader"), "unknown")));
			if (!string.IsNullOrEmpty(text) && text4 != "unknown" && text4 != "" && text4 != text && (!(text == "quilt") || !(text4 == "fabric")))
			{
				list2.Add(PyDict.Of("type", "loader_mismatch", "severity", "error", "id", item3.Get("id"), "file", item3.Get("file"), "message", Py.Str(item3.Get("file")) + " 是 " + text4 + " 模组，当前实例是 " + text));
			}
			foreach (object item4 in Py.ListOr(Py.Or(item3.Get("depends"), new List<object>())))
			{
				string text5 = L(Agent.DGet(item4, "id"));
				if (text5.Length == 0 || _SKIP_DEP.Contains(text5))
				{
					continue;
				}
				switch (text5)
				{
				case "fabric-api":
				case "fabricapi":
				case "fabric":
					if (!hashSet.Contains("fabric-api") && !hashSet.Contains("fabricapi"))
					{
						list2.Add(PyDict.Of("type", "missing_dep", "severity", "error", "id", item3.Get("id"), "need", "fabric-api", "file", item3.Get("file"), "message", Py.Str(Py.Or(item3.Get("name"), item3.Get("file"))) + " 需要 Fabric API"));
					}
					break;
				default:
					if (!hashSet.Contains(text5))
					{
						list2.Add(PyDict.Of("type", "missing_dep", "severity", "error", "id", item3.Get("id"), "need", text5, "file", item3.Get("file"), "message", Py.Str(Py.Or(item3.Get("name"), item3.Get("file"))) + " 缺少依赖 " + text5));
					}
					break;
				}
			}
			foreach (object item5 in Py.ListOr(Py.Or(item3.Get("breaks"), new List<object>())).Concat(Py.ListOr(Py.Or(item3.Get("conflicts"), new List<object>()))))
			{
				string text6 = L(Agent.DGet(item5, "id"));
				if (text6.Length > 0 && hashSet.Contains(text6))
				{
					list2.Add(PyDict.Of("type", "breaks", "severity", "error", "id", item3.Get("id"), "other", text6, "file", item3.Get("file"), "message", Py.Str(item3.Get("id")) + " 与 " + text6 + " 不兼容"));
				}
			}
		}
		return PyDict.Of("instance", instance.name, "loader", text, "mc_version", text2, "mod_count", (long)list.Count, "enabled", (long)list.Count((PyDict m) => Py.Truthy(m.Get("enabled"))), "mods", ((IEnumerable<PyDict>)list).Select((Func<PyDict, object>)((PyDict m) => PyDict.Of("file", m.Get("file"), "id", m.Get("id"), "name", m.Get("name"), "version", m.Get("version"), "loader", m.Get("loader"), "enabled", m.Get("enabled"), "depends", (from d in Py.ListOr(Py.Or(m.Get("depends"), new List<object>()))
			select Agent.DGet(d, "id")).Take(12).ToList()))).ToList(), "issues", list2, "issue_count", (long)list2.Count);
	}
}
