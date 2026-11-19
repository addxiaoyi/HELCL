using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HelL.Models;

public sealed class ModUpdateRow
{
	public string Filename { get; set; } = "";

	public string Name { get; set; } = "";

	public string Current { get; set; } = "";

	public string Latest { get; set; } = "";

	public string Project { get; set; } = "";

	public string Url { get; set; } = "";

	public string Sha1 { get; set; } = "";

	public long Size { get; set; }

	[JsonPropertyName("filename_new")]
	public string FilenameNew { get; set; } = "";

	public string Source { get; set; } = "";

	[JsonPropertyName("mc_version")]
	public string McVersion { get; set; } = "";

	[JsonPropertyName("game_versions")]
	public List<string> GameVersions { get; set; } = new List<string>();

	[JsonExtensionData]
	public Dictionary<string, JsonElement>? Extra { get; set; }
}
