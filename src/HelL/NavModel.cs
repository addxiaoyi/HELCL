using System;
using System.Collections.Generic;
using System.Linq;
using HelL.Services;

namespace HelL;

public static class NavModel
{
	public sealed record PinResult(Dictionary<string, object?>? Patch, string? Error);

	public const string StyleCompact = "compact";

	public const string StyleGrouped = "grouped";

	public const string DefaultStyle = "grouped";

	public static readonly IReadOnlyDictionary<string, string> StyleLabels = new Dictionary<string, string>
	{
		["compact"] = I18n.L("精简（启动 / 游戏 / 版本管理）"),
		["grouped"] = I18n.L("桌面（游戏入口 / 账户与设置）")
	};

	public static readonly string[] SectionIds = new string[2] { "download", "more" };

	public static readonly IReadOnlyDictionary<string, string[]> SubDefaultMembers = new Dictionary<string, string[]>
	{
		["download"] = new string[8] { "version", "mod", "modpack", "datapack", "resource", "shader", "world", "java" },
		["more"] = new string[9] { "instance", "mods", "account", "multiplayer", "servers", "playtime", "plugins", "feedback", "settings" }
	};

	public static readonly HashSet<string> AllSubKeys = new HashSet<string>(SectionIds.SelectMany((string sec) => SubDefaultMembers[sec]));

	public static readonly string[] TopKeys = new string[5] { "launch", "download", "ai", "more", "tasks" };

	public static readonly IReadOnlyDictionary<string, string> SubTitles = new Dictionary<string, string>
	{
		["version"] = I18n.L("原版游戏"),
		["mod"] = I18n.L("模组下载"),
		["modpack"] = I18n.L("整合包"),
		["datapack"] = I18n.L("数据包"),
		["resource"] = I18n.L("资源包"),
		["shader"] = I18n.L("光影包"),
		["world"] = I18n.L("世界"),
		["java"] = I18n.L("Java"),
		["instance"] = I18n.L("我的实例"),
		["mods"] = I18n.L("模组管理"),
		["account"] = I18n.L("账号"),
		["multiplayer"] = I18n.L("联机"),
		["servers"] = I18n.L("服务器"),
		["playtime"] = I18n.L("时长"),
		["feedback"] = I18n.L("反馈"),
		["settings"] = I18n.L("设置"),
		["plugins"] = "插件市场"
	};

	public static readonly IReadOnlyDictionary<string, string> NavSpecs = new Dictionary<string, string>
	{
		["launch"] = I18n.L("启动"),
		["download"] = I18n.L("游戏"),
		["ai"] = I18n.L("AI 助手"),
		["more"] = I18n.L("更多"),
		["tasks"] = I18n.L("下载任务")
	};

	public const string NavDefaultsVersion = "2026.09-ai-visible";

	public static readonly string[] DefaultNavOrder = new string[7] { "launch", "download", "instance", "ai", "more", "settings", "tasks" };

	public static readonly string[] DefaultNavPinned = new string[2] { "instance", "settings" };

	public static readonly string[] DefaultNavHidden = Array.Empty<string>();

	private static readonly Dictionary<string, string[]>[] LegacyNavFactories = new Dictionary<string, string[]>[1]
	{
		new Dictionary<string, string[]>
		{
			["ui_nav_order"] = new string[6] { "launch", "download", "instance", "more", "settings", "tasks" },
			["ui_nav_pinned"] = new string[2] { "instance", "settings" },
			["ui_nav_hidden"] = new string[1] { "ai" }
		}
	};

	public static readonly string[] BottomKeys = new string[4] { "ai", "more", "settings", "tasks" };

	public static readonly (string Title, string[] Keys)[] GroupedNav = new(string, string[])[2]
	{
		(I18n.L("游戏"), new[] { "launch", "instance", "download", "multiplayer", "ai", "more" }),
		(I18n.L("应用"), new[] { "account", "tasks", "settings" })
	};

	public static readonly IReadOnlyDictionary<string, string> GroupedLabels = new Dictionary<string, string>
	{
		["download"] = I18n.L("下载资源"),
		["multiplayer"] = I18n.L("多人联机")
	};

	public static readonly IReadOnlyDictionary<string, string> PageForNavKey = new Dictionary<string, string>
	{
		["launch"] = "launch",
		["ai"] = "ai",
		["tasks"] = "tasks",
		["version"] = "version",
		["mod"] = "mod",
		["modpack"] = "modpack",
		["datapack"] = "datapack",
		["resource"] = "resourcepack",
		["shader"] = "shader",
		["world"] = "world",
		["java"] = "java",
		["instance"] = "instance",
		["mods"] = "mods",
		["account"] = "account",
		["multiplayer"] = "multiplayer",
		["servers"] = "servers",
		["playtime"] = "playtime",
		["feedback"] = "feedback",
		["settings"] = "settings",
		["plugins"] = "plugins"
	};

