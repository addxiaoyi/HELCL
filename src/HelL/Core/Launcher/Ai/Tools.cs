using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;

namespace HelL.Core.Launcher.Ai;

public static class Tools
{
	private static List<object> _schemas;

	private static DownloadManager _CJK_DM;

	private const string _OFFLINE_KEY = "离线模式";

	private static readonly Dictionary<string, string> _TOOL_LABELS = new Dictionary<string, string>
	{
		{ "diagnose_launch", "诊断启动" },
		{ "get_crash_report", "读崩溃报告" },
		{ "get_installed_versions", "查已装版本" },
		{ "get_java_list", "查 Java 列表" },
		{ "get_launcher_state", "查启动器状态" },
		{ "get_latest_log", "读最新日志" },
		{ "inspect_mod", "检查模组" },
		{ "list_installed_versions", "查已装版本" },
		{ "list_instances", "查实例列表" },
		{ "list_mod_configs", "查模组配置" },
		{ "list_mods", "查模组列表" },
		{ "read_artifact", "读文件" },
		{ "read_mod_config", "读模组配置" },
		{ "scan_mod_conflicts", "扫描模组冲突" },
		{ "search_content", "搜内容" },
		{ "search_modpacks", "搜整合包" },
		{ "search_mods", "搜模组" },
		{ "search_versions", "搜版本" },
		{ "search_worlds", "搜地图" },
		{ "dispatch_subagent", "派发子任务" },
		{ "update_plan", "更新计划" },
		{ "remember", "记住" },
		{ "forget", "忘掉" }
	};

	private static readonly Dictionary<string, string> _FMT = new Dictionary<string, string>
	{
		{ "install_game", "安装游戏 {version} {loader} → {inst}" },
		{ "install_mod", "安装模组 {name} → {inst}" },
		{ "install_modpack", "安装整合包 {name} → {inst}" },
		{ "install_shader", "安装光影 {name} → {inst}" },
		{ "install_resourcepack", "安装资源包 {name} → {inst}" },
		{ "install_datapack", "安装数据包 {name} → {inst}" },
		{ "install_world", "安装地图 {name} → {inst}" },
		{ "download_java", "下载 Java {major}" },
		{ "launch_game", "启动 {version} @ {inst}" },
		{ "create_instance", "新建实例 {name}" },
		{ "delete_instance", "删除实例 {name}（不可恢复）" },
		{ "delete_mod", "删除模组 {filename} @ {inst}" },
		{ "disable_mod", "禁用模组 {filename} @ {inst}" },
		{ "enable_mod", "启用模组 {filename} @ {inst}" },
		{ "write_mod_config", "改配置 {path} @ {inst}" },
		{ "plan_approval", "批准这份计划并继续？{n} 项待办" }
	};

	private static readonly Dictionary<string, string> _ERROR_READABLE = new Dictionary<string, string>
	{
		{ "bad_arguments", "工具参数不合法" },
		{ "not_found", "目标不存在" },
		{ "permission_denied", "没有权限执行" },
		{ "timeout", "执行超时" },
		{ "cancelled", "已被用户停止" },
		{ "io_error", "读写文件失败" },
		{ "upstream_error", "上游服务出错" },
		{ "unknown", "执行失败" }
	};

	private static readonly Regex _KV_STR = new Regex("\"?(\\w+)\"?\\s*[:=]\\s*\"([^\"]*)\"");

	private static readonly Regex _KV_BARE = new Regex("\"?(\\w+)\"?\\s*[:=]\\s*([-\\w.]+)");

	private static readonly HashSet<string> _NO_STRIP = new HashSet<string> { "content", "text", "message" };

