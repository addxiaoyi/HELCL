using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace HelL;

public sealed class NavConfig
{
	public string? Style { get; set; }

	public List<string> Order { get; set; } = new List<string>();

	public List<string> Pinned { get; set; } = new List<string>();

	public List<string> Hidden { get; set; } = new List<string>();

	public List<NavGroup>? Groups { get; set; }

	public Dictionary<string, List<string>>? SectionMembers { get; set; }

	public int SidebarWidth { get; set; }

	public static NavConfig FromJson(JsonElement settings)
	{
		NavConfig navConfig = new NavConfig();
		if (settings.ValueKind != JsonValueKind.Object)
		{
			return navConfig;
		}
		if (settings.TryGetProperty("ui_nav_style", out var value) && value.ValueKind == JsonValueKind.String)
		{
			navConfig.Style = value.GetString();
		}
		navConfig.Order = StrList(Prop(settings, "ui_nav_order"));
		navConfig.Pinned = StrList(Prop(settings, "ui_nav_pinned"));
		navConfig.Hidden = StrList(Prop(settings, "ui_nav_hidden"));
		if (settings.TryGetProperty("ui_nav_groups", out var value2) && value2.ValueKind == JsonValueKind.Array)
		{
			navConfig.Groups = new List<NavGroup>();
			foreach (JsonElement item in value2.EnumerateArray())
			{
				if (item.ValueKind == JsonValueKind.Object)
				{
					JsonElement value3;
					string text = ((item.TryGetProperty("title", out value3) && value3.ValueKind == JsonValueKind.String) ? (value3.GetString() ?? "").Trim() : "");
					if (text.Length != 0)
					{
						navConfig.Groups.Add(new NavGroup
						{
							Title = text,
							Keys = StrList(Prop(item, "keys"))
						});
					}
				}
			}
		}
		if (settings.TryGetProperty("ui_section_members", out var value4) && value4.ValueKind == JsonValueKind.Object)
		{
			navConfig.SectionMembers = new Dictionary<string, List<string>>();
			string[] sectionIds = NavModel.SectionIds;
			foreach (string text2 in sectionIds)
			{
				navConfig.SectionMembers[text2] = StrList(Prop(value4, text2));
			}
		}
		if (settings.TryGetProperty("ui_sidebar_width", out var value5))
		{
			int result;
			if (value5.ValueKind == JsonValueKind.Number && value5.TryGetInt32(out var value6))
			{
				navConfig.SidebarWidth = value6;
			}
			else if (value5.ValueKind == JsonValueKind.String && int.TryParse(value5.GetString(), out result))
			{
				navConfig.SidebarWidth = result;
			}
		}
		return navConfig;
	}

	private static JsonElement? Prop(JsonElement e, string name)
	{
		if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var value))
		{
			return null;
		}
		return value;
	}

	public static List<string> StrList(JsonElement? raw)
	{
		List<string> list = new List<string>();
		if (raw.HasValue)
		{
			JsonElement valueOrDefault = raw.GetValueOrDefault();
			if (valueOrDefault.ValueKind == JsonValueKind.Array)
			{
				foreach (JsonElement item in valueOrDefault.EnumerateArray())
				{
					if (item.ValueKind == JsonValueKind.String)
					{
						string text = item.GetString();
						if (text != null && text.Length > 0)
						{
							list.Add(text);
						}
					}
				}
				return list;
			}
		}
		return list;
	}

	public NavConfig Clone()
	{
		return new NavConfig
		{
			Style = Style,
			Order = new List<string>(Order),
			Pinned = new List<string>(Pinned),
			Hidden = new List<string>(Hidden),
			Groups = Groups?.Select((NavGroup g) => g.Clone()).ToList(),
			SectionMembers = SectionMembers?.ToDictionary<KeyValuePair<string, List<string>>, string, List<string>>((KeyValuePair<string, List<string>> kv) => kv.Key, (KeyValuePair<string, List<string>> kv) => new List<string>(kv.Value)),
			SidebarWidth = SidebarWidth
		};
	}

	public NavConfig With(IReadOnlyDictionary<string, object?> patch)
	{
		NavConfig navConfig = Clone();
		foreach (var (text2, obj2) in patch)
		{
			switch (text2)
			{
			case "ui_nav_style":
				navConfig.Style = obj2 as string;
				break;
			case "ui_nav_order":
				navConfig.Order = AsList(obj2);
				break;
			case "ui_nav_pinned":
				navConfig.Pinned = AsList(obj2);
				break;
			case "ui_nav_hidden":
				navConfig.Hidden = AsList(obj2);
				break;
			case "ui_nav_groups":
				navConfig.Groups = ((obj2 is IEnumerable<NavGroup> source2) ? source2.Select((NavGroup g) => g.Clone()).ToList() : null);
				break;
			case "ui_section_members":
				navConfig.SectionMembers = ((obj2 is IReadOnlyDictionary<string, List<string>> source) ? source.ToDictionary<KeyValuePair<string, List<string>>, string, List<string>>((KeyValuePair<string, List<string>> kv) => kv.Key, (KeyValuePair<string, List<string>> kv) => new List<string>(kv.Value)) : null);
				break;
			case "ui_sidebar_width":
				navConfig.SidebarWidth = ((obj2 is int num) ? num : 0);
				break;
			}
		}
		return navConfig;
	}

	private static List<string> AsList(object? value)
	{
		if (!(value is IEnumerable<string> source))
		{
			return new List<string>();
		}
		return source.Where((string s) => !string.IsNullOrEmpty(s)).ToList();
	}
}
