using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace HelL.Core.Launcher;

public static class Source
{
	public const string BMCLAPI = "https://bmclapi2.bangbang93.com";

	public const string MCIM = "https://mod.mcimirror.top";

	public const string MODRINTH_API = "https://api.modrinth.com";

	public const string MODRINTH_CDN = "https://cdn.modrinth.com";

	public const string CF_OFFICIAL = "https://api.curseforge.com/v1";

	public const double PROBE_LIMIT = 4.0;

	public const double PROBE_TTL = 600.0;

	private static readonly string[][] _FILE_MIRRORS = new string[13][]
	{
		new string[2] { "https://piston-meta.mojang.com/", "https://bmclapi2.bangbang93.com/" },
		new string[2] { "https://launchermeta.mojang.com/", "https://bmclapi2.bangbang93.com/" },
		new string[2] { "https://piston-data.mojang.com/", "https://bmclapi2.bangbang93.com/" },
		new string[2] { "https://resources.download.minecraft.net/", "https://bmclapi2.bangbang93.com/assets/" },
		new string[2] { "https://libraries.minecraft.net/", "https://bmclapi2.bangbang93.com/maven/" },
		new string[2] { "https://maven.minecraftforge.net/", "https://bmclapi2.bangbang93.com/maven/" },
		new string[2] { "https://files.minecraftforge.net/maven/", "https://bmclapi2.bangbang93.com/maven/" },
		new string[2] { "https://maven.neoforged.net/releases/", "https://bmclapi2.bangbang93.com/maven/" },
		new string[2] { "https://maven.fabricmc.net/", "https://bmclapi2.bangbang93.com/maven/" },
		new string[2] { "https://maven.quiltmc.org/repository/release/", "https://bmclapi2.bangbang93.com/maven/" },
		new string[2] { "https://maven.quiltmc.org/", "https://bmclapi2.bangbang93.com/maven/" },
		new string[2] { "https://meta.fabricmc.net/", "https://bmclapi2.bangbang93.com/fabric-meta/" },
		new string[2] { "https://meta.quiltmc.org/", "https://bmclapi2.bangbang93.com/quilt-meta/" }
	};

	public static Func<bool> ProbeHook;

	private static object _cfg(string key, object dflt = null)
	{
		return Config.CONFIG.Get(key, dflt);
	}

	public static string DownloadMode()
	{
		string text = Py.Lower(Py.Strip(Py.Str(Py.Or(_cfg("download_source", "auto"), "auto"))));
		switch (text)
		{
		default:
			return "auto";
		case "auto":
		case "official":
		case "bmclapi":
		case "mc_mirror":
		case "mc_mirror_west":
			return text;
		}
	}

	public static string CommunityMode()
	{
		string text = Py.Lower(Py.Strip(Py.Str(Py.Or(_cfg("community_source", "auto"), "auto"))));
		switch (text)
		{
		default:
			return "auto";
		case "auto":
		case "official":
		case "mcim":
			return text;
		}
	}

	public static void InvalidateProbe()
	{
		MirrorHealth.Invalidate();
	}

	public static bool OfficialIsFast()
	{
		if (ProbeHook != null) return ProbeHook();
		return Py.S(MirrorHealth.Refresh().Get("preferred")) == "official";
	}

	public static bool FileMirrorFirst()
	{
		string text = DownloadMode();
		if (text == "bmclapi" || text == "mc_mirror" || text == "mc_mirror_west")
		{
			return true;
		}
		if (text == "official")
		{
			return false;
		}
		return !OfficialIsFast();
	}

	public static bool CommunityMirrorFirst()
	{
		return CommunityMode() != "official";
	}

	public static void WarmupAsync()
	{
		if (DownloadMode() == "auto") MirrorHealth.Warmup();
	}

	private static List<string> _order(List<string> primary, List<string> secondary, bool secondaryFirst, string exclusive)
	{
		List<string> list;
		if (exclusive == "official")
		{
			list = primary;
		}
		else if (exclusive == "bmclapi" || exclusive == "mcim" || secondaryFirst)
		{
			list = new List<string>(secondary);
			list.AddRange(primary);
		}
		else
		{
			list = new List<string>(primary);
			list.AddRange(secondary);
		}
		List<string> list2 = new List<string>();
		HashSet<string> hashSet = new HashSet<string>();
		foreach (string item in list)
		{
			if (!string.IsNullOrEmpty(item) && hashSet.Add(item))
			{
				list2.Add(item);
			}
		}
		return list2;
	}

	public static List<string> VersionManifestUrls()
	{
		List<string> primary = new List<string> { "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json", "https://launchermeta.mojang.com/mc/game/version_manifest_v2.json" };
		List<string> secondary = new List<string> { "https://bmclapi2.bangbang93.com/mc/game/version_manifest_v2.json" };
		// Static MC Mirror catalogs are subsets; keep the complete upstream catalog.
		string mode = DownloadMode();
		return mode == "official" ? primary : MirrorHealth.Order(primary.Concat(secondary), primary[0], mode == "bmclapi" ? mode : "auto");
	}

