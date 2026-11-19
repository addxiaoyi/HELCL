using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HelL.Models;

public sealed class CatalogFile
{
	public JsonElement? Id { get; set; }

	public string Name { get; set; } = "";

	[JsonPropertyName("version_number")]
	public string VersionNumber { get; set; } = "";

	public string Filename { get; set; } = "";

	[JsonPropertyName("game_versions")]
	public List<string>? GameVersions { get; set; }

	public List<string>? Loaders { get; set; }

	public string Date { get; set; } = "";

	public long Downloads { get; set; }

	[JsonPropertyName("release_type")]
	public string ReleaseType { get; set; } = "";

	public string Source { get; set; } = "";
    public string VersionSource { get; set; } = "";
    public string VersionWarning { get; set; } = "";

	[JsonIgnore]
	public object? IdValue
	{
		get
		{
			JsonElement? id = Id;
			if (id.HasValue)
			{
				JsonElement valueOrDefault = id.GetValueOrDefault();
				long value;
				return valueOrDefault.ValueKind switch
				{
					JsonValueKind.Number => valueOrDefault.TryGetInt64(out value) ? ((object)value) : ((object)valueOrDefault.GetDouble()), 
					JsonValueKind.String => valueOrDefault.GetString(), 
					_ => null, 
				};
			}
			return null;
		}
	}
}
