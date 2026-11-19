using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace HelL;

public static class NavDump
{
	public static int Run()
	{
		UTF8Encoding encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
		using StreamReader input = new StreamReader(Console.OpenStandardInput(), encoding);
		using StreamWriter output = new StreamWriter(Console.OpenStandardOutput(), encoding);
		return Run(input, output);
	}

	public static int Run(TextReader input, TextWriter output)
	{
		try
		{
			string text = input.ReadToEnd();
			using JsonDocument jsonDocument = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "[]" : text);
			List<Dictionary<string, object>> list = new List<Dictionary<string, object>>();
			foreach (JsonElement item in jsonDocument.RootElement.EnumerateArray())
			{
				list.Add(Describe(item));
			}
			output.Write(JsonSerializer.Serialize(list, new JsonSerializerOptions
			{
				Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
			}));
			output.Flush();
			return 0;
		}
		catch (Exception value)
		{
			Console.Error.WriteLine(value);
			return 1;
		}
	}

	private static Dictionary<string, object?> Describe(JsonElement input)
	{
		JsonElement value;
		NavConfig cfg = NavConfig.FromJson(input.TryGetProperty("config", out value) ? value : default(JsonElement));
		List<List<string>> value2 = (from entry in NavModel.NavItemsFromConfig(cfg)
			select entry.Kind switch
			{
				NavEntryKind.Item => new List<string> { "item", entry.Key, entry.Label }, 
				NavEntryKind.Header => new List<string> { "header", entry.Label }, 
				_ => new List<string> { "stretch" }, 
			}).ToList();
		Dictionary<string, object> dictionary = new Dictionary<string, object>
		{
			["style"] = NavModel.NavStyle(cfg),
			["items"] = value2,
			["pinned"] = NavModel.PinnedFromConfig(cfg),
			["members"] = NavModel.SectionMembersFromConfig(cfg),
			["sequence"] = NavModel.SidebarSequence(cfg)
		};
		if (input.TryGetProperty("unpin", out var value3) && value3.ValueKind == JsonValueKind.Object)
		{
			JsonElement value4;
			string key = (value3.TryGetProperty("key", out value4) ? (value4.GetString() ?? "") : "");
			JsonElement value5;
			string backSection = ((value3.TryGetProperty("section", out value5) && value5.ValueKind == JsonValueKind.String) ? value5.GetString() : null);
			JsonElement value6;
			int index = ((value3.TryGetProperty("index", out value6) && value6.ValueKind == JsonValueKind.Number) ? value6.GetInt32() : (-1));
			(Dictionary<string, object>, string)? tuple = NavModel.UnpinNavConfig(cfg, key, backSection, index);
			dictionary["unpin"] = ((!tuple.HasValue) ? null : new Dictionary<string, object>
			{
				["section"] = tuple.Value.Item2,
				["patch"] = NavModel.ToRpcArgs(tuple.Value.Item1)
			});
		}
		return dictionary;
	}
}
