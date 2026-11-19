using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HelL.Models;

public sealed class CrashAction
{
	public string Id { get; set; } = "";

	public string Label { get; set; } = "";

	public List<string>? Mods { get; set; }

	public int? Major { get; set; }

	[JsonPropertyName("memory_mb")]
	public int? MemoryMb { get; set; }

	public string Instance { get; set; } = "";

	public string Version { get; set; } = "";

	public string Path { get; set; } = "";
}
