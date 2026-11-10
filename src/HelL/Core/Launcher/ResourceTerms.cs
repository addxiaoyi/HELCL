using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace HelL.Core.Launcher;

public static class ResourceTerms
{
    public static readonly IReadOnlyDictionary<string, string> Chinese = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["entities"]="实体", ["blocks"]="方块", ["items"]="物品", ["tweaks"]="细节调整", ["fantasy"]="奇幻",
        ["locale"]="语言", ["modded"]="模组适配", ["minecraft"]="我的世界", ["iris"]="Iris 光影", ["optifine"]="OptiFine 高清修复",
        ["16x"]="16× 分辨率", ["32x"]="32× 分辨率", ["64x"]="64× 分辨率", ["128x"]="128× 分辨率", ["256x"]="256× 分辨率",
        ["512x"]="512× 分辨率", ["kitchen-sink"]="综合玩法", ["quests"]="任务", ["multiplayer"]="多人联机", ["lightweight"]="轻量",
        ["challenging"]="挑战", ["combat"]="战斗", ["hardcore"]="极限生存", ["skyblock"]="空岛", ["parkour"]="跑酷",
        ["puzzle"]="解谜", ["puzzles"]="解谜", ["horror"]="恐怖", ["creation"]="创造", ["survival"]="生存", ["minigame"]="小游戏",
        ["performance"]="高性能", ["potato"]="低配", ["path-tracing"]="路径追踪", ["ray-tracing"]="光线追踪", ["cartoon"]="卡通",
        ["semi-realistic"]="半写实", ["low"]="低配置", ["medium"]="中等配置", ["high"]="高配置", ["screenshot"]="截图",
        ["colored-lighting"]="彩色光照", ["shadows"]="阴影", ["reflections"]="反射", ["pbr"]="物理材质", ["atmosphere"]="环境氛围",
        ["utility"]="实用工具", ["optimization"]="性能优化", ["technology"]="科技", ["magic"]="魔法", ["adventure"]="冒险",
        ["worldgen"]="世界生成", ["world-generation"]="世界生成", ["decoration"]="装饰", ["storage"]="存储", ["equipment"]="装备",
        ["management"]="管理", ["game-mechanics"]="游戏机制", ["library"]="前置库", ["food"]="食物", ["mobs"]="生物", ["transportation"]="交通",
        ["social"]="社交", ["realistic"]="写实", ["vanilla-like"]="原版风格", ["simplistic"]="简约", ["themed"]="主题", ["cursed"]="趣味",
        ["audio"]="音效", ["font"]="字体", ["gui"]="界面", ["animated"]="动态效果", ["modern"]="现代", ["rpg"]="角色扮演",
        ["adventure and rpg"]="冒险与角色扮演", ["world gen"]="世界生成", ["map and information"]="地图与信息", ["api and library"]="接口与前置库",
        ["cosmetic"]="外观装饰", ["server utility"]="服务端工具", ["miscellaneous"]="其他", ["small / light"]="小型轻量", ["extra large"]="大型整合包",
        ["environment"]="环境", ["models"]="模型", ["economy"]="经济", ["bloom"]="泛光", ["foliage"]="植被", ["core-shaders"]="核心着色器",
        ["48x"]="48× 分辨率", ["512x+"]="512× 及更高分辨率", ["8x-"]="8× 及更低分辨率", ["512x and higher"]="512× 及更高分辨率",
        ["armor, tools, and weapons"]="盔甲、工具与武器", ["traditional"]="传统风格", ["photo realistic"]="照片级写实", ["utility & qol"]="实用与体验改善",
        ["steampunk"]="蒸汽朋克", ["education"]="教育", ["game map"]="游戏地图", ["integrated dynamics"]="集成动力", ["tinker's construct"]="匠魂",
        ["addons"]="附属", ["industrial craft"]="工业时代", ["blood magic"]="血魔法", ["bug fixes"]="问题修复", ["applied energistics 2"]="应用能源 2",
        ["create"]="机械动力", ["font packs"]="字体包", ["galacticraft"]="星系", ["automation"]="自动化", ["redstone"]="红石", ["processing"]="加工",
        ["hardcore questing mode"]="极限任务模式", ["crafttweaker"]="CraftTweaker 配方修改", ["twitch integration"]="Twitch 联动", ["mod support"]="模组适配",
        ["data packs"]="数据包", ["guidebook"]="指南", ["mcreator"]="MCreator 模组", ["modded world"]="模组世界", ["sci-fi"]="科幻",
        ["kubejs"]="KubeJS 脚本", ["progression"]="流程推进", ["player transport"]="玩家交通", ["building gadgets"]="建筑小帮手", ["buildcraft"]="建筑工艺",
        ["ftb official pack"]="FTB 官方整合包", ["ores and resources"]="矿石与资源", ["vanilla+"]="原版增强", ["thaumcraft"]="神秘时代", ["dimensions"]="维度",
        ["mini game"]="小游戏", ["combat / pvp"]="战斗与玩家对战", ["configuration"]="配置", ["energy"]="能源", ["twilight forest"]="暮色森林", ["tech"]="科技",
        ["energy, fluid, and item transport"]="能量、流体与物品运输", ["genetics"]="遗传学", ["exploration"]="探索", ["map based"]="地图玩法",
        ["thermal expansion"]="热力膨胀", ["forestry"]="林业", ["fancymenu"]="FancyMenu 菜单", ["scripts"]="脚本", ["recipes"]="配方",
        ["modjam 2025"]="ModJam 2025 作品", ["creativemode"]="创造模式", ["refined storage"]="精致存储", ["expert"]="专家模式", ["rlcraft"]="RLCraft 真实生存",
        ["farmer's delight"]="农夫乐事", ["forge"]="Forge", ["fabric"]="Fabric", ["neoforge"]="NeoForge", ["quilt"]="Quilt",
        ["fonts"]="字体", ["structures"]="结构", ["farming"]="农业", ["medieval"]="中世纪", ["biomes"]="生物群系", ["vanilla"]="原版",
        ["datapack"]="数据包", ["resourcepack"]="资源包", ["modpack"]="整合包", ["shader"]="光影", ["mod"]="模组",
        ["bukkit"]="Bukkit", ["paper"]="Paper", ["folia"]="Folia", ["purpur"]="Purpur", ["sponge"]="Sponge"
    };

    private static readonly Dictionary<string, string> QueryWords = new()
    {
        ["空岛生存"]="skyblock", ["单方块"]="oneblock", ["幸运方块"]="lucky block", ["跑酷"]="parkour", ["解谜"]="puzzle", ["恐怖"]="horror",
        ["生存"]="survival", ["空岛"]="skyblock", ["城市"]="city", ["中世纪"]="medieval", ["城堡"]="castle", ["村庄"]="village", ["地牢"]="dungeon",
        ["优化"]="optimization", ["高性能"]="performance", ["低配"]="potato", ["写实"]="realistic", ["卡通"]="cartoon", ["光追"]="path tracing",
        ["动态"]="animated", ["动画"]="animations", ["材质"]="texture", ["高清"]="HD", ["原版"]="vanilla", ["科技"]="technology", ["魔法"]="magic",
        ["冒险"]="adventure", ["建筑"]="building", ["装饰"]="decoration", ["工具"]="utility", ["地图"]="map", ["背包"]="backpack", ["农场"]="farm",
        ["农业"]="farming", ["武器"]="weapons", ["战斗"]="combat", ["生物"]="mobs", ["群系"]="biomes", ["地形"]="terrain", ["红石"]="redstone",
        ["传送"]="teleport", ["任务"]="quests", ["整合包"]="modpack", ["资源包"]="resource pack", ["数据包"]="datapack", ["光影"]="shaders", ["模组"]="mod",
        ["世界"]="world", ["中文"]="Chinese", ["汉化"]="Chinese translation", ["简约"]="simple", ["音效"]="sounds", ["字体"]="font"
    };

    public static string TranslateQuery(string kind, string query)
    {
        string text = query.Normalize(System.Text.NormalizationForm.FormKC).Trim();
        foreach (var term in QueryWords.OrderByDescending(pair => pair.Key.Length)) text = text.Replace(term.Key, " " + term.Value + " ");
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    private static readonly IReadOnlyDictionary<string, string> TitleWords = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["skyblock"]="空岛", ["oneblock"]="单方块", ["lucky blocks"]="幸运方块", ["lucky block"]="幸运方块", ["parkour"]="跑酷",
        ["survival"]="生存", ["starter base"]="新手基地", ["city"]="城市", ["castle"]="城堡", ["medieval"]="中世纪", ["horror"]="恐怖",
        ["adventure"]="冒险", ["puzzle"]="解谜", ["dungeons"]="地牢", ["villages"]="村庄", ["towers"]="高塔", ["islands"]="岛屿",
        ["fresh animations"]="生动动画", ["animations"]="动画", ["extensions"]="扩展", ["extension"]="扩展", ["player"]="玩家",
        ["shaders"]="光影", ["shader"]="光影", ["textures"]="材质", ["texture pack"]="材质包", ["resource pack"]="资源包",
        ["datapack"]="数据包", ["modpack"]="整合包", ["compatibility"]="兼容", ["compat"]="兼容", ["addon"]="附属", ["add-on"]="附属",
        ["translations"]="翻译", ["translation"]="翻译", ["optimized"]="优化", ["optimization"]="优化", ["performance"]="性能",
        ["better"]="增强", ["simple"]="简易", ["realistic"]="写实", ["default"]="默认", ["classic"]="经典", ["vanilla"]="原版",
        ["lucky"]="幸运", ["weapons"]="武器", ["tools"]="工具", ["blocks"]="方块", ["items"]="物品", ["mobs"]="生物", ["animals"]="动物",
        ["biomes"]="群系", ["terrain"]="地形", ["sounds"]="音效", ["music"]="音乐", ["font"]="字体", ["fonts"]="字体", ["dark mode"]="深色模式",
        ["inventory"]="物品栏", ["backpack"]="背包", ["backpacks"]="背包", ["storage"]="存储", ["chests"]="箱子", ["farming"]="农业",
        ["delight"]="乐事", ["cutting"]="切割", ["quests"]="任务", ["structures"]="结构", ["world"]="世界", ["map"]="地图",
        ["official"]="官方", ["ultimate"]="终极", ["reimagined"]="重制", ["unbound"]="无限", ["remastered"]="重制", ["remake"]="重制"
    };

    public static string TranslateTitle(string original)
    {
        string text = original;
        foreach (var term in TitleWords.OrderByDescending(pair => pair.Key.Length))
            text = Regex.Replace(text, @"(?<![\p{L}\p{N}])" + Regex.Escape(term.Key) + @"(?![\p{L}\p{N}])", term.Value, RegexOptions.IgnoreCase);
        return text;
    }
}
