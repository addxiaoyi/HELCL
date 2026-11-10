using System.Collections.Generic;
using System;
using System.Linq;

namespace HelL.Core.Launcher;

public static class Catalog
{
	public sealed class AliasInfo
	{
		public readonly string Key;

		public readonly string Slug;

		public readonly string Title;

		public readonly object Cf;

		public readonly bool HasTitle;

		public AliasInfo(string key, string slug, object cf, string title, bool hasTitle)
		{
			Key = key;
			Slug = slug;
			Cf = cf;
			Title = title;
			HasTitle = hasTitle;
		}
	}

	private static Dictionary<string, AliasInfo> _modIdx;

	private static Dictionary<string, AliasInfo> _packIdx;

	public const long CBC_CF_ID = 1238396L;

	public const string CBC_CF_SLUG = "create-the-brass-concerto";

	public const long CDC_CF_ID = 1059094L;

	public const string CDC_CF_SLUG = "create-delight-remake";

	private static readonly AliasInfo[] _MOD_ALIASES_LOWER = new AliasInfo[311]
	{
		new AliasInfo("jei", "jei", null, "Just Enough Items (JEI)", hasTitle: true),
		new AliasInfo("jei物品管理器", "jei", null, "Just Enough Items (JEI)", hasTitle: true),
		new AliasInfo("物品管理器", "jei", null, "Just Enough Items (JEI)", hasTitle: true),
		new AliasInfo("just enough items", "jei", null, "Just Enough Items (JEI)", hasTitle: true),
		new AliasInfo("rei", "rei", null, "Roughly Enough Items (REI)", hasTitle: true),
		new AliasInfo("roughly enough items", "rei", null, "Roughly Enough Items (REI)", hasTitle: true),
		new AliasInfo("emi", "emi", null, "EMI", hasTitle: true),
		new AliasInfo("emi物品管理器", "emi", null, "EMI", hasTitle: true),
		new AliasInfo("小地图", "xaeros-minimap", null, "Xaero's Minimap", hasTitle: true),
		new AliasInfo("xaeros小地图", "xaeros-minimap", null, "Xaero's Minimap", hasTitle: true),
		new AliasInfo("xaero's minimap", "xaeros-minimap", null, "Xaero's Minimap", hasTitle: true),
		new AliasInfo("世界地图", "xaeros-world-map", null, "Xaero's World Map", hasTitle: true),
		new AliasInfo("xaeros世界地图", "xaeros-world-map", null, "Xaero's World Map", hasTitle: true),
		new AliasInfo("高清修复", "iris", null, "Iris Shaders", hasTitle: true),
		new AliasInfo("optifine", "iris", null, "Iris Shaders", hasTitle: true),
		new AliasInfo("光影", "iris", null, "Iris Shaders", hasTitle: true),
		new AliasInfo("fabric api", "fabric-api", null, "Fabric API", hasTitle: true),
		new AliasInfo("fabric接口", "fabric-api", null, "Fabric API", hasTitle: true),
		new AliasInfo("群峦传说", "terrafirmacraft", 302973L, "TerraFirmaCraft", hasTitle: true),
		new AliasInfo("terrafirmacraft", "terrafirmacraft", 302973L, "TerraFirmaCraft", hasTitle: true),
		new AliasInfo("群峦", "terrafirmacraft", 302973L, "TerraFirmaCraft", hasTitle: true),
		new AliasInfo("tfc", "terrafirmacraft", 302973L, "TerraFirmaCraft", hasTitle: true),
		new AliasInfo("hbm核科技", null, 235439L, "Hbm's Nuclear Tech Mod", hasTitle: true),
		new AliasInfo("hbm nuclear tech", null, 235439L, "Hbm's Nuclear Tech Mod", hasTitle: true),
		new AliasInfo("旅行地图", "journeymap", 32274L, "JourneyMap", hasTitle: true),
		new AliasInfo("journeymap", "journeymap", 32274L, "JourneyMap", hasTitle: true),
		new AliasInfo("journey map", "journeymap", 32274L, "JourneyMap", hasTitle: true),
		new AliasInfo("waila", "waila", null, "Hwyla / Waila", hasTitle: true),
		new AliasInfo("高亮显示", "waila", null, "Hwyla / Waila", hasTitle: true),
		new AliasInfo("看到方块信息", "waila", null, "Hwyla / Waila", hasTitle: true),
		new AliasInfo("jade", "jade", null, "Jade", hasTitle: true),
		new AliasInfo("玉", "jade", null, "Jade", hasTitle: true),
		new AliasInfo("暮色森林", "twilight-forest", 227639L, "Twilight Forest", hasTitle: true),
		new AliasInfo("twilight forest", "twilight-forest", 227639L, "Twilight Forest", hasTitle: true),
		new AliasInfo("twilight", "twilight-forest", 227639L, "Twilight Forest", hasTitle: true),
		new AliasInfo("应用能源2", "ae2", 223794L, "Applied Energistics 2", hasTitle: true),
		new AliasInfo("ae2", "ae2", 223794L, "Applied Energistics 2", hasTitle: true),
		new AliasInfo("applied energistics 2", "ae2", 223794L, "Applied Energistics 2", hasTitle: true),
		new AliasInfo("create", "create", 328085L, "Create", hasTitle: true),
		new AliasInfo("机械动力", "create", 328085L, "Create", hasTitle: true),
		new AliasInfo("机械动力模组", "create", 328085L, "Create", hasTitle: true),
		new AliasInfo("神秘时代", "thaumcraft", 223628L, "Thaumcraft", hasTitle: true),
		new AliasInfo("thaumcraft", "thaumcraft", 223628L, "Thaumcraft", hasTitle: true),
		new AliasInfo("植物魔法", "botania", 225643L, "Botania", hasTitle: true),
		new AliasInfo("botania", "botania", 225643L, "Botania", hasTitle: true),
		new AliasInfo("匠魂", "tinkers-construct", 74072L, "Tinkers' Construct", hasTitle: true),
		new AliasInfo("tinkers construct", "tinkers-construct", 74072L, "Tinkers' Construct", hasTitle: true),
		new AliasInfo("匠魂3", "tinkers-construct-3", null, "Tinkers' Construct 3", hasTitle: true),
		new AliasInfo("工业时代2", "industrial-craft", 242638L, "IndustrialCraft 2", hasTitle: true),
		new AliasInfo("ic2", "industrial-craft", 242638L, "IndustrialCraft 2", hasTitle: true),
		new AliasInfo("industrial craft", "industrial-craft", 242638L, "IndustrialCraft 2", hasTitle: true),
		new AliasInfo("热力膨胀", "thermal-expansion", 69163L, "Thermal Expansion", hasTitle: true),
		new AliasInfo("thermal expansion", "thermal-expansion", 69163L, "Thermal Expansion", hasTitle: true),
		new AliasInfo("thermal系列", "thermal-expansion", 69163L, "Thermal Expansion", hasTitle: true),
		new AliasInfo("铁路", "railcraft", 227443L, "Railcraft", hasTitle: true),
		new AliasInfo("railcraft", "railcraft", 227443L, "Railcraft", hasTitle: true),
		new AliasInfo("林业", "forestry", 59751L, "Forestry", hasTitle: true),
		new AliasInfo("forestry", "forestry", 59751L, "Forestry", hasTitle: true),
		new AliasInfo("星系", null, 564236L, "Galacticraft Legacy", hasTitle: true),
		new AliasInfo("galacticraft", null, 564236L, "Galacticraft Legacy", hasTitle: true),
		new AliasInfo("储物抽屉", "storage-drawers", 223852L, "Storage Drawers", hasTitle: true),
		new AliasInfo("storage drawers", "storage-drawers", 223852L, "Storage Drawers", hasTitle: true),
		new AliasInfo("循环", "cyclic", 239286L, "Cyclic", hasTitle: true),
		new AliasInfo("cyclic", "cyclic", 239286L, "Cyclic", hasTitle: true),
		new AliasInfo("更多箱子", "iron-chests", 228756L, "Iron Chests", hasTitle: true),
		new AliasInfo("iron chests", "iron-chests", 228756L, "Iron Chests", hasTitle: true),
		new AliasInfo("铁箱子", "iron-chests", 228756L, "Iron Chests", hasTitle: true),
		new AliasInfo("背包", "backpacked", null, "Backpacked", hasTitle: true),
		new AliasInfo("旅行背包", "travelers-backpack", null, "Traveler's Backpack", hasTitle: true),
		new AliasInfo("traveler's backpack", "travelers-backpack", null, "Traveler's Backpack", hasTitle: true),
		new AliasInfo("苹果皮", "appleskin", null, "AppleSkin", hasTitle: true),
		new AliasInfo("appleskin", "appleskin", null, "AppleSkin", hasTitle: true),
		new AliasInfo("饥饿值显示", "appleskin", null, "AppleSkin", hasTitle: true),
		new AliasInfo("伤害显示", null, null, "ToroHealth Damage Indicators", hasTitle: true),
		new AliasInfo("torohealth", null, null, "ToroHealth Damage Indicators", hasTitle: true),
		new AliasInfo("方块信息", "wthit", null, "What Is That? (WTHIT)", hasTitle: true),
		new AliasInfo("wthit", "wthit", null, "What Is That? (WTHIT)", hasTitle: true),
		new AliasInfo("hud显示", "wthit", null, "What Is That? (WTHIT)", hasTitle: true),
		new AliasInfo("连锁采集", "veinminer", 67133L, "VeinMiner", hasTitle: true),
		new AliasInfo("veinminer", "veinminer", 67133L, "VeinMiner", hasTitle: true),
		new AliasInfo("vein miner", "veinminer", 67133L, "VeinMiner", hasTitle: true),
		new AliasInfo("一键挖矿", "veinminer", 67133L, "VeinMiner", hasTitle: true),
		new AliasInfo("投影", "litematica", null, "Litematica", hasTitle: true),
		new AliasInfo("litematica", "litematica", null, "Litematica", hasTitle: true),
		new AliasInfo("迷你地图", "voxelmap", 225179L, "VoxelMap", hasTitle: true),
		new AliasInfo("voxelmap", "voxelmap", 225179L, "VoxelMap", hasTitle: true),
		new AliasInfo("物品滚轮", "inventory-profiles-next", null, "Inventory Profiles Next", hasTitle: true),
		new AliasInfo("inventory profiles", "inventory-profiles-next", null, "Inventory Profiles Next", hasTitle: true),
		new AliasInfo("更好的f3", "betterf3", null, "BetterF3", hasTitle: true),
		new AliasInfo("betterf3", "betterf3", null, "BetterF3", hasTitle: true),
		new AliasInfo("better f3", "betterf3", null, "BetterF3", hasTitle: true),
		new AliasInfo("钠", "sodium", null, "Sodium", hasTitle: true),
		new AliasInfo("sodium", "sodium", null, "Sodium", hasTitle: true),
		new AliasInfo("铷", "sodium", null, "Sodium (Rubidium)", hasTitle: true),
		new AliasInfo("锂", "lithium", null, "Lithium", hasTitle: true),
		new AliasInfo("lithium", "lithium", null, "Lithium", hasTitle: true),
		new AliasInfo("磷", "phosphor", null, "Phosphor", hasTitle: true),
		new AliasInfo("phosphor", "phosphor", null, "Phosphor", hasTitle: true),
		new AliasInfo("星光", "starlight", null, "Starlight", hasTitle: true),
		new AliasInfo("starlight", "starlight", null, "Starlight", hasTitle: true),
		new AliasInfo("iris", "iris", null, "Iris Shaders", hasTitle: true),
		new AliasInfo("iris光影", "iris", null, "Iris Shaders", hasTitle: true),
		new AliasInfo("光影加载器", "iris", null, "Iris Shaders", hasTitle: true),
		new AliasInfo("oculus", "oculus", null, "Oculus", hasTitle: true),
		new AliasInfo("oculus光影", "oculus", null, "Oculus", hasTitle: true),
		new AliasInfo("优化", "sodium-extra", null, "Sodium Extra", hasTitle: true),
		new AliasInfo("sodium extra", "sodium-extra", null, "Sodium Extra", hasTitle: true),
		new AliasInfo("更好的结束", "better-end", null, "Better End", hasTitle: true),
		new AliasInfo("better end", "better-end", null, "Better End", hasTitle: true),
		new AliasInfo("更好的下界", "better-nether", null, "Better Nether", hasTitle: true),
		new AliasInfo("better nether", "better-nether", null, "Better Nether", hasTitle: true),
		new AliasInfo("夸克", "quark", 243121L, "Quark", hasTitle: true),
		new AliasInfo("quark", "quark", 243121L, "Quark", hasTitle: true),
		new AliasInfo("补充配件", "supplementaries", null, "Supplementaries", hasTitle: true),
		new AliasInfo("supplementaries", "supplementaries", null, "Supplementaries", hasTitle: true),
		new AliasInfo("农夫乐事", "farmers-delight", null, "Farmer's Delight", hasTitle: true),
		new AliasInfo("farmer's delight", "farmers-delight", null, "Farmer's Delight", hasTitle: true),
		new AliasInfo("farmers delight", "farmers-delight", null, "Farmer's Delight", hasTitle: true),
		new AliasInfo("中式工坊", "chineseworkshop", null, "ChineseWorkshop", hasTitle: true),
		new AliasInfo("chineseworkshop", "chineseworkshop", null, "ChineseWorkshop", hasTitle: true),
		new AliasInfo("中式建筑", "chineseworkshop", null, "ChineseWorkshop", hasTitle: true),
		new AliasInfo("创世神", "worldedit", 225608L, "WorldEdit", hasTitle: true),
		new AliasInfo("worldedit", "worldedit", 225608L, "WorldEdit", hasTitle: true),
		new AliasInfo("we", "worldedit", 225608L, "WorldEdit", hasTitle: true),
		new AliasInfo("创世神cui", "worldeditcui", null, "WorldEditCUI", hasTitle: true),
		new AliasInfo("worldeditcui", "worldeditcui", null, "WorldEditCUI", hasTitle: true),
		new AliasInfo("nei", "nei", null, "Not Enough Items", hasTitle: true),
		new AliasInfo("not enough items", "nei", null, "Not Enough Items", hasTitle: true),
		new AliasInfo("伤害指示器", null, null, "ToroHealth Damage Indicators", hasTitle: true),
		new AliasInfo("地牢", "dungeons-mod", null, "Dungeons Mod", hasTitle: true),
		new AliasInfo("更多结构", "yungs-better-dungeons", null, "YUNG's Better Dungeons", hasTitle: true),
		new AliasInfo("yungs更好的地牢", "yungs-better-dungeons", null, "YUNG's Better Dungeons", hasTitle: true),
		new AliasInfo("更好的村庄", "yungs-better-villages", null, "YUNG's Better Villages", hasTitle: true),
		new AliasInfo("yungs更好的村庄", "yungs-better-villages", null, "YUNG's Better Villages", hasTitle: true),
		new AliasInfo("更好的矿井", "yungs-better-mineshafts", null, "YUNG's Better Mineshafts", hasTitle: true),
		new AliasInfo("yungs更好的矿井", "yungs-better-mineshafts", null, "YUNG's Better Mineshafts", hasTitle: true),
		new AliasInfo("更好的海洋", "yungs-better-ocean-monuments", null, "YUNG's Better Ocean Monuments", hasTitle: true),
		new AliasInfo("yungs更好的海洋神殿", "yungs-better-ocean-monuments", null, "YUNG's Better Ocean Monuments", hasTitle: true),
		new AliasInfo("更好的要塞", "yungs-better-strongholds", null, "YUNG's Better Strongholds", hasTitle: true),
		new AliasInfo("yungs更好的要塞", "yungs-better-strongholds", null, "YUNG's Better Strongholds", hasTitle: true),
		new AliasInfo("更好的桥梁", "yungs-bridges", null, "YUNG's Bridges", hasTitle: true),
		new AliasInfo("沉浸工程", "immersive-engineering", 231951L, "Immersive Engineering", hasTitle: true),
		new AliasInfo("immersive engineering", "immersive-engineering", 231951L, "Immersive Engineering", hasTitle: true),
		new AliasInfo("沉浸传送门", "immersive-portals-mod", null, "Immersive Portals", hasTitle: true),
		new AliasInfo("immersive portals", "immersive-portals-mod", null, "Immersive Portals", hasTitle: true),
		new AliasInfo("动态光源", "lambdynamiclights", null, "LambDynamicLights", hasTitle: true),
		new AliasInfo("dynamic lights", "lambdynamiclights", null, "LambDynamicLights", hasTitle: true),
		new AliasInfo("动态光源forge", "dynamic-lights", 227874L, "Dynamic Lights", hasTitle: true),
		new AliasInfo("真实掉落", "realistic-item-drops", null, "Realistic Item Drops", hasTitle: true),
		new AliasInfo("声音滤波器", "presence-footsteps", null, "Presence Footsteps", hasTitle: true),
		new AliasInfo("脚步声", "presence-footsteps", null, "Presence Footsteps", hasTitle: true),
		new AliasInfo("万物皆可熔炼", "all-in-one-oven", null, "All In One Oven", hasTitle: true),
		new AliasInfo("更多熔炉", "iron-furnaces", null, "Iron Furnaces", hasTitle: true),
		new AliasInfo("铁熔炉", "iron-furnaces", null, "Iron Furnaces", hasTitle: true),
		new AliasInfo("更多傀儡", "extra-golems", null, "Extra Golems", hasTitle: true),
		new AliasInfo("更多生物", "mowzies-mobs", null, "Mowzie's Mobs", hasTitle: true),
		new AliasInfo("mowzies mobs", "mowzies-mobs", null, "Mowzie's Mobs", hasTitle: true),
		new AliasInfo("更多附魔", "apotheosis", 313970L, "Apotheosis", hasTitle: true),
		new AliasInfo("apotheosis", "apotheosis", 313970L, "Apotheosis", hasTitle: true),
		new AliasInfo("传说武器", "mcdw", null, "MCDungeons Weapons", hasTitle: true),
		new AliasInfo("更多武器", "mcdw", null, "MCDungeons Weapons", hasTitle: true),
		new AliasInfo("考古", "archaeology", null, "Archaeology", hasTitle: true),
		new AliasInfo("传送书", "waystones", null, "Waystones", hasTitle: true),
		new AliasInfo("waystones", "waystones", null, "Waystones", hasTitle: true),
		new AliasInfo("传送石碑", "waystones", null, "Waystones", hasTitle: true),
		new AliasInfo("传送门枪", "portal-gun", 229084L, "Portal Gun", hasTitle: true),
		new AliasInfo("搬运", "carry-on", null, "Carry On", hasTitle: true),
		new AliasInfo("carry on", "carry-on", null, "Carry On", hasTitle: true),
		new AliasInfo("搬箱器", "carry-on", null, "Carry On", hasTitle: true),
		new AliasInfo("懒人厨房", "cooking-for-blockheads", null, "Cooking for Blockheads", hasTitle: true),
		new AliasInfo("cooking for blockheads", "cooking-for-blockheads", null, "Cooking for Blockheads", hasTitle: true),
		new AliasInfo("潘马斯农场", "pam-harvestcraft-2", 221857L, "Pam's HarvestCraft 2", hasTitle: true),
		new AliasInfo("pam's harvestcraft", "pam-harvestcraft-2", 221857L, "Pam's HarvestCraft 2", hasTitle: true),
		new AliasInfo("harvestcraft", "pam-harvestcraft-2", 221857L, "Pam's HarvestCraft 2", hasTitle: true),
		new AliasInfo("糖果世界", "candy-world", null, "Candy World", hasTitle: true),
		new AliasInfo("梦幻世界", "twilight-forest", 227639L, "Twilight Forest", hasTitle: true),
		new AliasInfo("atc警报", "atc-alarm", null, "ATC Alarm", hasTitle: true),
		new AliasInfo("经验书", "xp-book", null, "XP Book", hasTitle: true),
		new AliasInfo("xp book", "xp-book", null, "XP Book", hasTitle: true),
		new AliasInfo("通用机械", "mekanism", 268560L, "Mekanism", hasTitle: true),
		new AliasInfo("mekanism", "mekanism", 268560L, "Mekanism", hasTitle: true),
		new AliasInfo("通用机械发电机", "mekanism-generators", 268566L, "Mekanism Generators", hasTitle: true),
		new AliasInfo("通用机械附加", "mekanism-tools", 268567L, "Mekanism Tools", hasTitle: true),
		new AliasInfo("深度学习", "deep-learning", null, "Deep Learning", hasTitle: true),
		new AliasInfo("更好的进度", "better-advancements", null, "Better Advancements", hasTitle: true),
		new AliasInfo("better advancements", "better-advancements", null, "Better Advancements", hasTitle: true),
		new AliasInfo("更好的食谱", "roughly-enough-professions", null, "Roughly Enough Professions", hasTitle: true),
		new AliasInfo("roughly enough professions", "roughly-enough-professions", null, "Roughly Enough Professions", hasTitle: true),
		new AliasInfo("更好的交易", "better-trades", null, "Better Trades", hasTitle: true),
		new AliasInfo("better trades", "better-trades", null, "Better Trades", hasTitle: true),
		new AliasInfo("村民交易", "easy-villagers", null, "Easy Villagers", hasTitle: true),
		new AliasInfo("easy villagers", "easy-villagers", null, "Easy Villagers", hasTitle: true),
		new AliasInfo("刷怪塔", "mob-grinding-utils", null, "Mob Grinding Utils", hasTitle: true),
		new AliasInfo("经验增益", "xp-berry", null, "XP Berry", hasTitle: true),
		new AliasInfo("经验浆果", "xp-berry", null, "XP Berry", hasTitle: true),
		new AliasInfo("加速火把", "torchmaster", null, "Torchmaster", hasTitle: true),
		new AliasInfo("torchmaster", "torchmaster", null, "Torchmaster", hasTitle: true),
		new AliasInfo("刷怪笼", "apotheosis", 313970L, "Apotheosis", hasTitle: true),
		new AliasInfo("末影接口", "ender-io", 64578L, "Ender IO", hasTitle: true),
		new AliasInfo("ender io", "ender-io", 64578L, "Ender IO", hasTitle: true),
		new AliasInfo("末影存储", "ender-storage-1-8", 245174L, "Ender Storage", hasTitle: true),
		new AliasInfo("ender storage", "ender-storage-1-8", 245174L, "Ender Storage", hasTitle: true),
		new AliasInfo("末影箱子", "ender-chests", null, "Ender Chests", hasTitle: true),
		new AliasInfo("传输管道", "xnet", null, "XNet", hasTitle: true),
		new AliasInfo("xnet", "xnet", null, "XNet", hasTitle: true),
		new AliasInfo("管道", "pipez", null, "Pipez", hasTitle: true),
		new AliasInfo("pipez", "pipez", null, "Pipez", hasTitle: true),
		new AliasInfo("漏斗", "hopper", null, "Hopper", hasTitle: true),
		new AliasInfo("更多漏斗", "hopper", null, "Hopper", hasTitle: true),
		new AliasInfo("构建工艺", "buildcraft", 61811L, "BuildCraft", hasTitle: true),
		new AliasInfo("buildcraft", "buildcraft", 61811L, "BuildCraft", hasTitle: true),
		new AliasInfo("电脑", "computercraft", 225604L, "ComputerCraft", hasTitle: true),
		new AliasInfo("computercraft", "computercraft", 225604L, "ComputerCraft", hasTitle: true),
		new AliasInfo("cc", "computercraft", 225604L, "ComputerCraft", hasTitle: true),
		new AliasInfo("开放式电脑", "opencomputers", 225658L, "OpenComputers", hasTitle: true),
		new AliasInfo("opencomputers", "opencomputers", 225658L, "OpenComputers", hasTitle: true),
		new AliasInfo("更好的fps", null, 229876L, "BetterFps", hasTitle: true),
		new AliasInfo("显示fps", null, 229876L, "BetterFps", hasTitle: true),
		new AliasInfo("按键显示", "keystrokes", null, "Keystrokes", hasTitle: true),
		new AliasInfo("keystrokes", "keystrokes", null, "Keystrokes", hasTitle: true),
		new AliasInfo("cps显示", "cps", null, "CPS", hasTitle: true),
		new AliasInfo("皮肤显示", "3dskinlayers", null, "3D Skin Layers", hasTitle: true),
		new AliasInfo("3d皮肤层", "3dskinlayers", null, "3D Skin Layers", hasTitle: true),
		new AliasInfo("3d skin layers", "3dskinlayers", null, "3D Skin Layers", hasTitle: true),
		new AliasInfo("自定义皮肤", "customskinloader", null, "CustomSkinLoader", hasTitle: true),
		new AliasInfo("customskinloader", "customskinloader", null, "CustomSkinLoader", hasTitle: true),
		new AliasInfo("csl", "customskinloader", null, "CustomSkinLoader", hasTitle: true),
		new AliasInfo("万用皮肤", "customskinloader", null, "CustomSkinLoader", hasTitle: true),
		new AliasInfo("语音", "simple-voice-chat", null, "Simple Voice Chat", hasTitle: true),
		new AliasInfo("simple voice chat", "simple-voice-chat", null, "Simple Voice Chat", hasTitle: true),
		new AliasInfo("语音聊天", "simple-voice-chat", null, "Simple Voice Chat", hasTitle: true),
		new AliasInfo("真实地形", "terralith", null, "Terralith", hasTitle: true),
		new AliasInfo("terralith", "terralith", null, "Terralith", hasTitle: true),
		new AliasInfo("更好的地形", "terralith", null, "Terralith", hasTitle: true),
		new AliasInfo("更好的末地", "better-end", null, "Better End", hasTitle: true),
		new AliasInfo("更好的下界地形", "betternether", null, "BetterNether", hasTitle: true),
		new AliasInfo("生物群系", "biomes-o-plenty", 220318L, "Biomes O' Plenty", hasTitle: true),
		new AliasInfo("biomes o plenty", "biomes-o-plenty", 220318L, "Biomes O' Plenty", hasTitle: true),
		new AliasInfo("bop", "biomes-o-plenty", 220318L, "Biomes O' Plenty", hasTitle: true),
		new AliasInfo("超多生物群系", "biomes-o-plenty", 220318L, "Biomes O' Plenty", hasTitle: true),
		new AliasInfo("改进生物群系", "byg", null, "Oh The Biomes You'll Go", hasTitle: true),
		new AliasInfo("byg", "byg", null, "Oh The Biomes You'll Go", hasTitle: true),
		new AliasInfo("oh the biomes you'll go", "byg", null, "Oh The Biomes You'll Go", hasTitle: true),
		new AliasInfo("地物", "regions-unexplored", null, "Regions Unexplored", hasTitle: true),
		new AliasInfo("regions unexplored", "regions-unexplored", null, "Regions Unexplored", hasTitle: true),
		new AliasInfo("奇怪的装饰", "quark", 243121L, "Quark", hasTitle: true),
		new AliasInfo("更多的门", "more-doors", null, "More Doors", hasTitle: true),
		new AliasInfo("家具", "furniture-mod", 229088L, "Furniture Mod", hasTitle: true),
		new AliasInfo("家具mod", "furniture-mod", 229088L, "Furniture Mod", hasTitle: true),
		new AliasInfo("mrcrayfish的家具", "furniture-mod", 229088L, "MrCrayfish's Furniture Mod", hasTitle: true),
		new AliasInfo("枪械", "mrcrayfishs-gun-mod", 228832L, "MrCrayfish's Gun Mod", hasTitle: true),
		new AliasInfo("cgwm", "mrcrayfishs-gun-mod", 228832L, "MrCrayfish's Gun Mod", hasTitle: true),
		new AliasInfo("维克的现代战争", "vic-modern-warfare", null, "Vic's Modern Warfare", hasTitle: true),
		new AliasInfo("vic's modern warfare", "vic-modern-warfare", null, "Vic's Modern Warfare", hasTitle: true),
		new AliasInfo("现代战争", "vic-modern-warfare", null, "Vic's Modern Warfare", hasTitle: true),
		new AliasInfo("古代战争", "ancient-warfare", null, "Ancient Warfare", hasTitle: true),
		new AliasInfo("史诗战斗", "epic-fight", null, "Epic Fight", hasTitle: true),
		new AliasInfo("epic fight", "epic-fight", null, "Epic Fight", hasTitle: true),
		new AliasInfo("史诗格斗", "epic-fight", null, "Epic Fight", hasTitle: true),
		new AliasInfo("更好的战斗", "better-combat", null, "Better Combat", hasTitle: true),
		new AliasInfo("better combat", "better-combat", null, "Better Combat", hasTitle: true),
		new AliasInfo("更多武器mod", "weapon-mod", null, "Weapon Mod", hasTitle: true),
		new AliasInfo("钻石工具", "diamond-tools", null, "Diamond Tools", hasTitle: true),
		new AliasInfo("更多工具", "tool-belt", null, "Tool Belt", hasTitle: true),
		new AliasInfo("更多盔甲", "armor-plus", null, "Armor Plus", hasTitle: true),
		new AliasInfo("更多附魔书", "spellbound", null, "Spellbound", hasTitle: true),
		new AliasInfo("魔法", "ars-nouveau", null, "Ars Nouveau", hasTitle: true),
		new AliasInfo("ars nouveau", "ars-nouveau", null, "Ars Nouveau", hasTitle: true),
		new AliasInfo("新生魔法", "ars-nouveau", null, "Ars Nouveau", hasTitle: true),
		new AliasInfo("巫术", "witchcraft", null, "Witchcraft", hasTitle: true),
		new AliasInfo("witchcraft", "witchcraft", null, "Witchcraft", hasTitle: true),
		new AliasInfo("血魔法", "blood-magic", 224791L, "Blood Magic", hasTitle: true),
		new AliasInfo("blood magic", "blood-magic", 224791L, "Blood Magic", hasTitle: true),
		new AliasInfo("星辉魔法", "astral-sorcery", 241721L, "Astral Sorcery", hasTitle: true),
		new AliasInfo("astral sorcery", "astral-sorcery", 241721L, "Astral Sorcery", hasTitle: true),
		new AliasInfo("自然灵气", "nature-essence", null, "Nature's Essence", hasTitle: true),
		new AliasInfo("元素工艺", "elemental-craft", null, "Elemental Craft", hasTitle: true),
		new AliasInfo("神秘农业", "mystical-agriculture", 246640L, "Mystical Agriculture", hasTitle: true),
		new AliasInfo("mystical agriculture", "mystical-agriculture", 246640L, "Mystical Agriculture", hasTitle: true),
		new AliasInfo("神秘农业扩展", "mystical-agriculture-extensions", null, "Mystical Agriculture Extensions", hasTitle: true),
		new AliasInfo("矿石作物", "mystical-agriculture", 246640L, "Mystical Agriculture", hasTitle: true),
		new AliasInfo("懒人农业", "simple-farming", null, "Simple Farming", hasTitle: true),
		new AliasInfo("simple farming", "simple-farming", null, "Simple Farming", hasTitle: true),
		new AliasInfo("更多农作物", "pam-harvestcraft-2", 221857L, "Pam's HarvestCraft 2", hasTitle: true),
		new AliasInfo("集装箱", "shulker-box-tooltip", null, "Shulker Box Tooltip", hasTitle: true),
		new AliasInfo("潜影盒预览", "shulker-box-tooltip", null, "Shulker Box Tooltip", hasTitle: true),
		new AliasInfo("shulker box tooltip", "shulker-box-tooltip", null, "Shulker Box Tooltip", hasTitle: true),
		new AliasInfo("潜影盒", "iron-shulker-boxes", null, "Iron Shulker Boxes", hasTitle: true),
		new AliasInfo("iron shulker boxes", "iron-shulker-boxes", null, "Iron Shulker Boxes", hasTitle: true),
		new AliasInfo("打包", "packing-tape", null, "Packing Tape", hasTitle: true),
		new AliasInfo("区块加载", "chunk-pregenerator", null, "Chunk Pregenerator", hasTitle: true),
		new AliasInfo("chunk pregenerator", "chunk-pregenerator", null, "Chunk Pregenerator", hasTitle: true),
		new AliasInfo("预生成区块", "chunk-pregenerator", null, "Chunk Pregenerator", hasTitle: true),
		new AliasInfo("多世界", "multiverse", null, "MultiVerse", hasTitle: true),
		new AliasInfo("multiverse", "multiverse", null, "MultiVerse", hasTitle: true),
		new AliasInfo("多世界管理", "multiverse", null, "MultiVerse", hasTitle: true),
		new AliasInfo("区域保护", "worldguard", null, "WorldGuard", hasTitle: true),
		new AliasInfo("worldguard", "worldguard", null, "WorldGuard", hasTitle: true),
		new AliasInfo("领地", "griefprevention", null, "GriefPrevention", hasTitle: true),
		new AliasInfo("griefprevention", "griefprevention", null, "GriefPrevention", hasTitle: true),
		new AliasInfo("登录", "authme", null, "AuthMe", hasTitle: true),
		new AliasInfo("authme", "authme", null, "AuthMe", hasTitle: true),
		new AliasInfo("皮肤站", "blessing-skin", null, "Blessing Skin", hasTitle: true),
		new AliasInfo("更好的钓鱼", "better-fishing", null, "Better Fishing", hasTitle: true),
		new AliasInfo("better fishing", "better-fishing", null, "Better Fishing", hasTitle: true),
		new AliasInfo("更好的下雨", "better-weather", null, "Better Weather", hasTitle: true),
		new AliasInfo("better weather", "better-weather", null, "Better Weather", hasTitle: true),
		new AliasInfo("更好的雪", "snow-realistic", null, "Snow! Realistic", hasTitle: true),
		new AliasInfo("更好的树叶", "better-leaves", null, "Better Leaves", hasTitle: true),
		new AliasInfo("经验掉落", "experience-bottling", null, "Experience Bottling", hasTitle: true),
		new AliasInfo("附魔书", "experience-bottling", null, "Experience Bottling", hasTitle: true)
	};

