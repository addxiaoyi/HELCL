using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HelL.Services;

public static class FileKinds
{
	public sealed class Verdict
	{
		public string Path { get; init; } = "";

		public string Name { get; init; } = "";

		public List<string> Kinds { get; init; } = new List<string>();

		public bool Sure { get; init; }

		public string Detail { get; init; } = "";

		public string KindSig => string.Join(",", Kinds);
	}

	public static class ModpackProbe
	{
		public sealed class Info
		{
			public string Kind = "";

			public string Format = "";

			public string Name = "";

			public string Version = "";

			public string McVersion = "";

			public string Loader = "";

			public string LoaderVersion = "";

			public int Files;

			public string Path = "";
		}

		private static readonly HashSet<string> PlainMarkers = new HashSet<string> { "mods", "config", "versions", "saves", "resourcepacks", "shaderpacks" };

		private static readonly Dictionary<string, string> LoaderLabels = new Dictionary<string, string>
		{
			["forge"] = "Forge",
			["neoforge"] = "NeoForge",
			["fabric"] = "Fabric",
			["fabric-loader"] = "Fabric",
			["quilt"] = "Quilt",
			["quilt-loader"] = "Quilt",
			["liteloader"] = "LiteLoader"
		};

		public static string LoaderLabel(string? loader, string version = "")
		{
			if (string.IsNullOrEmpty(loader))
			{
				return I18n.L("原版（未声明加载器）");
			}
			string text = LoaderLabels.GetValueOrDefault(loader.ToLowerInvariant()) ?? loader;
			if (!string.IsNullOrEmpty(version))
			{
				return text + " " + version;
			}
			return text;
		}