	public static List<object> TOOL_SCHEMAS => _schemas ?? (_schemas = WithMemoryTools((List<object>)PyJson.Loads("[{\"type\": \"function\", \"function\": {\"name\": \"ask_user\", \"description\": \"向用户弹出结构化选择题。需要用户选实例、加载器、搜到多个结果、冲突留哪个时必须用这个，不要只在文字里问。界面会自动加「其他」让用户自己填。没选完之前不要调用 install_*。用户选完后你必须立刻再调对应工具，不能结束。\", \"parameters\": {\"type\": \"object\", \"properties\": {\"title\": {\"type\": \"string\", \"description\": \"可选，整组题的小标题\"}, \"prompt\": {\"type\": \"string\", \"description\": \"单题时的问题\"}, \"allow_multiple\": {\"type\": \"boolean\", \"description\": \"单题时是否可多选，默认 false\"}, \"options\": {\"type\": \"array\", \"description\": \"单题选项。每项可以是字符串，或 {id, label}\", \"items\": {\"type\": \"object\", \"properties\": {\"id\": {\"type\": \"string\"}, \"label\": {\"type\": \"string\"}}}}, \"questions\": {\"type\": \"array\", \"description\": \"多题时用。每题 {id, prompt, allow_multiple, options:[{id,label}]}，至少 2 个选项\", \"items\": {\"type\": \"object\"}}}}}}, {\"type\": \"function\", \"function\": {\"name\": \"get_launcher_state\", \"description\": \"查看实例、已装版本、Java、模组数量等当前状态\", \"parameters\": {\"type\": \"object\", \"properties\": {}}}}, {\"type\": \"function\", \"function\": {\"name\": \"list_instances\", \"description\": \"列出全部实例\", \"parameters\": {\"type\": \"object\", \"properties\": {}}}}, {\"type\": \"function\", \"function\": {\"name\": \"list_installed_versions\", \"description\": \"列出某实例已安装的游戏版本\", \"parameters\": {\"type\": \"object\", \"properties\": {\"instance\": {\"type\": \"string\", \"description\": \"实例名，空则用默认\"}}}}}, {\"type\": \"function\", \"function\": {\"name\": \"search_versions\", \"description\": \"搜索可下载的 Minecraft 版本号\", \"parameters\": {\"type\": \"object\", \"properties\": {\"query\": {\"type\": \"string\", \"description\": \"如 1.20.1 或 25w\"}, \"kind\": {\"type\": \"string\", \"description\": \"release / snapshot / all\"}}, \"required\": [\"query\"]}}}, {\"type\": \"function\", \"function\": {\"name\": \"search_mods\", \"description\": \"搜索模组（支持中文名）。同一轮用户请求只调用一次；搜完必须 ask_user 让用户选，禁止换词再搜。\", \"parameters\": {\"type\": \"object\", \"properties\": {\"query\": {\"type\": \"string\"}, \"source\": {\"type\": \"string\", \"description\": \"全部 / Modrinth / CurseForge\"}}, \"required\": [\"query\"]}}}, {\"type\": \"function\", \"function\": {\"name\": \"search_modpacks\", \"description\": \"搜索整合包（支持中文名）。同一轮只调用一次，搜完 ask_user，禁止换词再搜。\", \"parameters\": {\"type\": \"object\", \"properties\": {\"query\": {\"type\": \"string\"}, \"source\": {\"type\": \"string\", \"description\": \"全部 / Modrinth / CurseForge\"}}, \"required\": [\"query\"]}}}, {\"type\": \"function\", \"function\": {\"name\": \"list_mods\", \"description\": \"列出实例已装模组（含禁用）\", \"parameters\": {\"type\": \"object\", \"properties\": {\"instance\": {\"type\": \"string\"}}}}}, {\"type\": \"function\", \"function\": {\"name\": \"install_game\", \"description\": \"真正开始下载/安装 Minecraft。用户已选定版本后必须调用这个，否则不会下载。纯原版 loader 填「无」。\", \"parameters\": {\"type\": \"object\", \"properties\": {\"version\": {\"type\": \"string\", \"description\": \"如 1.20.1\"}, \"loader\": {\"type\": \"string\", \"description\": \"无 / Fabric / Forge / Quilt / NeoForge。纯原版必须填 无\"}, \"loader_version\": {\"type\": \"string\"}, \"instance\": {\"type\": \"string\"}}, \"required\": [\"version\"]}}}, {\"type\": \"function\", \"function\": {\"name\": \"install_mod\", \"description\": \"安装模组。优先传搜索结果里的 slug 或 id\", \"parameters\": {\"type\": \"object\", \"properties\": {\"name\": {\"type\": \"string\", \"description\": \"显示名或 slug\"}, \"instance\": {\"type\": \"string\"}, \"source\": {\"type\": \"string\"}, \"slug\": {\"type\": \"string\"}, \"id\": {\"type\": \"string\", \"description\": \"CurseForge 数字 id\"}}, \"required\": [\"name\"]}}}, {\"type\": \"function\", \"function\": {\"name\": \"install_modpack\", \"description\": \"安装整合包。建议先 create_instance\", \"parameters\": {\"type\": \"object\", \"properties\": {\"name\": {\"type\": \"string\"}, \"instance\": {\"type\": \"string\"}, \"source\": {\"type\": \"string\"}, \"slug\": {\"type\": \"string\"}, \"id\": {\"type\": \"string\"}}, \"required\": [\"name\"]}}}, {\"type\": \"function\", \"function\": {\"name\": \"install_shader\", \"description\": \"安装光影包\", \"parameters\": {\"type\": \"object\", \"properties\": {\"name\": {\"type\": \"string\"}, \"instance\": {\"type\": \"string\"}, \"source\": {\"type\": \"string\"}, \"slug\": {\"type\": \"string\"}}, \"required\": [\"name\"]}}}, {\"type\": \"function\", \"function\": {\"name\": \"install_resourcepack\", \"description\": \"安装资源包\", \"parameters\": {\"type\": \"object\", \"properties\": {\"name\": {\"type\": \"string\"}, \"instance\": {\"type\": \"string\"}, \"source\": {\"type\": \"string\"}, \"slug\": {\"type\": \"string\"}}, \"required\": [\"name\"]}}}, {\"type\": \"function\", \"function\": {\"name\": \"install_datapack\", \"description\": \"安装数据包\", \"parameters\": {\"type\": \"object\", \"properties\": {\"name\": {\"type\": \"string\"}, \"instance\": {\"type\": \"string\"}, \"source\": {\"type\": \"string\"}, \"slug\": {\"type\": \"string\"}}, \"required\": [\"name\"]}}}, {\"type\": \"function\", \"function\": {\"name\": \"search_content\", \"description\": \"搜索光影 / 资源包 / 数据包（支持中文名）。装之前先用它拿 slug 或 id。\", \"parameters\": {\"type\": \"object\", \"properties\": {\"kind\": {\"type\": \"string\", \"description\": \"shader / resourcepack / datapack\"}, \"query\": {\"type\": \"string\"}, \"source\": {\"type\": \"string\", \"description\": \"全部 / Modrinth / CurseForge\"}}, \"required\": [\"kind\", \"query\"]}}}, {\"type\": \"function\", \"function\": {\"name\": \"search_worlds\", \"description\": \"搜索地图存档（CurseForge 世界）\", \"parameters\": {\"type\": \"object\", \"properties\": {\"query\": {\"type\": \"string\"}, \"source\": {\"type\": \"string\"}}, \"required\": [\"query\"]}}}, {\"type\": \"function\", \"function\": {\"name\": \"install_world\", \"description\": \"安装地图存档到实例的 saves\", \"parameters\": {\"type\": \"object\", \"properties\": {\"name\": {\"type\": \"string\"}, \"instance\": {\"type\": \"string\"}, \"source\": {\"type\": \"string\"}, \"slug\": {\"type\": \"string\"}, \"id\": {\"type\": \"string\", \"description\": \"CurseForge 数字 id\"}}, \"required\": [\"name\"]}}}, {\"type\": \"function\", \"function\": {\"name\": \"create_instance\", \"description\": \"新建隔离实例，装整合包前建议先建\", \"parameters\": {\"type\": \"object\", \"properties\": {\"name\": {\"type\": \"string\"}}, \"required\": [\"name\"]}}}, {\"type\": \"function\", \"function\": {\"name\": \"delete_instance\", \"description\": \"删除整个实例（危险）\", \"parameters\": {\"type\": \"object\", \"properties\": {\"name\": {\"type\": \"string\"}}, \"required\": [\"name\"]}}}, {\"type\": \"function\", \"function\": {\"name\": \"delete_mod\", \"description\": \"删除模组文件\", \"parameters\": {\"type\": \"object\", \"properties\": {\"filename\": {\"type\": \"string\"}, \"instance\": {\"type\": \"string\"}}, \"required\": [\"filename\"]}}}, {\"type\": \"function\", \"function\": {\"name\": \"disable_mod\", \"description\": \"禁用模组（改名为 .disabled，可恢复）\", \"parameters\": {\"type\": \"object\", \"properties\": {\"filename\": {\"type\": \"string\"}, \"instance\": {\"type\": \"string\"}}, \"required\": [\"filename\"]}}}, {\"type\": \"function\", \"function\": {\"name\": \"enable_mod\", \"description\": \"重新启用已禁用模组\", \"parameters\": {\"type\": \"object\", \"properties\": {\"filename\": {\"type\": \"string\"}, \"instance\": {\"type\": \"string\"}}, \"required\": [\"filename\"]}}}, {\"type\": \"function\", \"function\": {\"name\": \"get_java_list\", \"description\": \"列出已安装 Java\", \"parameters\": {\"type\": \"object\", \"properties\": {}}}}, {\"type\": \"function\", \"function\": {\"name\": \"download_java\", \"description\": \"下载 Adoptium Java\", \"parameters\": {\"type\": \"object\", \"properties\": {\"major\": {\"type\": \"string\", \"description\": \"8 / 11 / 17 / 21\"}}, \"required\": [\"major\"]}}}, {\"type\": \"function\", \"function\": {\"name\": \"launch_game\", \"description\": \"启动游戏。不填则用默认实例和已装版本\", \"parameters\": {\"type\": \"object\", \"properties\": {\"instance\": {\"type\": \"string\"}, \"version\": {\"type\": \"string\"}, \"username\": {\"type\": \"string\"}, \"memory_mb\": {\"type\": \"integer\"}}}}}, {\"type\": \"function\", \"function\": {\"name\": \"diagnose_launch\", \"description\": \"分析启动失败：规则扫 latest.log 和崩溃报告\", \"parameters\": {\"type\": \"object\", \"properties\": {\"instance\": {\"type\": \"string\"}}}}}, {\"type\": \"function\", \"function\": {\"name\": \"get_latest_log\", \"description\": \"读取 latest.log 末尾\", \"parameters\": {\"type\": \"object\", \"properties\": {\"instance\": {\"type\": \"string\"}}}}}, {\"type\": \"function\", \"function\": {\"name\": \"get_crash_report\", \"description\": \"读取最新崩溃报告\", \"parameters\": {\"type\": \"object\", \"properties\": {\"instance\": {\"type\": \"string\"}}}}}, {\"type\": \"function\", \"function\": {\"name\": \"scan_mod_conflicts\", \"description\": \"扫描模组冲突、缺依赖、加载器不匹配\", \"parameters\": {\"type\": \"object\", \"properties\": {\"instance\": {\"type\": \"string\"}}}}}, {\"type\": \"function\", \"function\": {\"name\": \"inspect_mod\", \"description\": \"解析单个模组 jar 的元数据\", \"parameters\": {\"type\": \"object\", \"properties\": {\"filename\": {\"type\": \"string\"}, \"instance\": {\"type\": \"string\"}}, \"required\": [\"filename\"]}}}, {\"type\": \"function\", \"function\": {\"name\": \"list_mod_configs\", \"description\": \"列出实例 config 下的配置文件\", \"parameters\": {\"type\": \"object\", \"properties\": {\"instance\": {\"type\": \"string\"}, \"prefix\": {\"type\": \"string\", \"description\": \"子目录或文件名前缀\"}}}}}, {\"type\": \"function\", \"function\": {\"name\": \"read_mod_config\", \"description\": \"读取某个配置文件\", \"parameters\": {\"type\": \"object\", \"properties\": {\"path\": {\"type\": \"string\", \"description\": \"相对 config/ 的路径\"}, \"instance\": {\"type\": \"string\"}}, \"required\": [\"path\"]}}}, {\"type\": \"function\", \"function\": {\"name\": \"write_mod_config\", \"description\": \"写入配置文件（会先备份 .bak）\", \"parameters\": {\"type\": \"object\", \"properties\": {\"path\": {\"type\": \"string\"}, \"content\": {\"type\": \"string\", \"description\": \"完整文件内容\"}, \"instance\": {\"type\": \"string\"}}, \"required\": [\"path\", \"content\"]}}}, {\"type\": \"function\", \"function\": {\"name\": \"read_artifact\", \"description\": \"回读之前被存盘的超长工具结果。传结果摘要里给的文件名\", \"parameters\": {\"type\": \"object\", \"properties\": {\"artifact_id\": {\"type\": \"string\", \"description\": \"如 20260918-221533-a1b2c3.txt\"}, \"offset\": {\"type\": \"integer\", \"description\": \"起始行（0 起）\"}, \"limit\": {\"type\": \"integer\", \"description\": \"读取行数，默认 200\"}}, \"required\": [\"artifact_id\"]}}}, {\"type\": \"function\", \"function\": {\"name\": \"dispatch_subagent\", \"description\": \"派发一个独立的子任务给只读子代理去跑（查状态、搜资料、读日志、扫冲突这类），子代理跑完把结论带回来。多步骤的调研活用它，别把主对话搞成一长串中间步骤。\", \"parameters\": {\"type\": \"object\", \"properties\": {\"task\": {\"type\": \"string\", \"description\": \"子任务一句话说清楚要干什么\"}, \"context\": {\"type\": \"string\", \"description\": \"相关背景（实例名/报错原文等），可选\"}}, \"required\": [\"task\"]}}}, {\"type\": \"function\", \"function\": {\"name\": \"update_plan\", \"description\": \"维护本次任务的待办清单并在界面展示。三步以上的活先出计划再动手；每次更新都传完整清单，状态只能是 pending / in_progress / completed。\", \"parameters\": {\"type\": \"object\", \"properties\": {\"items\": {\"type\": \"array\", \"description\": \"完整待办列表\", \"items\": {\"type\": \"object\", \"properties\": {\"title\": {\"type\": \"string\", \"description\": \"这一步要干什么\"}, \"status\": {\"type\": \"string\", \"description\": \"pending / in_progress / completed\"}}}}}, \"required\": [\"items\"]}}}]")));

