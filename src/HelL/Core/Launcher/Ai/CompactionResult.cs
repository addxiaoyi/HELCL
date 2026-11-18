using System.Collections.Generic;

namespace HelL.Core.Launcher.Ai;

public sealed class CompactionResult
{
	public List<object> messages;

	public long pre_token_count;

	public long post_token_count;

	public long summarized_message_count;

	public long kept_message_count;
}
