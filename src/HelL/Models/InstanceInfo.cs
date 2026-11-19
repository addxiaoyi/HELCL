using System.Text.Json.Serialization;

namespace HelL.Models;

public sealed class InstanceInfo
{
	public string Name { get; set; } = "";
    public string Group { get; set; } = "";

	public int Versions { get; set; }

	public string Mc { get; set; } = "";

	public string Pack { get; set; } = "";

	[JsonPropertyName("pack_version")]
	public string PackVersion { get; set; } = "";

	[JsonPropertyName("mc_version")]
	public string McVersion { get; set; } = "";

	public string Java { get; set; } = "";

	[JsonPropertyName("java_label")]
	public string JavaLabel { get; set; } = "";
}
