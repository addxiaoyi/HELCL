using System.Text.Json.Serialization;

namespace HelL.Models;

public sealed class VersionRowDto
{
	public string Id { get; set; } = "";

	public string Loader { get; set; } = "";

	[JsonPropertyName("loader_color")]
	public string LoaderColor { get; set; } = "";

	public string Mc { get; set; } = "";

	public string Isolation { get; set; } = "none";

	[JsonPropertyName("isolation_label")]
	public string IsolationLabel { get; set; } = "";

	public bool Isolated { get; set; }

	public int Mods { get; set; }

	[JsonPropertyName("mods_dir")]
	public string ModsDir { get; set; } = "";

	public bool Hidden { get; set; }

	public string Java { get; set; } = "";

	[JsonPropertyName("memory_mb")]
	public int MemoryMb { get; set; }
}
