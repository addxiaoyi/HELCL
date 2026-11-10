using System.Collections.Generic;
using System.IO;
using System.Linq;
using HelL.Core.Launcher.Ai;

namespace HelL.Core.Launcher;

public sealed class LauncherConfig
{
	public PyDict data;

	public long revision;

	public readonly object _lock = new object();

	public string instances_dir => PPath.Join(Utils.ROOT, Py.Str(data.Req("instances_dir")));

	public string java_dir => PPath.Join(Utils.ROOT, Py.Str(data.Req("java_dir")));

	public string cache_dir => PPath.Join(Utils.ROOT, "cache");

	public LauncherConfig()
	{
		data = (PyDict)Py.DeepCopy(Config.DEFAULT_CONFIG);
		revision = 0L;
		Load();
	}

	public void Load()
	{
		revision++;
		if (Utils.ReadJson(Config.CONFIG_FILE) is PyDict pyDict)
		{
			bool flag = false;
			foreach (string key in Config.DEFAULT_CONFIG.Keys)
			{
				if (pyDict.ContainsKey(key))
				{
					data[key] = pyDict[key];
				}
				else
				{
					flag = true;
				}
			}
			foreach (KeyValuePair<string, object> item in pyDict)
			{
				if (!Config.DEFAULT_CONFIG.ContainsKey(item.Key))
				{
					data[item.Key] = item.Value;
				}
			}
			if (MigrateLegacyInstancesDir())
			{
				flag = true;
			}
			if (MigrateDefaultIsolation())
			{
				flag = true;
			}
			if (MigrateAiGatewayMode())
			{
				flag = true;
			}
			if (ScrubBuiltinSecrets())
			{
				flag = true;
			}
			if (flag)
			{
				Save();
			}
		}
		else
		{
			MigrateLegacyInstancesDir();
			MigrateDefaultIsolation();
			MigrateAiGatewayMode();
			ScrubBuiltinSecrets();
			Save();
		}
	}

	private static string Norm(object v)
	{
		return Py.RStrip(Py.Strip(Py.S(v)), "/");
	}

	internal bool ScrubBuiltinSecrets()
	{
		bool result = false;
		foreach (string field in new[] { "curseforge_api_key", "ai_api_key" })
		{
			if (LegacyCredential.Matches(field, Py.S(data.Get(field))))
			{
				data[field] = "";
				result = true;
			}
		}
		(string, string[])[] array = new(string, string[])[2]
		{
			("ai_base_url", new string[2]
			{
				Defaults.DEFAULT_API_BASE,
				Defaults.DEFAULT_GATEWAY_URL
			}),
			("ai_gateway_url", new string[2]
			{
				Defaults.DEFAULT_API_BASE,
				Defaults.DEFAULT_GATEWAY_URL
			})
		};
		for (int i = 0; i < array.Length; i++)
		{
			(string, string[]) tuple = array[i];
			string item = tuple.Item1;
			string[] item2 = tuple.Item2;
			string text = Norm(data.Get(item));
			HashSet<string> hashSet = new HashSet<string>(from v in item2
				where !string.IsNullOrEmpty(v)
				select v.TrimEnd('/'));
			if (text.Length > 0 && hashSet.Contains(text))
			{
				data[item] = "";
				result = true;
			}
		}
		return result;
	}

	internal bool MigrateLegacyInstancesDir()
	{
		if (Py.Strip(Py.S(data.Get("instances_dir"))) != "instances")
		{
			return false;
		}
		string text = PPath.Join(Utils.ROOT, "instances");
		string text2 = PPath.Join(Utils.ROOT, ".minecraft");
		if (Directory.Exists(text) && !Directory.Exists(text2) && !File.Exists(text2))
		{
			Directory.Move(text, text2);
		}
		data["instances_dir"] = ".minecraft";
		return true;
	}

	internal bool MigrateDefaultIsolation()
	{
		if (Py.Eq(data.Get("isolation_defaults"), "2026.09-isolate-all"))
		{
			return false;
		}
		if (Py.S(data.Get("default_isolation")) == "none")
		{
			data["default_isolation"] = "all";
		}
		data["isolation_defaults"] = "2026.09-isolate-all";
		return true;
	}

	internal bool MigrateAiGatewayMode()
	{
		if (Py.Eq(data.Get("ai_gateway_defaults"), "2026.10-builtin-newapi"))
		{
			return false;
		}
		if (!new string[3] { "ai_gateway_url", "ai_base_url", "ai_api_key" }.Any((string k) => Py.Strip(Py.S(data.Get(k))).Length > 0) && Py.S(data.Get("ai_mode")) == "public")
		{
			data["ai_mode"] = "custom";
		}
		data["ai_gateway_defaults"] = "2026.10-builtin-newapi";
		return true;
	}

	public void Save()
	{
		lock (_lock)
		{
			Utils.WriteJson(Config.CONFIG_FILE, data);
		}
	}

	public object Get(string key, object dflt = null)
	{
		return data.Get(key, dflt);
	}

	public void Set(string key, object value)
	{
		lock (_lock)
		{
			data[key] = value;
			revision++;
		}
	}

	public void Update(PyDict mapping)
	{
		lock (_lock)
		{
			data.Update(mapping);
			revision++;
		}
	}

	public string LibrariesDir(string instance_dir)
	{
		if (Py.Truthy(data.Req("shared_libraries")))
		{
			return PPath.Join(Utils.ROOT, "shared", "libraries");
		}
		return PPath.Join(instance_dir, "libraries");
	}

	public string AssetsDir(string instance_dir)
	{
		if (Py.Truthy(data.Req("shared_assets")))
		{
			return PPath.Join(Utils.ROOT, "shared", "assets");
		}
		return PPath.Join(instance_dir, "assets");
	}
}
