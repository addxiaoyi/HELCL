using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HelL.Models;

public sealed class AiPlanDto
{
	[JsonPropertyName("turn_id")]
	public string TurnId { get; set; } = "";

	public List<AiPlanItemDto> Items { get; set; } = new List<AiPlanItemDto>();
}
