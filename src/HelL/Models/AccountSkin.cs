using System;
using System.Text.Json.Serialization;

namespace HelL.Models;

public sealed class AccountSkin
{
	public string Name { get; set; } = "";

	[JsonPropertyName("skin_file")]
	public string SkinFile { get; set; } = "";

	[JsonPropertyName("skin_model")]
	public string SkinModel { get; set; } = "classic";

	[JsonPropertyName("data_url")]
	public string DataUrl { get; set; } = "";

	[JsonIgnore]
	public bool Slim => string.Equals(SkinModel, "slim", StringComparison.OrdinalIgnoreCase);
}
