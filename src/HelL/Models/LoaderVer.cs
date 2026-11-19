namespace HelL.Models;

public sealed class LoaderVer
{
	public string Label { get; set; } = "";

	public string Id { get; set; } = "";

	public bool Stable { get; set; }

	public override string ToString()
	{
		return Label;
	}
}
