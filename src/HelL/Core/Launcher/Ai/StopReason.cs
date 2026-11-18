namespace HelL.Core.Launcher.Ai;

public static class StopReason
{
	public const string COMPLETED = "completed";

	public const string NO_TOOL_CALL = "no_tool_call";

	public const string MAX_ROUNDS = "max_rounds";

	public const string TRUNCATED = "truncated";

	public const string PENDING_TASK = "pending_task";

	public const string STREAM_FAILED = "stream_failed";

	public const string EMPTY_RESPONSE = "empty_response";

	public const string RATE_LIMITED = "rate_limited";

	public const string CANCELLED = "cancelled";

	public const string ERROR = "error";
}
