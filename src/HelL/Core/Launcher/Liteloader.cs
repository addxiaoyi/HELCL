using System;
using System.IO;

namespace HelL.Core.Launcher;

public static class Liteloader
{
	public const string BMCL = "https://bmclapi2.bangbang93.com";

	public const string OFFICIAL = "http://dl.liteloader.com/versions/versions.json";

	public static object ListVersions(DownloadManager dm)
	{
		try
		{
			object obj = dm.FetchJson("https://bmclapi2.bangbang93.com/liteloader/list", 20L);
			if (obj is PyDict pyDict && Py.Truthy(pyDict.Get("versions")))
			{
				return pyDict["versions"];
			}
			if (obj is PyDict)
			{
				return obj;
			}
		}
		catch (Exception ex) when (!(ex is TaskCancelled))
		{
		}
		return Py.Or(LoaderMeta.G(Py.Or(dm.FetchJson("http://dl.liteloader.com/versions/versions.json", 20L), new PyDict()), "versions"), new PyDict());
	}

	public static string Install(Installer installer, string mc_version, bool force = false)
	{
		mc_version = Py.Strip(mc_version ?? "");
		object o = ((ListVersions(installer.dm) is PyDict pyDict) ? pyDict.Get(mc_version) : null);
		if (!Py.Truthy(o))
		{
			throw new InstallError("LiteLoader 不支持 Minecraft " + mc_version);
		}
		PyDict pyDict2 = Py.DictOr(o);
		object obj = Py.Or(pyDict2.Get("repo"), pyDict2.Get("artefacts"), new PyDict());
		object obj2 = null;
		if (obj is PyDict pyDict3 && Py.Or(pyDict3.Get("com.mumfrey:liteloader"), pyDict3) is PyDict pyDict4)
		{
			obj2 = Py.Or(pyDict4.Get("latest"), pyDict4.Get("snapshot"));
			if (!Py.Truthy(obj2))
			{
				string[] array = new string[2] { "1.12.2-SNAPSHOT", "1.8.9-SNAPSHOT" };
				foreach (string key in array)
				{
					if (pyDict4.ContainsKey(key))
					{
						obj2 = pyDict4[key];
						break;
					}
				}
			}
		}
		string text = "";
		string text2;
		string text3;
		if (obj2 is PyDict pyDict5)
		{
			text2 = Py.Str(Py.Or(pyDict5.Get("version"), mc_version + "-SNAPSHOT"));
			text = Py.Str(Py.Or(pyDict5.Get("url"), pyDict5.Get("file"), ""));
			text3 = Py.Str(Py.Or(pyDict5.Get("tweakClass"), "com.mumfrey.liteloader.launch.LiteLoaderTweaker"));
		}
		else
		{
			text2 = mc_version + "-SNAPSHOT";
			text3 = "com.mumfrey.liteloader.launch.LiteLoaderTweaker";
		}
		if (text.Length == 0)
		{
			text = "https://bmclapi2.bangbang93.com/maven/com/mumfrey/liteloader/" + text2 + "/liteloader-" + text2 + ".jar";
		}
		string text4 = mc_version + "-LiteLoader" + mc_version;
		if (installer.instance.HasVersion(text4) && !force)
		{
			return text4;
		}
		installer._note("安装原版 " + mc_version + "（LiteLoader 依赖）", 0L, 1L);
		installer.InstallVersion(mc_version);
		string text5 = PPath.Join(installer.instance.LibrariesDir(), "com", "mumfrey", "liteloader", text2, "liteloader-" + text2 + ".jar");
		installer._note("下载 LiteLoader " + text2, 0L, 1L);
		DownloadManager dm = installer.dm;
		string url = text;
		bool force2 = force;
		dm.Download(url, text5, null, null, force2);
		if (!new PPath(text5).IsFile())
		{
			throw new InstallError("LiteLoader 下载失败");
		}
		_ensure_launchwrapper(installer);
		PyDict vjson = PyDict.Of("id", text4, "inheritsFrom", mc_version, "jar", mc_version, "type", "release", "mainClass", "net.minecraft.launchwrapper.Launch", "minecraftArguments", "--username ${auth_player_name} --version ${version_name} --gameDir ${game_directory} --assetsDir ${assets_root} --assetIndex ${assets_index_name} --uuid ${auth_uuid} --accessToken ${auth_access_token} --userType ${user_type} --tweakClass " + text3, "libraries", Py.L(PyDict.Of("name", "com.mumfrey:liteloader:" + text2), PyDict.Of("name", "net.minecraft:launchwrapper:1.12")));
		installer._install_json(text4, vjson, force);
		return text4;
	}

	private static void _ensure_launchwrapper(Installer installer)
	{
		string text = PPath.Join(installer.instance.LibrariesDir(), "net", "minecraft", "launchwrapper", "1.12", "launchwrapper-1.12.jar");
		if (!new PPath(text).IsFile() || new FileInfo(Utils.AbsPath(text)).Length <= 1000)
		{
			string url = "https://bmclapi2.bangbang93.com/maven/net/minecraft/launchwrapper/1.12/launchwrapper-1.12.jar";
			installer._note("下载 launchwrapper 1.12", 0L, 1L);
			installer.dm.Download(url, text);
		}
	}
}
