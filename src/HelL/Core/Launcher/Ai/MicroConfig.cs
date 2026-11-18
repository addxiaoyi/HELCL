namespace HelL.Core.Launcher.Ai;

public sealed class MicroConfig
{
	public bool enabled = true;

	public long? threshold_tokens;

	public long keep_recent_tool_results = 5L;

	public long min_token_savings = 2000L;

	public long idle_threshold_minutes = 30L;
}
