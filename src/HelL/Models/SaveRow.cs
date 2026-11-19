using System.Text.Json.Serialization;

namespace HelL.Models;

public sealed class SaveRow
{
	public string Name { get; set; } = "";

	public string Folder { get; set; } = "";

	public string Size { get; set; } = "";

	public long Mtime { get; set; }
    public long Bytes { get; set; }
    public string Icon { get; set; } = "";
    public string Path { get; set; } = "";

	[JsonPropertyName("last_played")]
	public string LastPlayed { get; set; } = "";

	public string Mode { get; set; } = "";

	public string Seed { get; set; } = "";
}
