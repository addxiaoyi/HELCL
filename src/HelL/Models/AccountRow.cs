using System.Text.Json.Serialization;

namespace HelL.Models;

public sealed class AccountRow
{
	public string Name { get; set; } = "";

	public string Type { get; set; } = "";

	public string Uuid { get; set; } = "";

	public string Api { get; set; } = "";

	public string Avatar { get; set; } = "";

	public string Body { get; set; } = "";

	public bool Active { get; set; }

	[JsonPropertyName("skin_file")]
	public string SkinFile { get; set; } = "";

	[JsonPropertyName("skin_model")]
	public string SkinModel { get; set; } = "classic";
}
