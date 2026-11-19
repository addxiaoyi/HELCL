using System.Text.Json.Serialization;

namespace HelL.Models;

public sealed class UpdateInfo
{
	[JsonPropertyName("ok")]
	public bool Ok { get; set; }

	[JsonPropertyName("has_update")]
	public bool HasUpdate { get; set; }

	public string Message { get; set; } = "";

	[JsonPropertyName("latest")]
	public string Version { get; set; } = "";

	public string Notes { get; set; } = "";

	public string Url { get; set; } = "";
}
