using System.Text.Json.Serialization;

namespace HelL.Models;

public sealed class SmartRecommendation
{
	[JsonPropertyName("memory_mb")]
	public int MemoryMb { get; set; } = 4096;

	[JsonPropertyName("java_major")]
	public int JavaMajor { get; set; } = 17;

	[JsonPropertyName("window_width")]
	public int WindowWidth { get; set; } = 854;

	[JsonPropertyName("window_height")]
	public int WindowHeight { get; set; } = 480;

	[JsonPropertyName("gc_preset")]
	public string GcPreset { get; set; } = "auto";

	[JsonPropertyName("cpu_count")]
	public int CpuCount { get; set; }

	[JsonPropertyName("total_ram_gb")]
	public double TotalRamGb { get; set; }
}