	private static readonly AliasInfo[] _MODPACK_ALIASES_LOWER = new AliasInfo[29]
	{
		new AliasInfo("gtnh", null, 252507L, "GregTech: New Horizons", hasTitle: true),
		new AliasInfo("格雷科技新视野", null, 252507L, "GregTech: New Horizons", hasTitle: true),
		new AliasInfo("gregtech new horizons", null, 252507L, "GregTech: New Horizons", hasTitle: true),
		new AliasInfo("gt new horizons", null, 252507L, "GregTech: New Horizons", hasTitle: true),
		new AliasInfo("rlcraft", null, 285109L, "RLCraft", hasTitle: true),
		new AliasInfo("真实生存", null, 285109L, "RLCraft", hasTitle: true),
		new AliasInfo("更好的mc", null, 429793L, "Better MC", hasTitle: true),
		new AliasInfo("better mc", null, 429793L, "Better MC", hasTitle: true),
		new AliasInfo("better minecraft", null, 429793L, "Better MC", hasTitle: true),
		new AliasInfo("空岛", "simply-skyblock", null, "Simply Skyblock", hasTitle: true),
		new AliasInfo("skyblock", "simply-skyblock", null, "Simply Skyblock", hasTitle: true),
		new AliasInfo("空岛生存", "simply-skyblock", null, "Simply Skyblock", hasTitle: true),
		new AliasInfo("机械动力", null, 1238396L, "机械动力：黄铜协奏曲", hasTitle: true),
		new AliasInfo("机械动力整合包", null, 1238396L, "机械动力：黄铜协奏曲", hasTitle: true),
		new AliasInfo("机械动力整合", null, 1238396L, "机械动力：黄铜协奏曲", hasTitle: true),
		new AliasInfo("黄铜协奏曲", null, 1238396L, "机械动力：黄铜协奏曲", hasTitle: true),
		new AliasInfo("cbc", null, 1238396L, "机械动力：黄铜协奏曲", hasTitle: true),
		new AliasInfo("brass concerto", null, 1238396L, "机械动力：黄铜协奏曲", hasTitle: true),
		new AliasInfo("create the brass concerto", null, 1238396L, "机械动力：黄铜协奏曲", hasTitle: true),
		new AliasInfo("create-the-brass-concerto", null, 1238396L, "机械动力：黄铜协奏曲", hasTitle: true),
		new AliasInfo("齿轮盛宴", null, 1059094L, "机械动力：齿轮盛宴", hasTitle: true),
		new AliasInfo("cdc", null, 1059094L, "机械动力：齿轮盛宴", hasTitle: true),
		new AliasInfo("create delight", null, 1059094L, "机械动力：齿轮盛宴", hasTitle: true),
		new AliasInfo("create delight remake", null, 1059094L, "机械动力：齿轮盛宴", hasTitle: true),
		new AliasInfo("create: delight remake", null, 1059094L, "机械动力：齿轮盛宴", hasTitle: true),
		new AliasInfo("create+", "create_plus", null, "Create+", hasTitle: true),
		new AliasInfo("create plus", "create_plus", null, "Create+", hasTitle: true),
		new AliasInfo("createplus", "create_plus", null, "Create+", hasTitle: true),
		new AliasInfo("机械动力+", "create_plus", null, "Create+", hasTitle: true)
	};

