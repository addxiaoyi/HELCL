using System;
using HelL.Services;

namespace HelL.Models;

public sealed class CatalogKind
{
	public static readonly CatalogKind Mod = new CatalogKind
	{
		Key = "mod",
		Title = I18n.L("模组"),
		Icon = "mod",
		SearchMethod = "search_mods",
		InstallMethod = "install_mod",
		InstalledMethod = "get_installed_mods",
		DeleteMethod = "delete_mod",
		Empty = I18n.L("没有找到相关模组"),
		LinkHint = I18n.L("模组链接 / .jar 直链"),
		LocalFilter = I18n.L("模组 (*.jar)|*.jar|全部文件|*.*"),
		Types = new string[7] { "全部", "优化", "科技", "魔法", "冒险", "建筑", "工具" },
		FileKind = "mod",
		DefaultSource = "Modrinth"
	};

	public static readonly CatalogKind Modpack = new CatalogKind
	{
		Key = "modpack",
		Title = I18n.L("整合包"),
		Icon = "pack",
		SearchMethod = "search_modpacks",
		InstallMethod = "install_modpack",
		InstalledMethod = "get_installed_modpacks",
		DeleteMethod = "delete_modpack",
		Empty = I18n.L("没有找到相关整合包"),
		LinkHint = I18n.L("整合包链接 / .mrpack / .zip"),
		LocalFilter = I18n.L("整合包 (*.mrpack;*.zip)|*.mrpack;*.zip|全部文件|*.*"),
		IsModpack = true,
		Types = new string[6] { "全部", "生存", "空岛", "科技", "魔法", "冒险" },
		FileKind = "modpack",
		DefaultSource = "Modrinth"
	};

	public static readonly CatalogKind Datapack = new CatalogKind
	{
		Key = "datapack",
		Title = I18n.L("数据包"),
		Icon = "data",
		SearchMethod = "search_datapacks",
		InstallMethod = "install_datapack",
		InstalledMethod = "get_installed_datapacks",
		DeleteMethod = "delete_datapack",
		Empty = I18n.L("没有找到相关数据包"),
		LinkHint = I18n.L("数据包下载链接"),
		LocalFilter = I18n.L("数据包 (*.zip)|*.zip|全部文件|*.*"),
		Types = new string[5] { "全部", "生存", "冒险", "装饰", "工具" },
		FileKind = "datapack"
	};

	public static readonly CatalogKind ResourcePack = new CatalogKind
	{
		Key = "resourcepack",
		Title = I18n.L("资源包"),
		Icon = "res",
		SearchMethod = "search_resourcepacks",
		InstallMethod = "install_resourcepack",
		InstalledMethod = "get_installed_resourcepacks",
		DeleteMethod = "delete_resourcepack",
		Empty = I18n.L("没有找到相关资源包"),
		LinkHint = I18n.L("资源包下载链接"),
		LocalFilter = I18n.L("资源包 (*.zip)|*.zip|全部文件|*.*"),
		Types = new string[7] { "全部", "16x", "32x", "64x", "写实", "现代风", "动态效果" },
		FileKind = "resourcepack"
	};

	public static readonly CatalogKind Shader = new CatalogKind
	{
		Key = "shader",
		Title = I18n.L("光影包"),
		Icon = "shader",
		SearchMethod = "search_shaders",
		InstallMethod = "install_shader",
		InstalledMethod = "get_installed_shaders",
		DeleteMethod = "delete_shader",
		Empty = I18n.L("没有找到相关光影"),
		LinkHint = I18n.L("光影包下载链接"),
		LocalFilter = I18n.L("光影包 (*.zip)|*.zip|全部文件|*.*"),
		Types = new string[5] { "全部", "写实", "卡通", "高性能", "光追" },
		FileKind = "shader"
	};

	public static readonly CatalogKind World = new CatalogKind
	{
		Key = "world",
		Title = I18n.L("世界"),
		Icon = "world",
		SearchMethod = "search_worlds",
		InstallMethod = "install_world",
		Empty = I18n.L("没有找到相关世界地图"),
		LinkHint = I18n.L("世界地图下载链接"),
		LocalFilter = I18n.L("世界 (*.zip)|*.zip|全部文件|*.*"),
		Types = new string[5] { "全部", "生存", "冒险", "创造", "跑酷" },
		FileKind = "world",
		DefaultSource = "CurseForge"
	};

	public static readonly CatalogKind[] All = new CatalogKind[6] { Mod, Modpack, Datapack, ResourcePack, Shader, World };

	public string Key { get; set; } = "";

	public string Title { get; set; } = "";

	public string SearchMethod { get; set; } = "";

	public string InstallMethod { get; set; } = "";

	public string InstalledMethod { get; set; } = "";

	public string DeleteMethod { get; set; } = "";

	public string Empty { get; set; } = "";

	public string LinkHint { get; set; } = "";

	public string LocalFilter { get; set; } = "";

	public bool IsModpack { get; set; }

	public string[] Types { get; set; } = Array.Empty<string>();

	public string FileKind { get; set; } = "mod";

	public string DefaultSource { get; set; } = I18n.L("全部");

	public string Icon { get; set; } = "";
}