	private static List<object> WithMemoryTools(List<object> schemas)
	{
		schemas.AddRange(ExtraTools.Schemas());
		schemas.AddRange((List<object>)PyJson.Loads("[{\"type\": \"function\", \"function\": {\"name\": \"remember\", \"description\": \"把值得长期记住的事写进记忆，下次对话还能看到。玩家说出偏好（常玩版本、加载器、喜欢的光影、内存大小）、电脑配置（显卡、内存）、或你刚帮他修好一个问题（原因和解决办法）时调用。一次只记一条，写成简短的一句话，不要记一次性的闲聊。\", \"parameters\": {\"type\": \"object\", \"properties\": {\"text\": {\"type\": \"string\", \"description\": \"要记住的一句话，如：常玩 1.20.1 Fabric，喜欢 BSL 光影\"}}, \"required\": [\"text\"]}}}, {\"type\": \"function\", \"function\": {\"name\": \"forget\", \"description\": \"删除包含关键词的记忆。玩家让你忘掉某事，或发现记忆已过时、记错时调用。\", \"parameters\": {\"type\": \"object\", \"properties\": {\"keyword\": {\"type\": \"string\", \"description\": \"要删除的记忆里包含的关键词\"}}, \"required\": [\"keyword\"]}}}]"));
		return schemas;
	}

	public static string SchemaName(object s)
	{
		return Py.Str(((PyDict)((PyDict)s)["function"])["name"]);
	}

	public static bool IsWriteTool(string name)
	{
		ToolMetaInfo toolMetaInfo = ToolMeta.Get(name);
		if (toolMetaInfo != null)
		{
			return !toolMetaInfo.@readonly;
		}
		return false;
	}

	private static string _selection_text(List<object> messages, int limitPer = 4000, int lastN = 12)
	{
		List<string> list = new List<string>();
		List<object> list2 = messages ?? new List<object>();
		foreach (object item in list2.Skip(Math.Max(0, list2.Count - lastN)))
		{
			if (item is PyDict pyDict)
			{
				object a = pyDict.Get("role");
				if (Py.Eq(a, "user") || Py.Eq(a, "tool"))
				{
					list.Add(Py.Slice(Py.S(pyDict.Get("content")), null, limitPer));
				}
			}
		}
		return string.Join("\n", list);
	}

	public static List<object> SelectToolSchemas(List<object> messages, PyDict settings = null, bool forceAll = false)
	{
		bool flag = Py.Truthy((settings ?? new PyDict()).Get("ai_subagent"));
		HashSet<string> wanted;
		if (forceAll)
		{
			wanted = new HashSet<string>(TOOL_SCHEMAS.Select(SchemaName));
		}
		else
		{
			string text = _selection_text(messages);
			List<string> list = new List<string>(AiData.CORE_TOOLS);
			(string[], string[])[] kEYWORD_TOOLS = AiData.KEYWORD_TOOLS.Concat(ExtraTools.Keywords).ToArray();
			for (int i = 0; i < kEYWORD_TOOLS.Length; i++)
			{
				var (source, array) = kEYWORD_TOOLS[i];
				if (!source.Any((string k) => text.Contains(k)))
				{
					continue;
				}
				string[] array2 = array;
				foreach (string item in array2)
				{
					if (!list.Contains(item))
					{
						list.Add(item);
					}
				}
			}
			if ((text.Contains("cache/ai_results/") || text.Contains("[结果过长")) && !list.Contains("read_artifact"))
			{
				list.Add("read_artifact");
			}
			wanted = new HashSet<string>(list);
		}
		if (flag)
		{
			wanted = new HashSet<string>(wanted.Where((string n) => ToolMeta.Get(n) != null && ToolMeta.Get(n).@readonly));
			wanted.Remove("dispatch_subagent");
		}
		List<object> list2 = TOOL_SCHEMAS.Where((object s) => wanted.Contains(SchemaName(s))).ToList();
		if (list2.Count > 0)
		{
			return list2;
		}
		if (flag)
		{
			List<string> roCore = (from n in TOOL_SCHEMAS.Select(SchemaName)
				where AiData.CORE_TOOLS.Contains<string>(n) && ToolMeta.Get(n) != null && ToolMeta.Get(n).@readonly && n != "dispatch_subagent"
				select n).ToList();
			List<object> list3 = TOOL_SCHEMAS.Where((object s) => roCore.Contains(SchemaName(s))).ToList();
			if (list3.Count <= 0)
			{
				return new List<object> { TOOL_SCHEMAS[0] };
			}
			return list3;
		}
		return new List<object>(TOOL_SCHEMAS);
	}