		public static Info? Probe(string path)
		{
			bool flag = Directory.Exists(path);
			if (!flag && !File.Exists(path))
			{
				return null;
			}
			List<string> list = EntryNames(path);
			if (list == null)
			{
				return null;
			}
			Func<string, byte[]> reader = (flag ? ((Func<string, byte[]>)((string rel) => TryRead(Path.Combine(path, rel.Replace('/', Path.DirectorySeparatorChar))))) : ((Func<string, byte[]>)((string rel) => TryReadZip(path, rel))));
			Info info = ProbeMrpack(list, reader) ?? ProbeCurseForge(list, reader) ?? ProbePlain(list);
			if (info == null)
			{
				return null;
			}
			info.Path = path;
			if (string.IsNullOrEmpty(info.Name))
			{
				info.Name = (flag ? Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)) : Path.GetFileNameWithoutExtension(path));
			}
			if (flag)
			{
				info.Format += I18n.L(" · 已解开的目录");
			}
			return info;
		}

		private static byte[]? TryRead(string full)
		{
			try
			{
				return File.Exists(full) ? File.ReadAllBytes(full) : null;
			}
			catch
			{
				return null;
			}
		}

		private static byte[]? TryReadZip(string archive, string rel)
		{
			try
			{
				using ZipArchive zipArchive = ZipFile.OpenRead(archive);
				ZipArchiveEntry zipArchiveEntry = zipArchive.Entries.FirstOrDefault((ZipArchiveEntry e) => string.Equals(e.FullName.Replace('\\', '/'), rel, StringComparison.OrdinalIgnoreCase));
				if (zipArchiveEntry == null)
				{
					return null;
				}
				using Stream stream = zipArchiveEntry.Open();
				using MemoryStream memoryStream = new MemoryStream();
				stream.CopyTo(memoryStream);
				return memoryStream.ToArray();
			}
			catch
			{
				return null;
			}
		}

		private static string? Member(List<string> names, string marker)
		{
			string text = null;
			foreach (string name in names)
			{
				string[] array = name.Split('/');
				if (!(array[array.Length - 1] != marker) && array.Length <= 4 && (text == null || array.Length < text.Split('/').Length))
				{
					text = name;
				}
			}
			return text;
		}

		private static JsonElement? ReadJson(Func<string, byte[]?> reader, string member)
		{
			byte[] array = reader(member);
			if (array == null || array.Length == 0)
			{
				return null;
			}
			try
			{
				using JsonDocument jsonDocument = JsonDocument.Parse((array.Length >= 3 && array[0] == 239 && array[1] == 187 && array[2] == 191) ? array.Skip(3).ToArray() : array);
				return jsonDocument.RootElement.Clone();
			}
			catch
			{
				return null;
			}
		}

		private static string Str(JsonElement el, string key)
		{
			if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String)
			{
				return "";
			}
			return value.GetString() ?? "";
		}

		private static Info? ProbeMrpack(List<string> names, Func<string, byte[]?> reader)
		{
			string text = Member(names, "modrinth.index.json");
			if (text == null)
			{
				return null;
			}
			JsonElement? jsonElement = ReadJson(reader, text);
			if (jsonElement.HasValue)
			{
				JsonElement valueOrDefault = jsonElement.GetValueOrDefault();
				if (valueOrDefault.ValueKind == JsonValueKind.Object)
				{
					JsonElement value;
					JsonElement el = ((valueOrDefault.TryGetProperty("dependencies", out value) && value.ValueKind == JsonValueKind.Object) ? value : default(JsonElement));
					string text2 = "";
					string[] array = new string[4] { "forge", "neoforge", "fabric-loader", "quilt-loader" };
					foreach (string text3 in array)
					{
						if (el.ValueKind == JsonValueKind.Object && !string.IsNullOrEmpty(Str(el, text3)))
						{
							text2 = text3;
							break;
						}
					}
					JsonElement value2;
					return new Info
					{
						Kind = "mrpack",
						Format = "Modrinth .mrpack",
						Name = Str(valueOrDefault, "name"),
						Version = Str(valueOrDefault, "versionId"),
						McVersion = ((el.ValueKind == JsonValueKind.Object) ? Str(el, "minecraft") : ""),
						Loader = text2,
						LoaderVersion = ((text2.Length > 0) ? Str(el, text2) : ""),
						Files = ((valueOrDefault.TryGetProperty("files", out value2) && value2.ValueKind == JsonValueKind.Array) ? value2.GetArrayLength() : 0)
					};
				}
			}
			return null;
		}

		private static Info? ProbeCurseForge(List<string> names, Func<string, byte[]?> reader)
		{
			string text = Member(names, "manifest.json");
			if (text == null)
			{
				return null;
			}
			JsonElement? jsonElement = ReadJson(reader, text);
			if (jsonElement.HasValue)
			{
				JsonElement valueOrDefault = jsonElement.GetValueOrDefault();
				if (valueOrDefault.ValueKind == JsonValueKind.Object)
				{
					if (!valueOrDefault.TryGetProperty("minecraft", out var value) || value.ValueKind != JsonValueKind.Object)
					{
						return null;
					}
					string loader = "";
					string loaderVersion = "";
					if (value.TryGetProperty("modLoaders", out var value2) && value2.ValueKind == JsonValueKind.Array)
					{
						List<JsonElement> list = value2.EnumerateArray().Where(delegate(JsonElement x)
						{
							JsonElement jsonElement2 = x;
							return jsonElement2.ValueKind == JsonValueKind.Object;
						}).ToList();
						JsonElement value4;
						JsonElement el = list.FirstOrDefault((JsonElement x) => x.TryGetProperty("primary", out value4) && value4.ValueKind == JsonValueKind.True);
						if (el.ValueKind != JsonValueKind.Object && list.Count > 0)
						{
							el = list[0];
						}
						string text2 = ((el.ValueKind == JsonValueKind.Object) ? Str(el, "id") : "");
						int num = text2.IndexOf('-');
						loader = ((num < 0) ? text2 : text2.Substring(0, num));
						loaderVersion = ((num < 0) ? "" : text2.Substring(num + 1));
					}
					JsonElement value3;
					return new Info
					{
						Kind = "curseforge",
						Format = "CurseForge .zip",
						Name = Str(valueOrDefault, "name"),
						Version = Str(valueOrDefault, "version"),
						McVersion = Str(value, "version"),
						Loader = loader,
						LoaderVersion = loaderVersion,
						Files = ((valueOrDefault.TryGetProperty("files", out value3) && value3.ValueKind == JsonValueKind.Array) ? value3.GetArrayLength() : 0)
					};
				}
			}
			return null;
		}

		private static Info? ProbePlain(List<string> names)
		{
			Dictionary<string, HashSet<string>> dictionary = new Dictionary<string, HashSet<string>>();
			foreach (string name in names)
			{
				string[] array = name.Split(new char[1] { '/' }, StringSplitOptions.RemoveEmptyEntries);
				for (int i = 0; i < Math.Min(array.Length, 3); i++)
				{
					string key = string.Join("/", array.Take(i));
					if (!dictionary.TryGetValue(key, out var value))
					{
						HashSet<string> hashSet = (dictionary[key] = new HashSet<string>());
						value = hashSet;
					}
					value.Add(array[i].ToLowerInvariant());
				}
			}
			foreach (string item in dictionary.Keys.OrderBy((string k) => k.Length))
			{
				List<string> list = dictionary[item].Intersect<string>(PlainMarkers).ToList();
				if (list.Count >= 2 || list.Contains("versions"))
				{
					string prefix = ((item.Length == 0) ? "" : (item + "/"));
					var (mcVersion, loader) = PlainVersion(names, prefix);
					return new Info
					{
						Kind = "plain",
						Format = I18n.L("直接压缩的 .minecraft 目录"),
						McVersion = mcVersion,
						Loader = loader
					};
				}
			}
			return null;
		}

		private static (string McVersion, string Loader) PlainVersion(List<string> names, string prefix)
		{
			string text = prefix + "versions/";
			foreach (string name in names)
			{
				if (!name.StartsWith(text, StringComparison.Ordinal))
				{
					continue;
				}
				string[] array = name.Substring(text.Length).Split('/');
				if (array.Length != 2 || array[1] != array[0] + ".json")
				{
					continue;
				}
				string text2 = array[0];
				(string, string)[] array2 = new(string, string)[4]
				{
					("neoforge", "neoforge"),
					("-forge-", "forge"),
					("fabric-loader-", "fabric-loader"),
					("quilt-loader-", "quilt-loader")
				};
				for (int i = 0; i < array2.Length; i++)
				{
					var (value, item) = array2[i];
					if (text2.Contains(value, StringComparison.Ordinal))
					{
						Match match = Regex.Match(text2, "(\\d+\\.\\d+(?:\\.\\d+)?)");
						return (McVersion: match.Success ? match.Groups[1].Value : "", Loader: item);
					}
				}
				return (McVersion: text2, Loader: "");
			}
			return (McVersion: "", Loader: "");
		}
	}

	public const string Modpack = "modpack";

	public const string Mod = "mod";

	public const string ResourcePack = "resourcepack";

	public const string ShaderPack = "shaderpack";

	public const string DataPack = "datapack";

	public const string World = "world";

	public const string Skin = "skin";

	public const string Wallpaper = "wallpaper";

	public static readonly string[] AllKinds = new string[8] { "modpack", "mod", "resourcepack", "shaderpack", "datapack", "world", "skin", "wallpaper" };

	public static readonly string[] VideoSuffixes = new string[7] { ".mp4", ".m4v", ".mov", ".mkv", ".webm", ".avi", ".wmv" };

	public static readonly string[] ImageSuffixes = new string[6] { ".png", ".jpg", ".jpeg", ".bmp", ".webp", ".gif" };

	private static readonly Dictionary<string, string> Labels = new Dictionary<string, string>
	{
		["modpack"] = I18n.L("整合包"),
		["mod"] = I18n.L("模组"),
		["resourcepack"] = I18n.L("资源包"),
		["shaderpack"] = I18n.L("光影包"),
		["datapack"] = I18n.L("数据包"),
		["world"] = I18n.L("存档"),
		["skin"] = I18n.L("离线皮肤"),
		["wallpaper"] = I18n.L("启动器壁纸")
	};

	private static readonly Dictionary<string, string> Actions = new Dictionary<string, string>
	{
		["modpack"] = I18n.L("装成一个新版本"),
		["mod"] = I18n.L("装进当前版本的 mods"),
		["resourcepack"] = I18n.L("装进 resourcepacks"),
		["shaderpack"] = I18n.L("装进 shaderpacks"),
		["datapack"] = I18n.L("装进 datapacks"),
		["world"] = I18n.L("装进存档列表"),
		["skin"] = I18n.L("设为离线账号的皮肤"),
		["wallpaper"] = I18n.L("设为启动器背景")
	};

	private static readonly (int W, int H)[] SkinSizes = new(int, int)[2]
	{
		(64, 64),
		(64, 32)
	};

	private const int ScanDepth = 3;

	private const int ScanLimit = 4000;

	private const int MaxNest = 3;

	public static string Label(string kind)
	{
		return Labels.GetValueOrDefault(kind) ?? kind;
	}

	public static string Action(string kind)
	{
		return Actions.GetValueOrDefault(kind) ?? "";
	}

	public static bool IsVideo(string path)
	{
		return VideoSuffixes.Contains<string>(Path.GetExtension(path ?? "").ToLowerInvariant());
	}

	public static bool IsImage(string path)
	{
		return ImageSuffixes.Contains<string>(Path.GetExtension(path ?? "").ToLowerInvariant());
	}

	public static bool IsWallpaper(string path)
	{
		if (!IsVideo(path))
		{
			return IsImage(path);
		}
		return true;
	}

	public static Verdict Identify(string path)
	{
		string name = Path.GetFileName(path);
		if (string.IsNullOrEmpty(name))
		{
			name = path;
		}
		bool flag = Directory.Exists(path);
		if (!flag && !File.Exists(path))
		{
			return Make(null, sure: false, I18n.L("这个路径不存在"));
		}
		string text = Path.GetExtension(path).ToLowerInvariant();
		if (!flag)
		{
			if (VideoSuffixes.Contains<string>(text))
			{
				return Make(new string[1] { "wallpaper" }, sure: true, I18n.L("视频 · 可以当动态壁纸"));
			}
			if (ImageSuffixes.Contains<string>(text))
			{
				(int, int)? tuple = ((text == ".png") ? PngSize(path) : (((int, int)?)null));
				if (tuple.HasValue)
				{
					(int, int) valueOrDefault = tuple.GetValueOrDefault();
					if (SkinSizes.Contains<(int, int)>(valueOrDefault))
					{
						return Make(new string[2] { "skin", "wallpaper" }, sure: false, I18n.L("{0}x{1} 的 PNG，尺寸正好是皮肤贴图", valueOrDefault.Item1, valueOrDefault.Item2));
					}
				}
				return Make(new string[1] { "wallpaper" }, sure: true, I18n.L("图片"));
			}
			if (text == ".jar")
			{
				return IdentifyJar(path, Make);
			}
		}
		ModpackProbe.Info info = ModpackProbe.Probe(path);
		if (info != null)
		{
			return Make(new string[1] { "modpack" }, sure: true, info.Format + " · Minecraft " + (string.IsNullOrEmpty(info.McVersion) ? I18n.L("未知版本") : info.McVersion));
		}
		List<string> list = EntryNames(path);
		if (list == null)
		{
			return Make(null, sure: false, I18n.L("打不开，也认不出是什么"));
		}
		return IdentifyArchive(list, Make);
		Verdict Make(IEnumerable<string>? kinds = null, bool sure = false, string detail = "")
		{
			return new Verdict
			{
				Path = path,
				Name = name,
				Kinds = (kinds?.ToList() ?? new List<string>()),
				Sure = sure,
				Detail = detail
			};
		}
	}

	private static Verdict IdentifyJar(string path, Func<IEnumerable<string>?, bool, string, Verdict> make)
	{
		HashSet<string> hashSet = (EntryNames(path) ?? new List<string>()).ToHashSet<string>(StringComparer.Ordinal);
		if (hashSet.Contains("install_profile.json"))
		{
			return make(null, arg2: false, I18n.L("这是加载器安装器，不是模组；装加载器请到「原版游戏」页"));
		}
		if (!new string[5] { "fabric.mod.json", "quilt.mod.json", "meta-inf/mods.toml", "meta-inf/neoforge.mods.toml", "mcmod.info" }.Any(hashSet.Contains))
		{
			return make(new string[1] { "mod" }, arg2: false, I18n.L("jar 包，但里面没有模组描述文件"));
		}
		return make(new string[1] { "mod" }, arg2: true, I18n.L("Minecraft 模组"));
	}

	private static Verdict IdentifyArchive(List<string> names, Func<IEnumerable<string>?, bool, string, Verdict> make)
	{
		if (HasEntry(names, "level.dat"))
		{
			return make(new string[1] { "world" }, arg2: true, I18n.L("带 level.dat，是一个存档"));
		}
		if (HasDir(names, "shaders"))
		{
			return make(new string[1] { "shaderpack" }, arg2: true, I18n.L("带 shaders 目录"));
		}
		if (HasEntry(names, "pack.mcmeta"))
		{
			bool flag = HasDir(names, "assets");
			bool flag2 = HasDir(names, "data");
			if (flag && !flag2)
			{
				return make(new string[1] { "resourcepack" }, arg2: true, I18n.L("带 pack.mcmeta 与 assets"));
			}
			if (flag2 && !flag)
			{
				return make(new string[1] { "datapack" }, arg2: true, I18n.L("带 pack.mcmeta 与 data"));
			}
			return make(new string[2] { "resourcepack", "datapack" }, arg2: false, I18n.L("有 pack.mcmeta，但资源包和数据包的目录都在"));
		}
		return make(null, arg2: false, I18n.L("压缩包里没有认得出来的标志文件"));
	}

	private static (int W, int H)? PngSize(string path)
	{
		try
		{
			using FileStream fileStream = File.OpenRead(path);
			byte[] array = new byte[24];
			if (fileStream.Read(array, 0, 24) < 24)
			{
				return null;
			}
			byte[] array2 = new byte[8] { 137, 80, 78, 71, 13, 10, 26, 10 };
			for (int i = 0; i < 8; i++)
			{
				if (array[i] != array2[i])
				{
					return null;
				}
			}
			if (array[12] != 73 || array[13] != 72 || array[14] != 68 || array[15] != 82)
			{
				return null;
			}
			int item = (array[16] << 24) | (array[17] << 16) | (array[18] << 8) | array[19];
			int item2 = (array[20] << 24) | (array[21] << 16) | (array[22] << 8) | array[23];
			return (item, item2);
		}
		catch
		{
			return null;
		}
	}

	internal static List<string>? EntryNames(string path)
	{
		if (Directory.Exists(path))
		{
			return WalkNames(path);
		}
		try
		{
			using ZipArchive zipArchive = ZipFile.OpenRead(path);
			return (from e in zipArchive.Entries.Take(4000)
				select e.FullName.Replace('\\', '/').ToLowerInvariant()).ToList();
		}
		catch
		{
			return null;
		}
	}

	private static List<string> WalkNames(string root)
	{
		List<string> outp = new List<string>();
		Walk(root, 0, "");
		return outp;
		void Walk(string dir, int depth, string prefix)
		{
			if (outp.Count >= 4000)
			{
				return;
			}
			IEnumerable<string> enumerable;
			try
			{
				enumerable = Directory.EnumerateFileSystemEntries(dir);
			}
			catch
			{
				return;
			}
			foreach (string item in enumerable)
			{
				if (outp.Count >= 4000)
				{
					break;
				}
				string fileName = Path.GetFileName(item);
				outp.Add(prefix + fileName.ToLowerInvariant());
				if (depth + 1 < 3 && Directory.Exists(item))
				{
					Walk(item, depth + 1, prefix + fileName.ToLowerInvariant() + "/");
				}
			}
		}
	}

	private static bool HasEntry(List<string> names, string target)
	{
		return names.Any((string n) => n.Split('/').Last() == target && n.Count((char c) => c == '/') <= 1);
	}

	private static bool HasDir(List<string> names, string target)
	{
		return names.Any((string n) => n.Split('/').Take(2).Contains<string>(target));
	}
}
