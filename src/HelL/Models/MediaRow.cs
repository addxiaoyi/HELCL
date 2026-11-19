namespace HelL.Models;

public sealed class MediaRow
{
	public string Name { get; set; } = "";

	public string Path { get; set; } = "";

	public long Bytes { get; set; }

	public long Mtime { get; set; }
}
