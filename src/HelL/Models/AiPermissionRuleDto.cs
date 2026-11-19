using System.Text.Json.Serialization;

namespace HelL.Models;

public sealed class AiPermissionRuleDto
{
	public string Key { get; set; } = "";

	[JsonPropertyName("toolName")]
	public string ToolName { get; set; } = "";

	[JsonPropertyName("ruleContent")]
	public string RuleContent { get; set; } = "";

	public string Behavior { get; set; } = "";

	[JsonPropertyName("behavior_label")]
	public string BehaviorLabel { get; set; } = "";

	public string Instance { get; set; } = "";

	public string Scope { get; set; } = "";
}