	public static bool IsTop(string key)
	{
		return Array.IndexOf<string>(TopKeys, key) >= 0;
	}

	public static bool IsSection(string key)
	{
		return Array.IndexOf<string>(SectionIds, key) >= 0;
	}

	public static string NavStyle(NavConfig? cfg)
	{
		string text = cfg?.Style ?? "";
		if (!StyleLabels.ContainsKey(text))
		{
			return "grouped";
		}
		return text;
	}

	public static string NavLabel(string key)
	{
		if (!NavSpecs.TryGetValue(key, out string value))
		{
			if (!SubTitles.TryGetValue(key, out string value2))
			{
				return key;
			}
			return value2;
		}
		return value;
	}

	public static Dictionary<string, List<string>> SectionMembersFromConfig(NavConfig? cfg)
	{
		HashSet<string> hashSet = new HashSet<string>(PinnedFromConfig(cfg));
		Dictionary<string, List<string>> dictionary = SectionIds.ToDictionary((string s) => s, (string _) => new List<string>());
		HashSet<string> hashSet2 = new HashSet<string>();
		string[] sectionIds = SectionIds;
		foreach (string key in sectionIds)
		{
			List<string> value;
			foreach (string item2 in ((cfg?.SectionMembers != null && cfg.SectionMembers.TryGetValue(key, out value)) ? value : null) ?? new List<string>())
			{
				if (AllSubKeys.Contains(item2) && !hashSet2.Contains(item2) && !hashSet.Contains(item2))
				{
					hashSet2.Add(item2);
					dictionary[key].Add(item2);
				}
			}
		}
		sectionIds = SectionIds;
		foreach (string key2 in sectionIds)
		{
			string[] array = SubDefaultMembers[key2];
			foreach (string item in array)
			{
				if (!hashSet2.Contains(item) && !hashSet.Contains(item))
				{
					hashSet2.Add(item);
					dictionary[key2].Add(item);
				}
			}
		}
		return dictionary;
	}

	public static string DefaultSectionFor(string key)
	{
		string[] sectionIds = SectionIds;
		foreach (string text in sectionIds)
		{
			if (SubDefaultMembers[text].Contains<string>(key))
			{
				return text;
			}
		}
		return "more";
	}

	public static List<NavGroup> GroupedLayout(NavConfig? cfg)
	{
		if (LegacyGroups(cfg?.Groups)) cfg = null;
		List<NavGroup> list = new List<NavGroup>();
		HashSet<string> seen = new HashSet<string>();
		if (cfg?.Groups != null)
		{
			foreach (NavGroup group in cfg.Groups)
			{
				string text = (group.Title ?? "").Trim();
				if (text.Length == 0)
				{
					continue;
				}
				List<string> list2 = new List<string>();
				foreach (string key in group.Keys)
				{
					if (!string.IsNullOrEmpty(key) && (IsTop(key) || AllSubKeys.Contains(key)) && seen.Add(key))
					{
						list2.Add(key);
					}
				}
				list.Add(new NavGroup
				{
					Title = text,
					Keys = list2
				});
			}
		}
		if (!list.Any((NavGroup g) => g.Keys.Count > 0))
		{
			return GroupedNav.Select(((string Title, string[] Keys) g) => new NavGroup
			{
				Title = g.Title,
				Keys = new List<string>(g.Keys)
			}).ToList();
		}
		List<string> list3 = TopKeys.Where((string k) => !seen.Contains(k)).ToList();
		if (list3.Count > 0)
		{
			list[list.Count - 1].Keys.AddRange(list3);
		}
		return list;
	}

	public static Dictionary<string, object?> GroupedLayoutPatch(List<NavGroup> groups)
	{
		return new Dictionary<string, object> { ["ui_nav_groups"] = groups.Select((NavGroup g) => g.Clone()).ToList() };
	}

	public static bool MoveWithinGroups(List<NavGroup> groups, string key, string target, bool before)
	{
		if (key == target || !groups.Any((NavGroup g) => g.Keys.Contains(target)))
		{
			return false;
		}
		foreach (NavGroup group in groups)
		{
			group.Keys.Remove(key);
		}
		foreach (NavGroup group2 in groups)
		{
			int num = group2.Keys.IndexOf(target);
			if (num >= 0)
			{
				group2.Keys.Insert(num + ((!before) ? 1 : 0), key);
				return true;
			}
		}
		return false;
	}

