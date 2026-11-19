namespace HelL.Models;

public sealed class JavaOption
{
	public string Label { get; set; } = "";

	public string Value { get; set; } = "";

	public override string ToString()
	{
		return Label;
	}
}
