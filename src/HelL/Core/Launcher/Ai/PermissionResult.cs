namespace HelL.Core.Launcher.Ai;

public sealed class PermissionResult
{
	public string decision;

	public string reason = "";

	public string rule_id = "";

	public PyDict modified_input;

	public PermissionResult(string decision, string reason = "", string ruleId = "")
	{
		this.decision = decision;
		this.reason = reason;
		rule_id = ruleId;
	}
}