	public static List<string> PinnedFromConfig(NavConfig? cfg)
	{
		HashSet<string> hidden = new HashSet<string>(cfg?.Hidden ?? new List<string>());
		if (NavStyle(cfg) == "grouped")
		{
			return (from k in GroupedLayout(cfg).SelectMany((NavGroup g) => g.Keys)
				where AllSubKeys.Contains(k) && !hidden.Contains(k)
				select k).ToList();
		}
		List<string> list = new List<string>();
		HashSet<string> hashSet = new HashSet<string>();
		foreach (string item in cfg?.Pinned ?? new List<string>())
		{
			if (AllSubKeys.Contains(item) && hashSet.Add(item))
			{
				list.Add(item);
			}
		}
		return list;
	}

	private static NavEntry ItemEntry(string key, string label, bool draggable)
	{
		return new NavEntry(NavEntryKind.Item, key, label, IsTop(key), draggable);
	}

	public static List<NavEntry> GroupedNavItems(NavConfig? cfg)
	{
		HashSet<string> hidden = new HashSet<string>(cfg?.Hidden ?? new List<string>());
		List<NavEntry> list = new List<NavEntry>();
        var groups = GroupedLayout(cfg);
        bool standard = groups.Count == GroupedNav.Length && groups.Select((group, index) => group.Title == GroupedNav[index].Title && group.Keys.SequenceEqual(GroupedNav[index].Keys)).All(match => match);
		foreach (NavGroup item in groups)
		{
			List<string> list2 = item.Keys.Where((string k) => !hidden.Contains(k)).ToList();
			if (list2.Count == 0)
			{
				continue;
			}
			if (!standard) list.Add(NavEntry.Header(item.Title));
            else if (list.Count > 0) list.Add(NavEntry.Stretch);
			foreach (string item2 in list2)
			{
				list.Add(ItemEntry(item2, GroupedLabels.TryGetValue(item2, out string value) ? value : NavLabel(item2), draggable: false));
			}
		}
		return list;
	}

    private static bool LegacyGroups(List<NavGroup>? groups)
    {
        if (groups?.Count != 3) return false;
        string[][] keys = { new[] { "account" }, new[] { "launch", "instance", "download" }, new[] { "settings", "multiplayer", "ai", "more", "tasks" } };
        string[] titles = { I18n.L("账户"), I18n.L("游戏"), I18n.L("通用") };
        return groups.Select((group, index) => group.Title == titles[index] && group.Keys.SequenceEqual(keys[index])).All(match => match);
    }

	public static List<NavEntry> NavItemsFromConfig(NavConfig? cfg)
	{
		if (NavStyle(cfg) == "grouped")
		{
			return GroupedNavItems(cfg);
		}
		List<string> obj = cfg?.Order ?? new List<string>();
		List<string> list = PinnedFromConfig(cfg);
		HashSet<string> hidden = new HashSet<string>(cfg?.Hidden ?? new List<string>());
		List<string> order = new List<string>();
		foreach (string item2 in obj)
		{
			if (IsTop(item2) && !order.Contains(item2))
			{
				order.Add(item2);
			}
			else if (list.Contains(item2) && !order.Contains(item2))
			{
				order.Add(item2);
			}
		}
		string[] topKeys = TopKeys;
		foreach (string item in topKeys)
		{
			if (!order.Contains(item))
			{
				order.Add(item);
			}
		}
		List<string> list2 = list.Where((string k) => !order.Contains(k)).ToList();
		if (list2.Count > 0)
		{
			string text = new string[2] { "more", "tasks" }.FirstOrDefault((string a) => order.Contains(a));
			if (text != null)
			{
				order.InsertRange(order.IndexOf(text), list2);
			}
			else
			{
				order.AddRange(list2);
			}
		}
		List<string> list3 = order.Where((string k) => !IsTop(k) || !hidden.Contains(k)).ToList();
		int num = list3.FindIndex((string k) => BottomKeys.Contains<string>(k));
		if (num <= 0)
		{
			num = -1;
		}
		List<NavEntry> list4 = new List<NavEntry>();
		for (int num2 = 0; num2 < list3.Count; num2++)
		{
			if (num2 == num)
			{
				list4.Add(NavEntry.Stretch);
			}
			list4.Add(ItemEntry(list3[num2], NavLabel(list3[num2]), draggable: true));
		}
		return list4;
	}

	private static Dictionary<string, string[]> NavFactory()
	{
		return new Dictionary<string, string[]>
		{
			["ui_nav_order"] = DefaultNavOrder,
			["ui_nav_pinned"] = DefaultNavPinned,
			["ui_nav_hidden"] = DefaultNavHidden
		};
	}

