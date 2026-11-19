using System.Text.Json.Serialization;

namespace HelL.Models;

public sealed class OpResult
{
	public bool Ok { get; set; }

	public string Message { get; set; } = "";

	[JsonPropertyName("task_id")]
	public string? TaskId { get; set; }
}