	public static readonly Dictionary<string, string> MOD_DESC_CN = new Dictionary<string, string>
	{
		{ "fabric-api", "Fabric 模组开发的核心前置库，提供绝大多数模组依赖的接口。" },
		{ "create", "机械动力：用齿轮、传送带与蒸汽动力搭建自动化工厂，主打原版风格的大型机械。" },
		{ "jei", "物品管理器：在物品栏与合成界面直接查询配方、用途与来源。" },
		{ "rei", "物品管理器（REI）：JEI 的 Fabric 版，查看合成配方与物品用途。" },
		{ "emi", "物品管理器 EMI：现代界面的配方查询模组，支持合成、烧炼与村民交易。" },
		{ "sodium", "钠：重构渲染管线，大幅提升帧率、消除卡顿，Fabric 端首选渲染优化。" },
		{ "lithium", "锂：优化服务端与客户端 tick 逻辑，降低卡顿且几乎不改变原版行为。" },
		{ "iris", "光影加载器：为 Sodium 提供光影支持，兼容 OptiFine 光影包。" },
		{ "starlight", "星光：重写光照引擎，显著降低光照更新带来的卡顿。" },
		{ "ae2", "应用能源2：以存储网络与自动化合成闻名的科技模组。" },
		{ "botania", "植物魔法：以魔力花与自然之力驱动的植物系魔法模组。" },
		{ "applied-energistics-2", "应用能源2：以存储网络与自动化合成闻名的科技模组。" },
		{ "twilight-forest", "暮色森林：加入全新维度，含独特生物群系、地牢与 Boss。" },
		{ "journeymap", "旅行地图：小地图与全屏地图，支持路径点与坐标显示。" },
		{ "xaeros-minimap", "Xaero 小地图：轻量小地图，显示地形、生物与路径点。" },
		{ "xaeros-world-map", "Xaero 世界地图：与小地图配套的全屏世界地图。" },
		{ "mekanism", "通用机械：涵盖采矿、能源、传送与五倍矿物处理的综合科技模组。" },
		{ "thermal-expansion", "热力膨胀：以机器、能源与管道为核心的经典科技模组。" },
		{ "tinkers-construct", "匠魂：自由组合材料锻造工具与武器，含熔炼炉系统。" },
		{ "farmers-delight", "农夫乐事：新增烹饪、锅具与大量食物，丰富农业玩法。" },
		{ "quark", "夸克：大量贴近原版风格的小功能合集，可逐项开关。" },
		{ "appleskin", "苹果皮：显示食物恢复的饥饿值与饱和度。" },
		{ "wthit", "方块信息显示：准星指向方块/实体时显示名称与状态（WAILA 后继）。" },
		{ "jade", "玉：现代化的方块信息显示模组，WAILA 系列的替代品。" },
		{ "worldedit", "创世神：功能强大的世界编辑工具，支持批量建造与区域操作。" },
		{ "litematica", "投影：把建造蓝图投影到世界中，按图施工。" },
		{ "carry-on", "搬运：徒手搬起箱子、熔炉等方块而不掉落内容物。" },
		{ "simple-voice-chat", "简单语音聊天：游戏内就近声音传播的语音插件。" },
		{ "customskinloader", "皮肤加载器：支持离线皮肤与多皮肤站，无需正版也能换皮肤。" },
		{ "3dskinlayers", "3D 皮肤层：把玩家的帽子与外套层渲染成 3D 立体。" },
		{ "biomesoplenty", "超多群系：新增大量生物群系与植物，丰富世界生成。" },
		{ "iceandfire", "冰火传说：加入巨龙、神话生物与遗迹的冒险模组。" },
		{ "galacticraft-legacy", "星系：加入火箭、太空维度与各行星探索。" },
		{ "mowzies-mobs", "更多生物：新增风格贴近原版的 Boss 与敌对生物。" },
		{ "veinminer", "连锁采集：按住快捷键一次性挖掉整条矿脉。" },
		{ "betterf3", "更好的 F3：让调试界面更简洁、更易读、可自定义。" },
		{ "inventory-profiles-next", "物品栏配置：一键保存/加载背包排序与整理方案。" }
	};

