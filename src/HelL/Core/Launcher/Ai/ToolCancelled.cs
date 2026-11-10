namespace HelL.Core.Launcher.Ai;

public class ToolCancelled : PyException
{
	public ToolCancelled(string m = "已停止")
		: base("ToolCancelled", m)
	{
	}
}
