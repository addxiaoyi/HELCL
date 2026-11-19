using System.Text.Json.Serialization;

namespace HelL.Models;

public sealed class ThemeRow
{
	public string Name { get; set; } = "";

	public string File { get; set; } = "";

	[JsonPropertyName("theme_color")]
	public string ThemeColor { get; set; } = "";

	[JsonPropertyName("ui_dark")]
	public bool UiDark { get; set; }
}