	public static bool NavUntouched(NavConfig? cfg)
	{
		Dictionary<string, List<string>> stored = new Dictionary<string, List<string>>
		{
			["ui_nav_order"] = cfg?.Order ?? new List<string>(),
			["ui_nav_pinned"] = cfg?.Pinned ?? new List<string>(),
			["ui_nav_hidden"] = cfg?.Hidden ?? new List<string>()
		};
		return new Dictionary<string, string[]>[1] { NavFactory() }.Concat<Dictionary<string, string[]>>(LegacyNavFactories).Any((Dictionary<string, string[]> factory) => factory.All(delegate(KeyValuePair<string, string[]> kv)
		{
			List<string> list = stored[kv.Key];
			return list.Count == 0 || list.SequenceEqual<string>(kv.Value);
		}));
	}

	public static Dictionary<string, object?> DefaultNavPatch()
	{
		return new Dictionary<string, object>
		{
			["ui_nav_order"] = DefaultNavOrder.ToList(),
			["ui_nav_pinned"] = DefaultNavPinned.ToList(),
			["ui_nav_hidden"] = DefaultNavHidden.ToList(),
			["ui_nav_style"] = "grouped",
			["ui_nav_defaults"] = "2026.09-ai-visible",
			["ui_nav_groups"] = null,
			["ui_section_members"] = null
		};
	}

	public static List<string> VisibleSections(NavConfig? cfg)
	{
		HashSet<string> hashSet = new HashSet<string>(from it in NavItemsFromConfig(cfg)
			where it.Kind == NavEntryKind.Item
			select it.Key);
		return SectionIds.Where(hashSet.Contains).ToList();
	}

	public static (Dictionary<string, object?> Patch, string Section)? UnpinNavConfig(NavConfig? cfg, string key, string? backSection = null, int index = -1)
	{
		List<string> list = PinnedFromConfig(cfg);
		if (!list.Contains(key))
		{
			return null;
		}
		List<string> value = list.Where((string k) => k != key).ToList();
		Dictionary<string, object> dictionary = new Dictionary<string, object>();
		NavConfig navConfig = cfg ?? new NavConfig();
		NavConfig cfg2;
		if (NavStyle(cfg) == "grouped")
		{
			List<NavGroup> list2 = GroupedLayout(cfg);
			foreach (NavGroup item in list2)
			{
				item.Keys.Remove(key);
			}
			foreach (KeyValuePair<string, object> item2 in GroupedLayoutPatch(list2))
			{
				item2.Deconstruct(out var key2, out var value2);
				string key3 = key2;
				object value3 = value2;
				dictionary[key3] = value3;
			}
			cfg2 = navConfig.With(dictionary);
		}
		else
		{
			dictionary["ui_nav_pinned"] = value;
			cfg2 = navConfig.With(new Dictionary<string, object> { ["ui_nav_pinned"] = value });
		}
		Dictionary<string, List<string>> members = SectionMembersFromConfig(cfg2);
		string dest = ((backSection != null && IsSection(backSection)) ? backSection : (SectionIds.FirstOrDefault((string sec) => members[sec].Contains(key)) ?? DefaultSectionFor(key)));
		int num = index;
		List<string> list3 = VisibleSections(cfg2);
		if (!list3.Contains(dest))
		{
			if (list3.Count > 0)
			{
				dest = list3[0];
				num = -1;
			}
			else
			{
				dictionary["ui_nav_hidden"] = (cfg?.Hidden ?? new List<string>()).Where((string k) => k != dest).ToList();
			}
		}
		Dictionary<string, List<string>> dictionary2 = SectionIds.ToDictionary((string sec) => sec, (string sec) => members[sec].Where((string k) => k != key).ToList());
		List<string> list4 = dictionary2[dest];
		list4.Insert((num >= 0 && num <= list4.Count) ? num : list4.Count, key);
		dictionary["ui_section_members"] = dictionary2;
		return (dictionary, dest);
	}

	public static List<string> SidebarSequence(NavConfig? cfg)
	{
		return (from it in NavItemsFromConfig(cfg)
			where it.Kind == NavEntryKind.Item
			select it.Key).ToList();
	}

	public static Dictionary<string, object?> WriteSidebarSequence(IEnumerable<string> seq)
	{
		HashSet<string> seen = new HashSet<string>();
		List<string> list = seq.Where((string k) => (IsTop(k) || AllSubKeys.Contains(k)) && seen.Add(k)).ToList();
		string[] topKeys = TopKeys;
		foreach (string item in topKeys)
		{
			if (!list.Contains(item))
			{
				list.Add(item);
			}
		}
		return new Dictionary<string, object>
		{
			["ui_nav_order"] = list,
			["ui_nav_pinned"] = list.Where(AllSubKeys.Contains).ToList()
		};
	}

