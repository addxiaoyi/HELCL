using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HelL.Models;

public sealed class CleanerPreview
{
	[JsonPropertyName("unused_libraries")]
	public List<CleanerEntry> UnusedLibraries { get; set; } = new List<CleanerEntry>();

	public List<CleanerEntry> Parts { get; set; } = new List<CleanerEntry>();

	public List<CleanerEntry> Cache { get; set; } = new List<CleanerEntry>();

	public long Bytes { get; set; }

	public int Count { get; set; }
}