	private static readonly Dictionary<string, string> _MOD_DESC_BY_TITLE = new Dictionary<string, string>
	{
		{ "just enough items (jei)", "物品管理器：在物品栏与合成界面直接查询配方、用途与来源。" },
		{ "roughly enough items (rei)", "物品管理器（REI）：JEI 的 Fabric 版，查看合成配方与物品用途。" },
		{ "emi", "物品管理器 EMI：现代界面的配方查询模组，支持合成、烧炼与村民交易。" },
		{ "xaero's minimap", "Xaero 小地图：轻量小地图，显示地形、生物与路径点。" },
		{ "xaero's world map", "Xaero 世界地图：与小地图配套的全屏世界地图。" },
		{ "iris shaders", "光影加载器：为 Sodium 提供光影支持，兼容 OptiFine 光影包。" },
		{ "fabric api", "Fabric 模组开发的核心前置库，提供绝大多数模组依赖的接口。" },
		{ "journeymap", "旅行地图：小地图与全屏地图，支持路径点与坐标显示。" },
		{ "jade", "玉：现代化的方块信息显示模组，WAILA 系列的替代品。" },
		{ "twilight forest", "暮色森林：加入全新维度，含独特生物群系、地牢与 Boss。" },
		{ "applied energistics 2", "应用能源2：以存储网络与自动化合成闻名的科技模组。" },
		{ "create", "机械动力：用齿轮、传送带与蒸汽动力搭建自动化工厂，主打原版风格的大型机械。" },
		{ "botania", "植物魔法：以魔力花与自然之力驱动的植物系魔法模组。" },
		{ "tinkers' construct", "匠魂：自由组合材料锻造工具与武器，含熔炼炉系统。" },
		{ "thermal expansion", "热力膨胀：以机器、能源与管道为核心的经典科技模组。" },
		{ "appleskin", "苹果皮：显示食物恢复的饥饿值与饱和度。" },
		{ "what is that? (wthit)", "方块信息显示：准星指向方块/实体时显示名称与状态（WAILA 后继）。" },
		{ "veinminer", "连锁采集：按住快捷键一次性挖掉整条矿脉。" },
		{ "litematica", "投影：把建造蓝图投影到世界中，按图施工。" },
		{ "inventory profiles next", "物品栏配置：一键保存/加载背包排序与整理方案。" },
		{ "betterf3", "更好的 F3：让调试界面更简洁、更易读、可自定义。" },
		{ "sodium", "钠：重构渲染管线，大幅提升帧率、消除卡顿，Fabric 端首选渲染优化。" },
		{ "sodium (rubidium)", "钠：重构渲染管线，大幅提升帧率、消除卡顿，Fabric 端首选渲染优化。" },
		{ "lithium", "锂：优化服务端与客户端 tick 逻辑，降低卡顿且几乎不改变原版行为。" },
		{ "starlight", "星光：重写光照引擎，显著降低光照更新带来的卡顿。" },
		{ "quark", "夸克：大量贴近原版风格的小功能合集，可逐项开关。" },
		{ "farmer's delight", "农夫乐事：新增烹饪、锅具与大量食物，丰富农业玩法。" },
		{ "worldedit", "创世神：功能强大的世界编辑工具，支持批量建造与区域操作。" },
		{ "mowzie's mobs", "更多生物：新增风格贴近原版的 Boss 与敌对生物。" },
		{ "carry on", "搬运：徒手搬起箱子、熔炉等方块而不掉落内容物。" },
		{ "mekanism", "通用机械：涵盖采矿、能源、传送与五倍矿物处理的综合科技模组。" },
		{ "3d skin layers", "3D 皮肤层：把玩家的帽子与外套层渲染成 3D 立体。" },
		{ "customskinloader", "皮肤加载器：支持离线皮肤与多皮肤站，无需正版也能换皮肤。" },
		{ "simple voice chat", "简单语音聊天：游戏内就近声音传播的语音插件。" }
	};

