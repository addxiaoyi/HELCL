using System.Collections.Generic;

namespace HelL.Models;

public sealed class AiChatDto
{
	public string Id { get; set; } = "";

	public string Title { get; set; } = "";

	public List<AiMsgDto> Messages { get; set; } = new List<AiMsgDto>();

	public AiPlanDto? Plan { get; set; }
}
