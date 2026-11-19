using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HelL.Models;

public sealed class AiStoreDto
{
	[JsonPropertyName("active_id")]
	public string ActiveId { get; set; } = "";

	public List<AiChatDto> Chats { get; set; } = new List<AiChatDto>();

	public bool Busy { get; set; }

	[JsonPropertyName("pending_card")]
	public JsonElement? PendingCard { get; set; }
}