	public static Dictionary<string, object?>? ReorderNavConfig(NavConfig? cfg, string key, string target, bool before)
	{
		if (NavStyle(cfg) == "grouped")
		{
			List<NavGroup> groups = GroupedLayout(cfg);
			if (!MoveWithinGroups(groups, key, target, before))
			{
				return null;
			}
			return GroupedLayoutPatch(groups);
		}
		List<string> list = SidebarSequence(cfg);
		if (key == target || !list.Contains(key) || !list.Contains(target))
		{
			return null;
		}
		list.Remove(key);
		list.Insert(list.IndexOf(target) + ((!before) ? 1 : 0), key);
		return WriteSidebarSequence(list);
	}

	public static PinResult? PinNavConfig(NavConfig? cfg, string key, string? target = null, bool before = true)
	{
		if (!AllSubKeys.Contains(key))
		{
			return null;
		}
		if (PinnedFromConfig(cfg).Contains(key))
		{
			return null;
		}
		Dictionary<string, List<string>> members = SectionMembersFromConfig(cfg);
		string text = SectionIds.FirstOrDefault((string sec) => members[sec].Contains(key));
		if (text != null && members[text].Count <= 1)
		{
			return new PinResult(null, I18n.L("该分区只剩这一个子页，移走会变空栏；先在「自定义分区」里补充其它子页"));
		}
		Dictionary<string, object> dictionary = new Dictionary<string, object>();
		string key2;
		object value;
		if (NavStyle(cfg) == "grouped")
		{
			List<NavGroup> list = GroupedLayout(cfg);
			string text2 = ((target != null && list.Any((NavGroup g) => g.Keys.Contains(target))) ? target : "more");
			if (!MoveWithinGroups(list, key, text2, !(text2 == target) || before))
			{
				list[list.Count - 1].Keys.Add(key);
			}
			foreach (KeyValuePair<string, object> item in GroupedLayoutPatch(list))
			{
				item.Deconstruct(out key2, out value);
				string key3 = key2;
				object value2 = value;
				dictionary[key3] = value2;
			}
		}
		else
		{
			List<string> list2 = SidebarSequence(cfg);
			string text3 = ((target != null && list2.Contains(target)) ? target : (list2.Contains("more") ? "more" : null));
			if (text3 != null)
			{
				list2.Insert(list2.IndexOf(text3) + ((text3 == target && !before) ? 1 : 0), key);
			}
			else
			{
				list2.Add(key);
			}
			foreach (KeyValuePair<string, object> item2 in WriteSidebarSequence(list2))
			{
				item2.Deconstruct(out key2, out value);
				string key4 = key2;
				object value3 = value;
				dictionary[key4] = value3;
			}
		}
		if (text != null)
		{
			dictionary["ui_section_members"] = SectionIds.ToDictionary((string sec) => sec, (string sec) => members[sec].Where((string k) => k != key).ToList());
		}
		return new PinResult(dictionary, null);
	}

	public static List<string> MergeNavOrder(NavConfig? cfg, IEnumerable<string> topOrder, IReadOnlyCollection<string> pinned)
	{
		List<string> list = SidebarSequence(cfg);
		Queue<string> queue = new Queue<string>(topOrder);
		List<string> list2 = new List<string>();
		foreach (string item in list)
		{
			if (AllSubKeys.Contains(item))
			{
				if (pinned.Contains<string>(item))
				{
					list2.Add(item);
				}
			}
			else if (queue.Count > 0)
			{
				list2.Add(queue.Dequeue());
			}
		}
		list2.AddRange(queue);
		foreach (string item2 in pinned)
		{
			if (!list2.Contains(item2))
			{
				list2.Add(item2);
			}
		}
		return list2;
	}

	public static Dictionary<string, object?> ToRpcArgs(IReadOnlyDictionary<string, object?> patch)
	{
		Dictionary<string, object> dictionary = new Dictionary<string, object>();
		foreach (KeyValuePair<string, object> item in patch)
		{
			item.Deconstruct(out var key, out var value);
			string text = key;
			object obj = value;
			key = text;
			object obj2 = (dictionary[key] = ((!(obj is IEnumerable<NavGroup> source)) ? obj : source.Select((NavGroup g) => new Dictionary<string, object>
			{
				["title"] = g.Title,
				["keys"] = new List<string>(g.Keys)
			}).ToList()));
			value = obj2;
		}
		return dictionary;
	}
}