	public static string RewriteToBmcl(string url)
	{
		string[][] fILE_MIRRORS = _FILE_MIRRORS;
		foreach (string[] array in fILE_MIRRORS)
		{
			if (url.StartsWith(array[0], StringComparison.Ordinal))
			{
				return array[1] + url.Substring(array[0].Length);
			}
		}
		return null;
	}

	public static string RewriteToMcim(string url)
	{
		if (url.Contains("api.modrinth.com"))
		{
			return url.Replace("https://api.modrinth.com", "https://mod.mcimirror.top/modrinth");
		}
		if (url.Contains("https://cdn.modrinth.com"))
		{
			return url.Replace("https://cdn.modrinth.com", "https://mod.mcimirror.top");
		}
		if (url.StartsWith("https://api.curseforge.com", StringComparison.Ordinal))
		{
			return url.Replace("https://api.curseforge.com", "https://mod.mcimirror.top/curseforge");
		}
		string[] array = new string[2] { "https://edge.forgecdn.net", "https://mediafilez.forgecdn.net" };
		foreach (string text in array)
		{
			if (url.StartsWith(text, StringComparison.Ordinal))
			{
				return "https://mod.mcimirror.top" + url.Substring(text.Length);
			}
		}
		return null;
	}

	public static List<string> ModrinthApiBases()
	{
		return _order(new List<string> { "https://api.modrinth.com/v2" }, new List<string> { "https://mod.mcimirror.top/modrinth/v2" }, CommunityMirrorFirst(), CommunityMode());
	}

	public static List<string> CfApiBases()
	{
		return _order(new List<string> { "https://api.curseforge.com/v1" }, new List<string> { "https://mod.mcimirror.top/curseforge/v1" }, CommunityMirrorFirst(), CommunityMode());
	}

	public static List<string> ModrinthFileUrls(object urls)
	{
		List<object> list = new List<object>();
		if (urls is string item)
		{
			list.Add(item);
		}
		else if (urls is IEnumerable enumerable)
		{
			foreach (object item2 in enumerable)
			{
				list.Add(item2);
			}
		}
		List<string> list2 = new List<string>();
		List<string> list3 = new List<string>();
		foreach (object item3 in list)
		{
			if (Py.Truthy(item3))
			{
				string text = Py.Str(item3);
				list2.Add(text);
				string text2 = RewriteToMcim(text);
				if (!string.IsNullOrEmpty(text2))
				{
					list3.Add(text2);
				}
			}
		}
		return _order(list2, list3, CommunityMirrorFirst(), CommunityMode());
	}

	public static bool IsGithubUrl(string url)
	{
		string text = Py.Lower(url ?? "");
		if (!text.Contains("github.com"))
		{
			return text.Contains("githubusercontent.com");
		}
		return true;
	}

	public static List<string> ExpandDownloadUrls(object url)
	{
		if (!Py.Truthy(url))
		{
			return new List<string>();
		}
		if (!(url is string) && url is IEnumerable enumerable)
		{
			List<string> list = new List<string>();
			HashSet<string> hashSet = new HashSet<string>();
			{
				foreach (object item in enumerable)
				{
					foreach (string item2 in ExpandDownloadUrls(item))
					{
						if (hashSet.Add(item2))
						{
							list.Add(item2);
						}
					}
				}
				return list;
			}
		}
		string text = Py.Str(url);
		if (text.Contains("api.modrinth.com") || text.Contains("https://cdn.modrinth.com") || text.Contains("forgecdn.net") || text.StartsWith("https://mod.mcimirror.top/files/", StringComparison.Ordinal))
		{
			return ModrinthFileUrls(text);
		}
		if (IsGithubUrl(text))
		{
			if (DownloadMode() == "official")
			{
				return new List<string> { text };
			}
			return Mirrors.GithubCandidates(text);
		}
		string text2 = RewriteToBmcl(text);
		var staticMirrors = FileMirrors.Candidates(text);
		if (!string.IsNullOrEmpty(text2) || staticMirrors.Count > 0)
		{
			if (DownloadMode() == "official") return new List<string> { text };
			return MirrorHealth.Order(new[] { text, text2 }.Where(candidate => !string.IsNullOrEmpty(candidate)).Concat(staticMirrors), text, DownloadMode());
		}
		return new List<string> { text };
	}

	public static string Describe()
	{
		string text = DownloadMode();
		string text2 = CommunityMode();
		string text3 = text == "auto" ? "自动·按网络选择" : text == "official" ? "仅官方" : FileMirrors.Label(text) + "优先";
		return "文件:" + text3 + " / 社区:" + text2;
	}
}
