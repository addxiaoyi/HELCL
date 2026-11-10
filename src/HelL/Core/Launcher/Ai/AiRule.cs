namespace HelL.Core.Launcher.Ai;

public sealed class AiRule
{
	public string tool_name;

	public object rule_content;

	public string behavior = "allow";

	public string scope = "instance";

	public AiRule(string toolName, object ruleContent = null, string behavior = "allow", string scope = "instance")
	{
		tool_name = toolName;
		rule_content = ruleContent;
		this.behavior = behavior;
		this.scope = scope;
	}

	public string Key()
	{
		return tool_name + "\0" + (Py.Truthy(rule_content) ? Py.Str(rule_content) : "");
	}
}
