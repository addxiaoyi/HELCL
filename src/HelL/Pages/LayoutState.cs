using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace HelL.Pages;

public sealed class LayoutState
{
	public DashLayout Doc { get; set; } = DashLayout.Default();

	public string Profile { get; set; } = "";

	public List<string> Profiles { get; set; } = new List<string>();

	public DashLayout Default { get; set; } = DashLayout.Default();

	public Dictionary<string, (int W, int H)> MinSizes { get; set; } = new Dictionary<string, (int, int)>();

	public static LayoutState FromJson(JsonElement e)
	{
		LayoutState layoutState = new LayoutState();
		if (e.ValueKind != JsonValueKind.Object)
		{
			return layoutState;
		}
		if (e.TryGetProperty("doc", out var value))
		{
			layoutState.Doc = DashLayout.FromJson(value);
		}
		if (e.TryGetProperty("profile", out var value2) && value2.ValueKind == JsonValueKind.String)
		{
			layoutState.Profile = value2.GetString() ?? "";
		}
		if (e.TryGetProperty("profiles", out var value3) && value3.ValueKind == JsonValueKind.Array)
		{
			layoutState.Profiles = (from s in value3.EnumerateArray().Where(delegate(JsonElement x)
				{
					JsonElement jsonElement = x;
					return jsonElement.ValueKind == JsonValueKind.String;
				}).Select(delegate(JsonElement x)
				{
					JsonElement jsonElement = x;
					return jsonElement.GetString() ?? "";
				})
				where s.Length > 0
				select s).ToList();
		}
		if (e.TryGetProperty("default", out var value4))
		{
			layoutState.Default = DashLayout.FromJson(value4);
		}
		if (e.TryGetProperty("min_sizes", out var value5) && value5.ValueKind == JsonValueKind.Object)
		{
			foreach (JsonProperty item in value5.EnumerateObject())
			{
				if (item.Value.ValueKind == JsonValueKind.Array && item.Value.GetArrayLength() >= 2 && item.Value[0].TryGetInt32(out var value6) && item.Value[1].TryGetInt32(out var value7))
				{
					layoutState.MinSizes[item.Name] = (value6, value7);
				}
			}
		}
		return layoutState;
	}
}
