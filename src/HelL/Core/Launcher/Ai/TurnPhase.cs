using System.Collections.Generic;

namespace HelL.Core.Launcher.Ai;

public static class TurnPhase
{
	public const string IDLE = "idle";

	public const string PROCESSING_INPUT = "processing_input";

	public const string AWAITING_MODEL = "awaiting_model";

	public const string STREAMING = "streaming";

	public const string SCHEDULING_TOOLS = "scheduling_tools";

	public const string EXECUTING_TOOLS = "executing_tools";

	public const string AWAITING_PERMISSION = "awaiting_permission";

	public const string COMPLETING = "completing";

	public const string ERROR = "error";

	public static readonly Dictionary<string, string[]> ALLOWED = new Dictionary<string, string[]>
	{
		{
			"idle",
			new string[1] { "processing_input" }
		},
		{
			"processing_input",
			new string[2] { "awaiting_model", "completing" }
		},
		{
			"awaiting_model",
			new string[3] { "streaming", "completing", "error" }
		},
		{
			"streaming",
			new string[3] { "scheduling_tools", "completing", "error" }
		},
		{
			"scheduling_tools",
			new string[3] { "executing_tools", "awaiting_permission", "completing" }
		},
		{
			"executing_tools",
			new string[3] { "awaiting_model", "completing", "error" }
		},
		{
			"awaiting_permission",
			new string[3] { "executing_tools", "completing", "error" }
		},
		{
			"completing",
			new string[0]
		},
		{
			"error",
			new string[0]
		}
	};
}
