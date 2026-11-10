namespace HelL.Core.Launcher.Ai;

public sealed class AutoConfig
{
	public bool enabled = true;

	public long context_window = 131072L;

	public long buffer_tokens = 13000L;

	public long max_consecutive_failures = 3L;

	public long max_consecutive_rapid_refills = 3L;

	public long tool_turn_threshold = 3L;

	public long keep_recent_messages = 6L;
}
