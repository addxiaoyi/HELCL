using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HelL.Models;

public sealed class CrashReport
{
	public string Title { get; set; } = "";

	public string Headline { get; set; } = "";

	public string Summary { get; set; } = "";

	public string Detail { get; set; } = "";

	public string Help { get; set; } = "";

	[JsonPropertyName("direct_file")]
	public string DirectFile { get; set; } = "";

	[JsonPropertyName("exit_code")]
	public int? ExitCode { get; set; }

	[JsonPropertyName("exit_hint")]
	public string ExitHint { get; set; } = "";

	[JsonPropertyName("task_id")]
	public string TaskId { get; set; } = "";

	public string Instance { get; set; } = "";

	public string Version { get; set; } = "";

	[JsonPropertyName("is_crash")]
	public bool IsCrash { get; set; }

	public List<CrashAction>? Actions { get; set; }
}
