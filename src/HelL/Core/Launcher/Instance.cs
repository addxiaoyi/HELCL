using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HelL.Core.Launcher;

public class Instance
{
	public string name;

	public string path;

	public bool is_root;

	private string Meta_Path => PPath.Join(path, ".instance.json");

	public Instance(object name = null) : this(name, false)
	{
	}

	public Instance(object name, bool forCreation)
	{
		if (name == null)
		{
			name = Config.CONFIG.Get("default_instance", "");
		}
		string text = forCreation ? Py.SS(name) : Instances.ResolveName(name);
		this.name = ((text.Length > 0) ? text : Instances.RootName());
		path = Instances.GetInstancePath(this.name);
		is_root = new PPath(path).Equals(new PPath(Config.CONFIG.instances_dir).Resolve());
	}

	public void Create(PyDict meta = null)
	{
		if (is_root)
		{
			EnsureStandardDirs();
			if (!LocalFs.IsFile(Meta_Path))
			{
				Utils.WriteJson(Meta_Path, NewMeta(meta));
			}
			return;
		}
		if (LocalFs.IsDir(path))
		{
			throw new InstanceError(Py.Fmt("实例 {0} 已存在。", name));
		}
		Utils.EnsureDir(path);
		string[] sTANDARD_DIRS = Instances._STANDARD_DIRS;
		foreach (string text in sTANDARD_DIRS)
		{
			Utils.EnsureDir(PPath.Join(path, text));
		}
		Utils.WriteJson(Meta_Path, NewMeta(meta));
	}

	public void Delete()
	{
		if (is_root)
		{
			throw new InstanceError("游戏目录不能删除，请到「版本管理」里逐个卸载版本。");
		}
		if (!LocalFs.IsDir(path))
		{
			throw new InstanceError(Py.Fmt("实例 {0} 不存在。", name));
		}
		Utils.RemoveTree(path);
        UpdateHomeReferences(null);
	}

	public void Rename(object new_name)
	{
		if (is_root)
		{
			throw new InstanceError("游戏目录不能重命名，请到设置里改「游戏目录」。");
		}
		string instancePath = Instances.GetInstancePath(new_name);
		if (LocalFs.Exists(instancePath))
		{
			throw new InstanceError(Py.Fmt("实例 {0} 已存在。", new_name));
		}
		if (!LocalFs.Exists(path))
		{
			throw new FileNotFoundError("[Errno 2] No such file or directory: " + Py.Repr(path) + " -> " + Py.Repr(instancePath));
		}
		if (LocalFs.IsDir(path))
		{
			Directory.Move(PPath.Abs(path), PPath.Abs(instancePath));
		}
		else
		{
			File.Move(PPath.Abs(path), PPath.Abs(instancePath));
		}
        UpdateHomeReferences(Py.Str(new_name));
		name = Py.Str(new_name);
		path = instancePath;
		SetMeta("name", new_name);
	}

    private void UpdateHomeReferences(string replacement)
    {
        lock (Config.CONFIG._lock)
        {
            bool changed = false;
            if (Py.Eq(Config.CONFIG.Get("default_instance"), name))
            {
                Config.CONFIG.Set("default_instance", replacement ?? ""); changed = true;
            }
            var pins = Py.ListOr(Config.CONFIG.Get("ui_pinned_instances")).Select(Py.S).ToList();
            if (pins.Contains(name))
            {
                var updated = pins.Select(pin => pin == name ? replacement : pin).Where(pin => pin != null).Distinct().Cast<object>().ToList();
                Config.CONFIG.Set("ui_pinned_instances", updated); changed = true;
            }
            if (changed) Config.CONFIG.Save();
        }
    }

	private PyDict NewMeta(PyDict meta)
	{
		PyDict pyDict = PyDict.Of("name", name, "mc_version", null, "modpack", null, "java", "自动选择");
		if (meta != null)
		{
			foreach (KeyValuePair<string, object> metum in meta)
			{
				pyDict[metum.Key] = metum.Value;
			}
		}
		return pyDict;
	}

	public PyDict Meta()
	{
		object obj = Utils.ReadJson(Meta_Path, new PyDict());
		if (!Py.Truthy(obj))
		{
			return new PyDict();
		}
		return (obj as PyDict) ?? new PyDict();
	}

	public void SetMeta(string key, object value)
	{
		PyDict pyDict = Meta();
		pyDict[key] = value;
		Utils.WriteJson(Meta_Path, pyDict);
	}

	public string JavaPref()
	{
		object obj = Meta()["java"];
		if (obj == null)
		{
			return "自动选择";
		}
		string text = Py.Strip(Py.Str(obj));
		if (text == null || text.Length != 0)
		{
			switch (text)
			{
			case "自动选择":
			case "auto":
			case "default":
				break;
			default:
				return text;
			}
		}
		return "自动选择";
	}

	public void SetJavaPref(object value)
	{
		string text = Py.Strip(Py.S(value));
		if (text.Length == 0)
		{
			text = "自动选择";
		}
		if (text == "auto" || text == "default")
		{
			text = "自动选择";
		}
		SetMeta("java", text);
	}

	public string VersionsDir()
	{
		return PPath.Join(path, "versions");
	}

	public string LibrariesDir()
	{
		return Config.CONFIG.LibrariesDir(path);
	}

	public string AssetsDir()
	{
		return Config.CONFIG.AssetsDir(path);
	}

	public void EnsureStandardDirs()
	{
		string[] sTANDARD_DIRS = Instances._STANDARD_DIRS;
		foreach (string text in sTANDARD_DIRS)
		{
			Utils.EnsureDir(PPath.Join(path, text));
		}
	}

	public List<object> InstalledIds()
	{
		List<object> list = new List<object>();
		foreach (string item in LocalFs.SortedChildren(VersionsDir()))
		{
			if (LocalFs.IsDir(item))
			{
				string text = new PPath(item).Name;
				if (LocalFs.IsFile(PPath.Join(item, text + ".json")))
				{
					list.Add(text);
				}
			}
		}
		return list;
	}

	public object VersionJson(string version_id)
	{
		return Utils.ReadJson(PPath.Join(VersionsDir(), version_id, version_id + ".json"));
	}

	public bool HasVersion(string version_id)
	{
		return LocalFs.IsFile(PPath.Join(VersionsDir(), version_id, version_id + ".json"));
	}
}
