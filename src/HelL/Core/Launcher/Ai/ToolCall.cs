namespace HelL.Core.Launcher.Ai;

public sealed class ToolCall
{
	public string id;

	public string name;

	public PyDict args;

	public string status = "scheduled";

	public string result = "";

	public string error = "";

	public double started_at;

	public double ended_at;

	public ToolCall(string id, string name, PyDict args)
	{
		this.id = id;
		this.name = name;
		this.args = args;
	}

	public void Mark(string status, string result = "", string error = "")
	{
		if (status == "running" && started_at == 0.0)
		{
			started_at = Py.Monotonic();
		}
		switch (status)
		{
		case "completed":
		case "failed":
		case "denied":
			ended_at = Py.Monotonic();
			break;
		}
		this.status = status;
		if (!string.IsNullOrEmpty(result))
		{
			this.result = result;
		}
		if (!string.IsNullOrEmpty(error))
		{
			this.error = error;
		}
	}
}