	public static readonly object[][] POPULAR_MODS = new object[36][]
	{
		new object[5] { "Sodium 钠 (渲染优化)", "modrinth", "sodium", "1.20.1", "fabric" },
		new object[5] { "Lithium 锂 (服务器优化)", "modrinth", "lithium", null, "fabric" },
		new object[5] { "Starlight 星光 (光照优化)", "modrinth", "starlight", null, "fabric" },
		new object[5] { "Iris 光影加载器", "modrinth", "iris", null, "fabric" },
		new object[5] { "BetterF3", "modrinth", "betterf3", null, "fabric" },
		new object[5] { "JEI 物品管理器", "modrinth", "jei", null, "forge" },
		new object[5] { "REI 物品管理器", "modrinth", "rei", null, "fabric" },
		new object[5] { "EMI 物品管理器", "modrinth", "emi", null, "fabric" },
		new object[5] { "Xaero's 小地图", "modrinth", "xaeros-minimap", null, "fabric" },
		new object[5] { "Xaero's 世界地图", "modrinth", "xaeros-world-map", null, "fabric" },
		new object[5] { "JourneyMap 旅行地图", "curseforge", 32274L, null, "forge" },
		new object[5] { "AppleSkin 饥饿值显示", "modrinth", "appleskin", null, "fabric" },
		new object[5] { "WTHIT 方块信息", "modrinth", "wthit", null, "fabric" },
		new object[5] { "Litematica 投影", "modrinth", "litematica", null, "fabric" },
		new object[5] { "VeinMiner 连锁采集", "curseforge", 67133L, null, "forge" },
		new object[5] { "WorldEdit 创世神", "modrinth", "worldedit", null, "forge" },
		new object[5] { "Carry On 搬运", "modrinth", "carry-on", null, "fabric" },
		new object[5] { "Twilight Forest 暮色森林", "curseforge", 227639L, null, "forge" },
		new object[5] { "Applied Energistics 2 AE2", "curseforge", 223794L, null, "forge" },
		new object[5] { "Botania 植物魔法", "curseforge", 225643L, null, "forge" },
		new object[5] { "Tinkers' Construct 匠魂", "curseforge", 74072L, null, "forge" },
		new object[5] { "IndustrialCraft 2 工业", "curseforge", 242638L, null, "forge" },
		new object[5] { "Mekanism 通用机械", "curseforge", 268560L, null, "forge" },
		new object[5] { "Thermal Expansion 热力", "curseforge", 69163L, null, "forge" },
		new object[5] { "Create 机械动力", "modrinth", "create", null, "fabric" },
		new object[5] { "Farmer's Delight 农夫乐事", "modrinth", "farmers-delight", null, "fabric" },
		new object[5] { "Quark 夸克", "curseforge", 243121L, null, "forge" },
		new object[5] { "Ice and Fire 冰火传说", "curseforge", 264231L, null, "forge" },
		new object[5] { "Galacticraft Legacy 星系", "curseforge", 564236L, null, "forge" },
		new object[5] { "Biomes O' Plenty 超多群系", "curseforge", 220318L, null, "forge" },
		new object[5] { "Mowzie's Mobs 更多生物", "modrinth", "mowzies-mobs", null, "forge" },
		new object[5] { "CustomSkinLoader 皮肤加载", "modrinth", "customskinloader", null, "fabric" },
		new object[5] { "3D Skin Layers 皮肤层", "modrinth", "3dskinlayers", null, "fabric" },
		new object[5] { "Simple Voice Chat 语音", "modrinth", "simple-voice-chat", null, "fabric" },
		new object[5] { "Inventory Profiles 物品栏", "modrinth", "inventory-profiles-next", null, "fabric" },
		new object[5] { "Path of Exile 音效", "modrinth", "presence-footsteps", null, "fabric" }
	};