	public static bool IsAskTool(string name)
	{
		return name == "ask_user";
	}

	public static List<object> NormalizeAskArgs(PyDict args)
	{
		args = args ?? new PyDict();
		object obj = args.Get("questions");
		if (!Py.Truthy(obj))
		{
			obj = Py.L(PyDict.Of("id", Py.Or(args.Get("id"), "q1"), "prompt", Py.Or(args.Get("prompt"), args.Get("title"), "请选择"), "allow_multiple", Py.Truthy(args.Get("allow_multiple")), "options", Py.Or(args.Get("options"), new List<object>())));
		}
		List<object> list = new List<object>();
		List<object> list2 = ((obj is List<object> list3) ? list3 : new List<object> { obj });
		for (int i = 0; i < list2.Count; i++)
		{
			if (!(list2[i] is PyDict pyDict))
			{
				continue;
			}
			List<PyDict> list4 = new List<PyDict>();
			object o = Py.Or(pyDict.Get("options"), new List<object>());
			int num = -1;
			foreach (object item in Agent.Iter(o))
			{
				num++;
				string text;
				string text2;
				if (item is string s)
				{
					text = "opt_" + num;
					text2 = Py.Strip(s);
				}
				else
				{
					if (!(item is PyDict pyDict2))
					{
						continue;
					}
					text = Py.Str(Py.Or(pyDict2.Get("id"), "opt_" + num));
					text2 = Py.Strip(Py.Str(Py.Or(pyDict2.Get("label"), pyDict2.Get("name"), text)));
				}
				if (text2.Length != 0)
				{
					list4.Add(PyDict.Of("id", text, "label", text2));
				}
			}
			if (!list4.Any((PyDict x) => (string)x["id"] == "other" || Py.RStrip((string)x["label"], "。.") == "其他" || Py.RStrip((string)x["label"], "。.") == "其它"))
			{
				list4.Add(PyDict.Of("id", "other", "label", "其他"));
			}
			if (list4.Count < 2)
			{
				list4.Insert(0, PyDict.Of("id", "skip", "label", "先不选"));
			}
			list.Add(PyDict.Of("id", Py.Str(Py.Or(pyDict.Get("id"), "q" + (i + 1))), "prompt", Py.Str(Py.Or(pyDict.Get("prompt"), pyDict.Get("title"), "请选择")), "allow_multiple", Py.Truthy(pyDict.Get("allow_multiple")), "options", list4.Cast<object>().ToList()));
		}
		if (list.Count == 0)
		{
			list.Add(PyDict.Of("id", "q1", "prompt", Py.Str(Py.Or(args.Get("prompt"), "请选择")), "allow_multiple", false, "options", Py.L(PyDict.Of("id", "skip", "label", "先不选"), PyDict.Of("id", "other", "label", "其他"))));
		}
		return list;
	}

	public static object NormalizeAskAnswer(List<object> questions, object answered)
	{
		if (answered == null || answered is string)
		{
			return answered;
		}
		if (!(answered is PyDict pyDict))
		{
			return answered;
		}
		PyDict pyDict2 = new PyDict();
		foreach (object item in questions ?? new List<object>())
		{
			if (item is PyDict pyDict3)
			{
				pyDict2[Py.S(pyDict3.Get("id"))] = pyDict3;
			}
		}
		PyDict pyDict4 = new PyDict();
		List<string> list = new List<string>();
		foreach (KeyValuePair<string, object> item2 in pyDict)
		{
			string key = item2.Key;
			object value = item2.Value;
			List<string> list2 = new List<string>();
			List<string> list3 = new List<string>();
			string text = "";
			object o = null;
			bool flag = false;
			if (value is PyDict pyDict5)
			{
				if (pyDict5.ContainsKey("picked"))
				{
					o = Py.Or(pyDict5.Get("picked"), new List<object>());
					flag = true;
				}
				else
				{
					list2 = Agent.Iter(Py.Or(pyDict5.Get("ids"), new List<object>())).Select(Py.Str).ToList();
					list3 = Agent.Iter(Py.Or(pyDict5.Get("labels"), new List<object>())).Select(Py.Str).ToList();
				}
				text = Py.Strip(Py.Str(Py.Or(pyDict5.Get("other_text"), pyDict5.Get("other"), "")));
			}
			else if (value is List<object>)
			{
				o = value;
				flag = true;
			}
			else if (value != null)
			{
				list2 = new List<string> { Py.Str(value) };
			}
			if (flag)
			{
				foreach (object item3 in Agent.Iter(o))
				{
					if (item3 is PyDict pyDict6)
					{
						string text2 = Py.S(pyDict6.Get("id"));
						list2.Add(text2);
						list3.Add(Py.Str(Py.Or(pyDict6.Get("label"), text2)));
					}
					else if (item3 != null)
					{
						list2.Add(Py.Str(item3));
					}
				}
			}
			PyDict pyDict7 = (pyDict2.Get(key) as PyDict) ?? new PyDict();
			Dictionary<string, string> optLabels = new Dictionary<string, string>();
			foreach (object item4 in Agent.Iter(Py.Or(pyDict7.Get("options"), new List<object>())))
			{
				if (item4 is PyDict pyDict8)
				{
					optLabels[Py.S(pyDict8.Get("id"))] = Py.S(pyDict8.Get("label"));
				}
			}
			if (list3.Count < list2.Count)
			{
				list3 = list2.Select((string i) => (optLabels.TryGetValue(i, out string value3) && value3.Length > 0) ? value3 : i).ToList();
			}
			if (text.Length > 0 && !list2.Contains("other"))
			{
				list2.Add("other");
				list3.Add((optLabels.TryGetValue("other", out string value2) && value2.Length > 0) ? value2 : I18nCore.Tr("其他"));
			}
			pyDict4[key] = PyDict.Of("ids", list2.Cast<object>().ToList(), "labels", list3.Cast<object>().ToList(), "other_text", text);
			string text3 = Py.Str(Py.Or(pyDict7.Get("prompt"), key));
			string text4 = string.Join(", ", list3.Where((string x) => x.Length > 0));
			if (text4.Length == 0)
			{
				text4 = "（未选）";
			}
			if (text.Length > 0)
			{
				text4 = text4 + "（补充：" + text + "）";
			}
			list.Add(text3 + ": " + text4);
		}
		return PyDict.Of("answers", pyDict4, "summary", string.Join("\n", list));
	}

	private static string _clip(object obj, string toolName = "")
	{
		string text = ((obj is string text2) ? text2 : PyJson.Dumps(obj, 2, ensureAscii: false));
		if (text.Length > 8000)
		{
			return Artifacts.Store(text, toolName);
		}
		return text;
	}

