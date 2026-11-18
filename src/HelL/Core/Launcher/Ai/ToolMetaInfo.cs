namespace HelL.Core.Launcher.Ai;

public sealed class ToolMetaInfo
{
	public readonly string name;

	public readonly string side_effect;

	public readonly string risk;

	public readonly bool @readonly;

	public readonly bool long_running;

	public ToolMetaInfo(string name, bool ro, string side, string risk, bool lr)
	{
		this.name = name;
		@readonly = ro;
		side_effect = side;
		this.risk = risk;
		long_running = lr;
	}
}