	public static readonly object[][] POPULAR_MODPACKS = new object[10][]
	{
		new object[4] { "机械动力：黄铜协奏曲 CBC 1.20.1", "curseforge", 1238396L, "create-the-brass-concerto" },
		new object[4] { "机械动力：齿轮盛宴 CDC 1.20.1", "curseforge", 1059094L, "create-delight-remake" },
		new object[4] { "RLCraft 真实生存", "curseforge", 285109L, "rlcraft" },
		new object[4] { "Better MC 更好的MC", "curseforge", 429793L, "better-mc-forge-bmc1" },
		new object[4] { "Simply Skyblock 空岛", "modrinth", "simply-skyblock", null },
		new object[4] { "Create+（原版风 1.19.2，不是CBC）", "modrinth", "create_plus", null },
		new object[4] { "GTNH 格雷科技新视野", "curseforge", 252507L, "gt-new-horizons" },
		new object[4] { "Fabulously Optimized", "modrinth", "fabulously-optimized", null },
		new object[4] { "Adrenaline", "modrinth", "adrenaline", null },
		new object[4] { "All Of Create", "modrinth", "all-of-create-fabric", null }
	};

	private static Dictionary<string, AliasInfo> Index(AliasInfo[] rows)
	{
		Dictionary<string, AliasInfo> dictionary = new Dictionary<string, AliasInfo>();
		foreach (AliasInfo aliasInfo in rows)
		{
			dictionary[aliasInfo.Key] = aliasInfo;
		}
		return dictionary;
	}