	private static bool _cjk(string s)
	{
		if (!string.IsNullOrEmpty(s))
		{
			return s.Any((char ch) => ch >= '一' && ch <= '鿿');
		}
		return false;
	}

	internal static string _inst_name(BackendApi backend, PyDict args)
	{
		string text = Py.Strip(Py.AsStr(Py.Or(args.Get("instance"), ""), "strip"));
		if (text.Length <= 0)
		{
			return Py.Str(Py.Or(Config.CONFIG.Get("default_instance", "default"), "default"));
		}
		return text;
	}

	internal static Instance _inst(BackendApi backend, PyDict args)
	{
		return backend._instance(_inst_name(backend, args));
	}

	public static List<string> AffectedPaths(BackendApi backend, string name, PyDict args)
	{
		args = args ?? new PyDict();
		Instance instance;
		string text;
		try
		{
			instance = _inst(backend, args);
			text = instance.path;
		}
		catch (Exception)
		{
			instance = null;
			text = null;
		}
		if (text == null)
		{
			return new List<string>();
		}
		string text2 = PPath.Join(text, "mods");
		switch (name)
		{
		case "write_mod_config":
		{
			string text4 = Py.LStrip(Py.S(args.Get("path")).Replace("\\", "/"), "/");
			if (text4.Length == 0 || new PPath(text4).Parts.Contains("..") || text4.Contains(":"))
			{
				return new List<string>();
			}
			List<string> list7 = new List<string>();
			list7.Add(PPath.Join(text, "config", text4));
			return list7;
		}
		case "delete_mod":
		case "enable_mod":
		case "disable_mod":
		{
			string text3 = Py.S(args.Get("filename"));
			List<string> list6 = new List<string>();
			list6.Add(PPath.Join(text2, text3));
			list6.Add(PPath.Join(text2, text3 + ".disabled"));
			return list6;
		}
		case "delete_instance":
			return new List<string> { text };
		case "create_instance":
			try
			{
				List<string> list5 = new List<string>();
				list5.Add(PPath.Join(new PPath(text).Parent.ToString(), Instances.UniqueInstanceName(Py.S(Py.Or(args.Get("name"), "游戏")))));
				return list5;
			}
			catch (Exception)
			{
				return new List<string>();
			}
		case "install_mod":
			return new List<string> { text2 };
		case "install_shader":
		{
			List<string> list4 = new List<string>();
			list4.Add(PPath.Join(text, "shaderpacks"));
			return list4;
		}
		case "install_resourcepack":
		{
			List<string> list3 = new List<string>();
			list3.Add(PPath.Join(text, "resourcepacks"));
			return list3;
		}
		case "install_datapack":
		{
			List<string> list2 = new List<string>();
			list2.Add(PPath.Join(text, "datapacks"));
			return list2;
		}
		case "install_world":
		{
			List<string> list = new List<string>();
			list.Add(PPath.Join(text, "saves"));
			return list;
		}
		case "install_modpack":
			return new List<string> { text };
		case "install_game":
			return new List<string> { instance.VersionsDir() };
		case "download_java":
			try
			{
				return new List<string> { Config.CONFIG.java_dir };
			}
			catch (Exception)
			{
				return new List<string>();
			}
		default:
			return new List<string>();
		}
	}

	private static (object, object) Mark(object r)
	{
		PyDict pyDict = (PyDict)r;
		return (pyDict.Get("source"), Py.Or(pyDict.Get("id"), pyDict.Get("slug"), pyDict.Get("name")));
	}

	private static List<object> _merge_cache(List<object> old, List<object> rows)
	{
		List<object> list = new List<object>(old ?? new List<object>());
		HashSet<string> hashSet = new HashSet<string>(list.Select((object r) => Py.Repr(Py.L(Mark(r).Item1, Mark(r).Item2))));
		foreach (object item2 in rows ?? new List<object>())
		{
			string item = Py.Repr(Py.L(Mark(item2).Item1, Mark(item2).Item2));
			if (hashSet.Add(item))
			{
				list.Add(item2);
			}
		}
		return list.Skip(Math.Max(0, list.Count - 80)).ToList();
	}

	private static List<object> _trim_hits(List<object> rows, int n = 12)
	{
		List<object> list = new List<object>();
		foreach (PyDict item in rows.Take(n))
		{
			list.Add(PyDict.Of("name", item.Get("name"), "source", item.Get("source"), "slug", item.Get("slug"), "id", item.Get("id"), "author", item.Get("author"), "downloads", item.Get("downloads"), "description", Py.Slice(Py.S(item.Get("description")), null, 160)));
		}
		return list;
	}

