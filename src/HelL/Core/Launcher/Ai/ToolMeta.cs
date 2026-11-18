using System.Collections.Generic;
using System.Linq;

namespace HelL.Core.Launcher.Ai;

public static class ToolMeta
{
	private static Dictionary<string, ToolMetaInfo> _map;

	public static readonly ToolMetaInfo[] ALL = new ToolMetaInfo[38]
	{
		new ToolMetaInfo("ask_user", ro: true, "none", "low", lr: false),
		new ToolMetaInfo("get_launcher_state", ro: true, "none", "low", lr: false),
		new ToolMetaInfo("list_instances", ro: true, "none", "low", lr: false),
		new ToolMetaInfo("list_installed_versions", ro: true, "none", "low", lr: false),
		new ToolMetaInfo("search_versions", ro: true, "network", "low", lr: false),
		new ToolMetaInfo("search_mods", ro: true, "network", "low", lr: false),
		new ToolMetaInfo("search_modpacks", ro: true, "network", "low", lr: false),
		new ToolMetaInfo("list_mods", ro: true, "read", "low", lr: false),
		new ToolMetaInfo("install_game", ro: false, "write_local", "high", lr: true),
		new ToolMetaInfo("install_mod", ro: false, "write_local", "medium", lr: true),
		new ToolMetaInfo("install_modpack", ro: false, "write_local", "high", lr: true),
		new ToolMetaInfo("install_shader", ro: false, "write_local", "low", lr: true),
		new ToolMetaInfo("install_resourcepack", ro: false, "write_local", "low", lr: true),
		new ToolMetaInfo("install_datapack", ro: false, "write_local", "low", lr: true),
		new ToolMetaInfo("search_content", ro: true, "network", "low", lr: false),
		new ToolMetaInfo("search_worlds", ro: true, "network", "low", lr: false),
		new ToolMetaInfo("install_world", ro: false, "write_local", "medium", lr: true),
		new ToolMetaInfo("create_instance", ro: false, "write_local", "medium", lr: false),
		new ToolMetaInfo("delete_instance", ro: false, "delete", "high", lr: false),
		new ToolMetaInfo("delete_mod", ro: false, "delete", "high", lr: false),
		new ToolMetaInfo("disable_mod", ro: false, "write_local", "medium", lr: false),
		new ToolMetaInfo("enable_mod", ro: false, "write_local", "medium", lr: false),
		new ToolMetaInfo("get_java_list", ro: true, "none", "low", lr: false),
		new ToolMetaInfo("download_java", ro: false, "write_local", "medium", lr: true),
		new ToolMetaInfo("launch_game", ro: false, "launch", "high", lr: false),
		new ToolMetaInfo("diagnose_launch", ro: true, "read", "low", lr: false),
		new ToolMetaInfo("get_latest_log", ro: true, "read", "low", lr: false),
		new ToolMetaInfo("get_crash_report", ro: true, "read", "low", lr: false),
		new ToolMetaInfo("scan_mod_conflicts", ro: true, "read", "low", lr: false),
		new ToolMetaInfo("inspect_mod", ro: true, "read", "low", lr: false),
		new ToolMetaInfo("list_mod_configs", ro: true, "read", "low", lr: false),
		new ToolMetaInfo("read_mod_config", ro: true, "read", "low", lr: false),
		new ToolMetaInfo("write_mod_config", ro: false, "write_local", "high", lr: false),
		new ToolMetaInfo("read_artifact", ro: true, "read", "low", lr: false),
		new ToolMetaInfo("dispatch_subagent", ro: true, "none", "low", lr: false),
		new ToolMetaInfo("update_plan", ro: true, "none", "low", lr: false),
		new ToolMetaInfo("remember", ro: true, "none", "low", lr: false),
		new ToolMetaInfo("forget", ro: true, "none", "low", lr: false)
	};

	public static Dictionary<string, ToolMetaInfo> Map => _map ?? (_map = ALL.Concat(ExtraTools.Meta).ToDictionary((ToolMetaInfo m) => m.name));

	public static ToolMetaInfo Get(object name)
	{
        if (name is string gameTool && gameTool.StartsWith(Minecraft.MinecraftSession.Prefix, System.StringComparison.Ordinal))
            return Minecraft.MinecraftSession.Meta(gameTool);
		if (!(name is string key) || !Map.TryGetValue(key, out ToolMetaInfo value))
		{
			return null;
		}
		return value;
	}
}
