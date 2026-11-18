namespace HelL.Core.Launcher.Ai;

public sealed class TokenState
{
	public long base_tokens;

	public long base_msg_index;

	public long cache_read;

	public long cache_write;

	public long last_output_tokens;

	public string source = "estimate";
}