	public static object ModDescCn(object slug = null, object title = null, object fallback = null)
	{
		string key = Py.Lower(Py.Strip(Py.S(slug)));
		if (MOD_DESC_CN.TryGetValue(key, out string value))
		{
			return value;
		}
		string text = Py.Lower(Py.Strip(Py.S(title)));
		if (text.Length > 0 && _MOD_DESC_BY_TITLE.TryGetValue(text, out string value2))
		{
			return value2;
		}
		return fallback ?? "";
	}

	public static object[] LookupModAlias(string query)
	{
		if (_modIdx == null)
		{
			_modIdx = Index(_MOD_ALIASES_LOWER);
		}
		string text = Py.Lower(Py.Strip(query));
		if (_modIdx.TryGetValue(text, out AliasInfo value))
		{
			return new object[3]
			{
				value.Slug,
				value.Cf,
				value.HasTitle ? value.Title : text
			};
		}
		return new object[3];
	}

    public static IEnumerable<ResourceName> ResourceAliases()
    {
        foreach (var group in _MOD_ALIASES_LOWER.Where(alias => alias.Key is not ("高清修复" or "optifine" or "光影"))
            .Select(alias => (Kind: "mod", Alias: alias)).Concat(_MODPACK_ALIASES_LOWER.Select(alias => (Kind: "modpack", Alias: alias)))
            .GroupBy(item => item.Kind + "|" + item.Alias.Slug + "|" + item.Alias.Cf + "|" + item.Alias.Title))
        {
            var first = group.First();
            yield return new ResourceName { Kind = first.Kind, English = first.Alias.Title ?? "", Modrinth = first.Alias.Slug ?? "",
                CurseforgeId = Py.S(first.Alias.Cf), Chinese = group.Select(item => item.Alias.Key).FirstOrDefault(ResourceNames.ContainsChinese) ?? "",
                Aliases = group.Select(item => item.Alias.Key).ToArray() };
        }
    }

	public static List<object[]> FuzzyMatchMod(string query)
	{
		string text = Py.Lower(Py.Strip(query));
		List<object[]> list = new List<object[]>();
		AliasInfo[] mOD_ALIASES_LOWER = _MOD_ALIASES_LOWER;
		foreach (AliasInfo aliasInfo in mOD_ALIASES_LOWER)
		{
			string key = aliasInfo.Key;
			if (key.Contains(text) || text.Contains(key))
			{
				list.Add(new object[4]
				{
					aliasInfo.Slug,
					aliasInfo.Cf,
					aliasInfo.HasTitle ? aliasInfo.Title : key,
					key
				});
			}
		}
		return list;
	}

	public static object[] LookupModpackAlias(string query)
	{
		if (_packIdx == null)
		{
			_packIdx = Index(_MODPACK_ALIASES_LOWER);
		}
		string text = Py.Lower(Py.Strip(query));
		if (_packIdx.TryGetValue(text, out AliasInfo value))
		{
			return new object[3]
			{
				value.Slug,
				value.Cf,
				value.HasTitle ? value.Title : text
			};
		}
		return new object[3];
	}
}
