namespace HelL.Models;

public sealed class BackupRow
{
	public string Name { get; set; } = "";

	public string Path { get; set; } = "";

	public string Size { get; set; } = "";

	public string Date { get; set; } = "";

	public long Bytes { get; set; }

	public long Mtime { get; set; }
}
