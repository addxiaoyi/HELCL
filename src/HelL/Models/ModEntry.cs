namespace HelL.Models;

public sealed class ModEntry
{
	public string Filename { get; set; } = "";

	public string Name { get; set; } = "";

	public bool Enabled { get; set; } = true;

	public string Version { get; set; } = "";

	public string Size { get; set; } = "";
}
