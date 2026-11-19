using System.Text.Json.Serialization;

namespace HelL.Models;

public sealed class AiMsgDto
{
	public string Role { get; set; } = "";

	public string Content { get; set; } = "";

	public string Note { get; set; } = "";

	[JsonPropertyName("id")]
	public string Id { get; set; } = "";
}