	private static long _count_mod_jars(Instance inst)
	{
		string path = PPath.Join(inst.path, "mods");
		long num = 0L;
		try
		{
			foreach (string item in Directory.EnumerateFileSystemEntries(PPath.Abs(path)))
			{
				string text = Path.GetFileName(item).ToLowerInvariant();
				if (text.EndsWith(".jar") || text.EndsWith(".jar.disabled") || text.EndsWith(".disabled"))
				{
					num++;
				}
			}
			return num;
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
		{
			return 0L;
		}
	}

	public static string RuntimeContext(BackendApi backend)
	{
		try
		{
			List<object> source = (List<object>)backend.get_instances();
			object o = Config.CONFIG.Get("default_instance", "default");
			object o2 = backend.get_java_list(false);
			List<string> list = new List<string>
			{
				"默认实例: " + Py.Str(o),
				"已装 Java: " + Agent.Len(o2) + " 个"
			};
			foreach (PyDict item in source.Take(8))
			{
				object obj = item.Get("name");
				long num = 0L;
				string a = null;
				object obj2 = (Py.Truthy(item.Get("mc_version")) ? item.Get("mc_version") : null);
				try
				{
					Instance instance = backend._instance(obj);
					num = _count_mod_jars(instance);
					a = Mods.DetectLoader(instance);
					if (!Py.Truthy(obj2))
					{
						obj2 = Mods.DetectMcVersion(instance);
					}
				}
				catch (Exception)
				{
				}
				list.Add("- " + Py.Str(obj) + ": MC " + Py.Str(Py.Or(obj2, "?")) + " / " + Py.Str(Py.Or(a, "原版")) + " / 版本" + Py.Str(item.Get("versions")) + "个 / 模组" + num + "个" + (Py.Truthy(item.Get("pack")) ? (" / 整合包 " + Py.Str(item.Get("pack"))) : ""));
			}
			return string.Join("\n", list);
		}
		catch (Exception e)
		{
			return "状态读取失败: " + Py.ExcStr(e);
		}
	}

	private static DownloadManager _cjk_downloader()
	{
		return _CJK_DM ?? (_CJK_DM = new DownloadManager(2L));
	}

	private static List<object> _search_mods(BackendApi backend, string query, string source)
	{
		string text = Py.Truthy(source) ? source : "全部";
        if (_cjk(query))
        {
            var hits = Py.ListOr(backend.search_mods(query, text));
            return _trim_hits(hits);
        }

		switch (text)
		{
		default:
			if (text.Length != 0)
			{
				break;
			}
			goto case "全部";
		case "全部":
		case "all":
		{
			HashSet<string> hashSet = new HashSet<string>();
			List<object> list2 = new List<object>();
			string[] array = new string[2] { "Modrinth", "CurseForge" };
			foreach (string source2 in array)
			{
				foreach (object item2 in Agent.Iter(backend.search_mods(query, source2)))
				{
					if (hashSet.Add(Py.Repr(Py.L(Mark(item2).Item1, Mark(item2).Item2))))
					{
						list2.Add(item2);
					}
				}
			}
			backend._mod_cache = _merge_cache(backend._mod_cache, list2);
			return _trim_hits(list2);
		}
		case null:
			break;
		}
		List<object> rows = Agent.Iter(backend.search_mods(query, text)).ToList();
		backend._mod_cache = _merge_cache(backend._mod_cache, rows);
		return _trim_hits(rows);
	}

	private static List<object> _search_modpacks(BackendApi backend, string query, string source)
	{
		string text = (Py.Truthy(source) ? source : "全部");
		switch (text)
		{
		default:
			if (text.Length != 0)
			{
				break;
			}
			goto case "全部";
		case "全部":
		case "all":
		{
			HashSet<string> hashSet = new HashSet<string>();
			List<object> list = new List<object>();
			string[] array = new string[2] { "Modrinth", "CurseForge" };
			foreach (string source2 in array)
			{
				foreach (object item in Agent.Iter(backend.search_modpacks(query, source2)))
				{
					if (hashSet.Add(Py.Repr(Py.L(Mark(item).Item1, Mark(item).Item2))))
					{
						list.Add(item);
					}
				}
			}
			return _trim_hits(list);
		}
		case null:
			break;
		}
		return _trim_hits(Agent.Iter(backend.search_modpacks(query, text)).ToList());
	}

	private static bool _is_offline_name(object name)
	{
		string text = Py.S(name);
		if (text.Length != 0 && !(text == "离线模式"))
		{
			return text == I18nCore.Tr("离线模式");
		}
		return true;
	}

	private static string _default_account(BackendApi backend, object accounts)
	{
		AccountManager accounts2 = backend.accounts;
		if (accounts2 != null)
		{
			PyDict pyDict;
			try
			{
				pyDict = accounts2.GetActive();
			}
			catch (Exception)
			{
				pyDict = null;
			}
			object obj = ((pyDict != null) ? pyDict.Get("name") : "");
			if (Py.Truthy(obj) && !_is_offline_name(obj))
			{
				return Py.Str(obj);
			}
		}
		List<object> list = (from a in Agent.Iter(Py.Or(accounts, new List<object>()))
			where !_is_offline_name(a)
			select a).ToList();
		if (list.Count <= 0)
		{
			return I18nCore.Tr("离线模式");
		}
		return Py.Str(list[0]);
	}

	public static string ConfirmLabel(string name, PyDict args)
	{
		args = args ?? new PyDict();
        if (Minecraft.MinecraftSession.Meta(name) != null) return Minecraft.MinecraftSession.Label(name, args);
		object value = Py.Or(args.Get("instance"), I18nCore.Tr("默认实例"));
		if (name == "ask_user")
		{
			return Py.Str(Py.Or(args.Get("prompt"), args.Get("title"), I18nCore.Tr("请选择")));
		}
		string value3;
		if (!_FMT.TryGetValue(name, out string value2))
		{
			return I18nCore.Tr((_TOOL_LABELS.TryGetValue(name, out value3) || ExtraTools.Labels.TryGetValue(name, out value3)) ? value3 : name);
		}
		string text = I18nCore.Tr(value2);
		Dictionary<string, object> fields = new Dictionary<string, object>
		{
			{ "inst", value },
			{
				"version",
				Py.Or(args.Get("version"), (name == "launch_game") ? I18nCore.Tr("当前版本") : "")
			},
			{
				"loader",
				Py.Or(args.Get("loader"), "")
			},
			{
				"name",
				Py.Or(args.Get("name"), "")
			},
			{
				"major",
				Py.Or(args.Get("major"), "")
			},
			{
				"filename",
				Py.Or(args.Get("filename"), "")
			},
			{
				"path",
				Py.Or(args.Get("path"), "")
			},
			{
				"n",
				(name == "plan_approval") ? ((object)Agent.Len(Py.Or(args.Get("items"), new List<object>()))) : ""
			}
		};
		bool bad = false;
		string s = Regex.Replace(text, "\\{\\{|\\}\\}|\\{([^{}]*)\\}", delegate(Match m)
		{
			if (m.Value == "{{")
			{
				return "{";
			}
			if (m.Value == "}}")
			{
				return "}";
			}
			string value4 = m.Groups[1].Value;
			if (!fields.TryGetValue(value4, out object value5))
			{
				bad = true;
				return "";
			}
			return Py.Str(value5);
		});
		if (bad)
		{
			return text;
		}
		return string.Join(" ", Py.SplitWs(s));
	}

	private static object Wait(BackendApi backend, object tid, bool wait, Func<bool> cancelled)
	{
		if (!wait)
		{
			return PyDict.Of("task_id", tid, "queued", true);
		}
		return backend.wait_task(tid, 1800L, cancelled);
	}

	public static object ExecuteTool(BackendApi backend, string name, PyDict args, bool wait = true, Func<bool> cancelled = null)
	{
		args = args ?? new PyDict();
		string text = _inst_name(backend, args);
		switch (name)
		{
		case "ask_user":
			return "ask_user 由界面处理";
		case "get_launcher_state":
			return RuntimeContext(backend);
		case "list_instances":
			return backend.get_instances();
		case "list_installed_versions":
			return backend.get_installed_versions(text, false);
		case "search_versions":
		{
			string text2 = Py.Lower(Py.Str(Py.Or(args.Get("query"), "")));
			string text3 = Py.Lower(Py.Str(Py.Or(args.Get("kind"), "all")));
			object o = Py.Or(backend.get_version_list(), new List<object>());
			if (!Py.Truthy(o))
			{
				o = backend.fetch_version_list();
			}
			List<object> list = new List<object>();
			foreach (PyDict item in Agent.Iter(o))
			{
				string s = Py.S(item.Get("version"));
				if ((text2.Length <= 0 || Py.Lower(s).Contains(text2)) && (!(text3 != "") || !(text3 != "all") || Py.Eq(Py.Or(item.Get("type"), ""), text3)))
				{
					list.Add(item);
					if (list.Count >= 20)
					{
						break;
					}
				}
			}
			if (list.Count <= 0)
			{
				return "没有匹配版本";
			}
			return list;
		}
		case "search_mods":
			return _search_mods(backend, Py.Str(Py.Or(args.Get("query"), "")), Py.Str(Py.Or(args.Get("source"), "全部")));
		case "search_modpacks":
		{
			List<object> list2 = _search_modpacks(backend, Py.Str(Py.Or(args.Get("query"), "")), Py.Str(Py.Or(args.Get("source"), "全部")));
			backend._pack_cache = _merge_cache(backend._pack_cache, list2);
			return list2;
		}
		case "list_mods":
			return Mods.ListInstanceModEntries(backend._instance(text));
		case "install_game":
		{
			string text5 = Py.Strip(Py.S(args.Get("loader")));
			if (Py.Lower(text5) == "无" || Py.Lower(text5) == "none" || Py.Lower(text5) == "vanilla")
			{
				text5 = "";
			}
			object tid2 = backend.install_game(args.Get("version"), text5, Py.Or(args.Get("loader_version"), ""), text);
			return Wait(backend, tid2, wait, cancelled);
		}
		case "install_mod":
		{
			PyDict extra = PyDict.Of("instance", text, "source", Py.Or(args.Get("source"), ""), "slug", Py.Or(args.Get("slug"), ""), "id", Py.Or(args.Get("id"), ""), "name", args.Get("name"));
			return Wait(backend, backend.install_mod(args.Get("name"), text, extra), wait, cancelled);
		}
		case "install_modpack":
		{
			PyDict pyDict3 = PyDict.Of("instance", text, "source", Py.Or(args.Get("source"), ""), "slug", Py.Or(args.Get("slug"), ""), "id", Py.Or(args.Get("id"), ""), "name", args.Get("name"));
			object source = Py.Or(pyDict3.Get("source"), "Modrinth");
			return Wait(backend, backend.install_modpack(args.Get("name"), source, pyDict3), wait, cancelled);
		}
		case "install_shader":
		case "install_datapack":
		case "install_resourcepack":
		{
			PyDict extra2 = PyDict.Of("instance", text, "source", Py.Or(args.Get("source"), ""), "slug", Py.Or(args.Get("slug"), ""));
			object tid = ((name == "install_shader") ? backend.install_shader(args.Get("name"), text, extra2) : ((name == "install_resourcepack") ? backend.install_resourcepack(args.Get("name"), text, extra2) : backend.install_datapack(args.Get("name"), text, extra2)));
			object obj3 = Wait(backend, tid, wait, cancelled);
			if (name != "install_datapack")
			{
				return obj3;
			}
			if (obj3 is PyDict pyDict4)
			{
				pyDict4["hint"] = "数据包在实例 datapacks 目录。进游戏后还要拷进对应存档的 datapacks 才会生效。";
				return pyDict4;
			}
			return Py.Str(obj3) + "\n数据包在实例 datapacks 目录。进游戏后还要拷进对应存档的 datapacks 才会生效。";
		}
		case "search_content":
		{
			string text6 = Py.Lower(Py.Str(Py.Or(args.Get("kind"), "shader")));
			object obj4;
			switch (text6)
			{
			default:
				obj4 = null;
				break;
			case "datapack":
				obj4 = new Func<object, object, object, object>(backend.search_datapacks);
				break;
			case "resourcepack":
				obj4 = new Func<object, object, object, object>(backend.search_resourcepacks);
				break;
			case "shader":
			case "shaderpack":
				obj4 = new Func<object, object, object, object>(backend.search_shaders);
				break;
			}
			Func<object, object, object, object> func = (Func<object, object, object, object>)obj4;
			if (func == null)
			{
				return "未知内容类型: " + text6;
			}
			object o4 = func(Py.Or(args.Get("query"), ""), Py.Or(args.Get("source"), "全部"), new PyDict());
			if (!Py.Truthy(o4))
			{
				return "没有搜到匹配内容";
			}
			return _trim_hits(Agent.Iter(o4).ToList());
		}
		case "search_worlds":
		{
			object o3 = backend.search_worlds(Py.Or(args.Get("query"), ""), Py.Or(args.Get("source"), "CurseForge"), new PyDict());
			if (!Py.Truthy(o3))
			{
				return "没有搜到匹配地图";
			}
			return _trim_hits(Agent.Iter(o3).ToList());
		}
		case "install_world":
		{
			PyDict extra3 = PyDict.Of("instance", text, "source", Py.Or(args.Get("source"), "CurseForge"), "slug", Py.Or(args.Get("slug"), ""), "id", Py.Or(args.Get("id"), ""), "name", args.Get("name"));
			return Wait(backend, backend.install_world(args.Get("name"), text, extra3), wait, cancelled);
		}
		case "create_instance":
		{
			string text4 = Instances.UniqueInstanceName(Py.Or(args.Get("name"), "游戏"));
			backend.create_instance(text4);
			return "已创建实例 " + text4;
		}
		case "delete_instance":
			backend.delete_instance(args.Get("name"));
			return "已删除实例 " + Py.Str(args.Get("name"));
		case "delete_mod":
			backend.delete_mod(text, args.Get("filename"), "");
			return "已删除 " + Py.Str(args.Get("filename"));
		case "disable_mod":
			return "已禁用 → " + Py.Str(backend.disable_mod(text, args.Get("filename"), ""));
		case "enable_mod":
			return "已启用 → " + Py.Str(backend.enable_mod(text, args.Get("filename"), ""));
		case "get_java_list":
			return backend.get_java_list(false);
		case "download_java":
			return Wait(backend, backend.download_java(Py.Str(args.Get("major")), "adoptium"), wait, cancelled);
		case "launch_game":
		{
			PyDict pyDict2 = backend._ui_launch ?? new PyDict();
			List<object> list3 = Agent.Iter(backend.get_installed_versions(text, false)).ToList();
			object obj = Py.Or(args.Get("version"), pyDict2.Get("version"), (list3.Count > 0) ? list3[list3.Count - 1] : "");
			object accounts = backend.get_accounts();
			object obj2 = Py.Or(args.Get("account"), pyDict2.Get("account"), "");
			if (!Py.Truthy(obj2))
			{
				obj2 = _default_account(backend, accounts);
			}
			object username = Py.Or(args.Get("username"), pyDict2.Get("username"), "");
			long num = Py.Int(Py.Or(args.Get("memory_mb"), pyDict2.Get("memory_mb"), Py.Or(Config.CONFIG.Get("memory_mb"), 4096L)));
			long num2 = Py.Int(Py.Or(pyDict2.Get("width"), Config.CONFIG.Get("width"), 854L));
			long num3 = Py.Int(Py.Or(pyDict2.Get("height"), Config.CONFIG.Get("height"), 480L));
			object java = Py.Or(pyDict2.Get("java"), "自动选择");
			object instance2 = Py.Or(args.Get("instance"), pyDict2.Get("instance"), text);
			object o2 = backend.launch_game(instance2, obj, obj2, username, num, num2, num3, java, null, false);
			return "已开始启动 " + Py.Str(obj) + "（任务 " + Py.Str(o2) + "），日志在「启动」页";
		}
		case "diagnose_launch":
			return Diagnose.Run(_inst(backend, args));
		case "get_latest_log":
			return Diagnose.LogExcerpt(_inst(backend, args));
		case "get_crash_report":
			return Diagnose.LogExcerpt(_inst(backend, args), "crash");
		case "scan_mod_conflicts":
			return Conflict.ScanConflicts(_inst(backend, args));
		case "inspect_mod":
		{
			Instance instance = _inst(backend, args);
			PPath pPath = new PPath(PPath.Join(instance.path, "mods")).Resolve();
			PPath pPath2 = (pPath / Py.Str(Py.Or(args.Get("filename"), ""))).Resolve();
			if (!pPath2.Parent.Equals(pPath) || !pPath2.IsFile())
			{
				return "找不到 " + Py.Str(args.Get("filename"));
			}
			return Conflict.InspectJar(pPath2.ToString());
		}
		case "list_mod_configs":
			return ModConfig.ListConfigs(_inst(backend, args), Py.Str(Py.Or(args.Get("prefix"), "")));
		case "read_mod_config":
			return ModConfig.ReadConfig(_inst(backend, args), args.Get("path"));
		case "write_mod_config":
			return ModConfig.WriteConfig(_inst(backend, args), args.Get("path"), Py.Str(Py.Or(args.Get("content"), "")));
		case "remember":
			return FileMemory.Remember(Py.Str(Py.Or(args.Get("text"), "")));
		case "forget":
			return FileMemory.Forget(Py.Str(Py.Or(args.Get("keyword"), "")));
		case "read_artifact":
			return Artifacts.ReadArtifact(args.Get("artifact_id"), Py.Or(args.Get("offset"), 0L), Py.Or(args.Get("limit"), 200L));
		default:
			return ExtraTools.Run(backend, name, args) ?? ("未知工具: " + name);
		}
	}

	public static string ClassifyException(Exception exc)
	{
		if (exc is ToolCancelled)
		{
			return "cancelled";
		}
		if (exc is TimeoutError || exc is TimeoutException)
		{
			return "timeout";
		}
		if (exc is FileNotFoundError || exc is FileNotFoundException || exc is DirectoryNotFoundException || exc is KeyError || exc is IndexError)
		{
			return "not_found";
		}
		if (exc is PermissionError || exc is UnauthorizedAccessException)
		{
			return "permission_denied";
		}
		if (exc is OSError || exc is IOException)
		{
			return "io_error";
		}
		if (exc is ValueError)
		{
			return "upstream_error";
		}
		return "unknown";
	}

	public static string ToolErrorPayload(Exception exc, string name, string code = null)
	{
		code = code ?? ClassifyException(exc);
		return PyJson.Dumps(PyDict.Of("ok", false, "error_code", code, "message", _ERROR_READABLE[code], "detail", Py.Slice(Trace.ExcType(exc) + ": " + Py.ExcStr(exc), null, 200), "tool", name), null, ensureAscii: false);
	}

	public static string BadArgsPayload(string name, string message)
	{
		return PyJson.Dumps(PyDict.Of("ok", false, "error_code", "bad_arguments", "message", message, "tool", name), null, ensureAscii: false);
	}

	public static string RunTool(BackendApi backend, string name, object rawArgs, bool wait = true, Func<bool> cancelled = null)
	{
		if (cancelled != null && cancelled())
		{
			throw new ToolCancelled();
		}
		var (args, text) = ParseArgs(rawArgs, name);
		if (text != null)
		{
			object[] extra = new object[2]
			{
				"detail",
				Py.Slice(text, null, 200)
			};
			Trace.Record("tool_bad_args", null, "", name, null, null, null, extra);
			return BadArgsPayload(name, text);
		}
		try
		{
			object obj = ExecuteTool(backend, name, args, wait, cancelled);
			if (cancelled != null && cancelled())
			{
				throw new ToolCancelled();
			}
			return _clip(obj, name);
		}
		catch (ToolCancelled)
		{
			throw;
		}
		catch (Exception exc)
		{
			Trace.Record("tool_exception", null, "", name, exc, null, null);
			return ToolErrorPayload(exc, name);
		}
	}

	private static bool TypeCheck(string want, object v, out bool known)
	{
		known = true;
		switch (want)
		{
		case "string":
			return v is string;
		case "integer":
			if (!(v is long) && !(v is int))
			{
				return v is BigInteger;
			}
			return true;
		case "number":
			if (!(v is long) && !(v is int) && !(v is double))
			{
				return v is BigInteger;
			}
			return true;
		case "boolean":
			return v is bool;
		case "array":
			return v is List<object>;
		case "object":
			return v is PyDict;
		default:
			known = false;
			return true;
		}
	}

	private static PyDict _schema_of(string name)
	{
		foreach (object tOOL_SCHEMA in TOOL_SCHEMAS)
		{
			if (SchemaName(tOOL_SCHEMA) == name)
			{
				return Py.Or(((PyDict)((PyDict)tOOL_SCHEMA)["function"]).Get("parameters"), new PyDict()) as PyDict;
			}
		}
		return null;
	}

	private static string _validate_schema(string name, PyDict args)
	{
		PyDict pyDict = _schema_of(name);
		if (pyDict == null || pyDict.Count == 0)
		{
			return null;
		}
		List<string> list = (from k in Py.ListOr(Py.Or(pyDict.Get("required"), new List<object>()))
			where !args.ContainsKey(Py.Str(k)) || args[Py.Str(k)] == null || Py.Eq(args[Py.Str(k)], "")
			select k).Select(Py.Str).ToList();
		if (list.Count > 0)
		{
			return "缺少必填字段: " + string.Join(", ", list) + "。请补齐后重新调用 " + name + "。";
		}
		PyDict obj = (Py.Or(pyDict.Get("properties"), new PyDict()) as PyDict) ?? new PyDict();
		List<string> list2 = new List<string>();
		foreach (KeyValuePair<string, object> item in obj)
		{
			if (args.ContainsKey(item.Key) && item.Value is PyDict pyDict2)
			{
				string text = Py.S(pyDict2.Get("type"));
				if (!TypeCheck(text, args[item.Key], out var known) && known)
				{
					list2.Add(item.Key + "（应为 " + text + "，实际 " + Py.TypeName(args[item.Key]) + "）");
				}
			}
		}
		if (list2.Count > 0)
		{
			return "字段类型不对: " + string.Join("; ", list2) + "。请修正后重新调用 " + name + "。";
		}
		return null;
	}

	public static (PyDict args, string err) ParseArgs(object raw, string name = "")
	{
		PyDict pyDict2;
		string text;
		if (raw is PyDict pyDict)
		{
			pyDict2 = pyDict.Copy();
			text = null;
		}
		else if (raw == null || Py.Strip(Py.Str(raw)).Length == 0)
		{
			pyDict2 = new PyDict();
			text = null;
		}
		else
		{
			string text2 = Py.Str(raw);
			object obj = null;
			bool flag = true;
			try
			{
				obj = PyJson.Loads(text2);
			}
			catch (Exception)
			{
				flag = false;
			}
			if (flag)
			{
				if (obj is PyDict pyDict3)
				{
					pyDict2 = pyDict3;
					text = null;
				}
				else
				{
					pyDict2 = new PyDict();
					text = "参数必须是 JSON 对象，收到的是 " + Py.TypeName(obj) + "。请用 {} 包裹字段重发。";
				}
			}
			else
			{
				PyDict pyDict4 = new PyDict();
				foreach (Match item in _KV_STR.Matches(text2))
				{
					if (!pyDict4.ContainsKey(item.Groups[1].Value))
					{
						pyDict4[item.Groups[1].Value] = item.Groups[2].Value;
					}
				}
				foreach (Match item2 in _KV_BARE.Matches(text2))
				{
					if (!pyDict4.ContainsKey(item2.Groups[1].Value))
					{
						pyDict4[item2.Groups[1].Value] = item2.Groups[2].Value;
					}
				}
				if (pyDict4.Count > 0)
				{
					pyDict2 = pyDict4;
					List<string> list = pyDict4.Keys.ToList();
					list.Sort(string.CompareOrdinal);
					text = "参数不是合法 JSON，已按 key=value 抢救出 " + pyDict4.Count + " 个字段（" + string.Join(", ", list) + "）。请改用标准 JSON 重新调用。";
				}
				else
				{
					pyDict2 = new PyDict();
					text = "参数不是合法 JSON（疑似被截断）。请把参数作为合法 JSON 对象重发。";
				}
			}
		}
		if (text == null && pyDict2.Count > 0)
		{
			PyDict pyDict5 = new PyDict();
			foreach (KeyValuePair<string, object> item3 in pyDict2)
			{
				pyDict5[item3.Key] = ((item3.Value is string s && !_NO_STRIP.Contains(item3.Key)) ? Py.Strip(s) : item3.Value);
			}
			pyDict2 = pyDict5;
			text = _validate_schema(name, pyDict2);
		}
		return (args: pyDict2, err: text);
	}
}
