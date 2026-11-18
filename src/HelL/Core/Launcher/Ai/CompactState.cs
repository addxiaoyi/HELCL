namespace HelL.Core.Launcher.Ai;

public sealed class CompactState
{
	public long consecutive_failures;

	public long rapid_refills;

	public long tool_turns_since_compact = 1000000000L;
}
