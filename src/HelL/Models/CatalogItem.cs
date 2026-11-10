using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HelL.Models;

public sealed class CatalogItem
{
	public string Name { get; set; } = "";
    public string ChineseName { get; set; } = "";
    public string NameLocalization { get; set; } = "";
    public string Kind { get; set; } = "";
    public List<string> SearchAliases { get; set; } = new();
    [JsonIgnore]
    public string DisplayName => HelL.Core.Launcher.I18nCore.CurrentLanguage() == "en" || ChineseName.Length == 0 ? Name : ChineseName;

	public string Author { get; set; } = "";

	public long Downloads { get; set; }

	public JsonElement? Id { get; set; }

	public string? Slug { get; set; }

	public string Source { get; set; } = "";

	public string Description { get; set; } = "";
    public string OriginalDescription { get; set; } = "";
    public string Localization { get; set; } = "";
    public string CoverUrl { get; set; } = "";
    public List<CatalogImage> Gallery { get; set; } = new List<CatalogImage>();
    public List<string> Loaders { get; set; } = new List<string>();
    public List<string> GameVersions { get; set; } = new List<string>();

	public List<string>? Tags { get; set; }

	public string Updated { get; set; } = "";

	[JsonPropertyName("game_version")]
	public string GameVersion { get; set; } = "";

	[JsonPropertyName("icon_url")]
	public string IconUrl { get; set; } = "";

	[JsonPropertyName("project_url")]
	public string ProjectUrl { get; set; } = "";

	[JsonIgnore]
	public object? IdValue
	{
		get
		{
			JsonElement? id = Id;
			if (id.HasValue)
			{
				JsonElement valueOrDefault = id.GetValueOrDefault();
				switch (valueOrDefault.ValueKind)
				{
				case JsonValueKind.Number:
				{
					if (!valueOrDefault.TryGetInt64(out var value))
					{
						return valueOrDefault.GetDouble();
					}
					return value;
				}
				case JsonValueKind.String:
					return valueOrDefault.GetString();
				case JsonValueKind.Undefined:
				case JsonValueKind.Null:
					return null;
				default:
					return valueOrDefault.ToString();
				}
			}
			return null;
		}
	}

	[JsonIgnore]
	public string Key => Source + ":" + (!string.IsNullOrWhiteSpace(Slug) ? Slug : IdValue?.ToString() ?? Name);
}

public sealed class CatalogImage
{
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
}

public sealed class CatalogCompatibility
{
    public int DependencyCount { get; set; }
    public string Version { get; set; } = "";
    public string Error { get; set; } = "";
    public List<string> Loaders { get; set; } = new List<string>();
    public List<string> GameVersions { get; set; } = new List<string>();
    public List<CatalogDependency> Dependencies { get; set; } = new List<CatalogDependency>();
}

public sealed class CatalogDependency
{
    public string Source { get; set; } = "";
    public string ProjectId { get; set; } = "";
    public string VersionId { get; set; } = "";
    public string ProjectType { get; set; } = "";
    public string Name { get; set; } = "";
    public string ChineseName { get; set; } = "";
    public string DisplayName => HelL.Core.Launcher.I18nCore.CurrentLanguage() == "en" || ChineseName.Length == 0 ? Name : ChineseName;
    public string Kind { get; set; } = "";
    public string Version { get; set; } = "";
    public string Url { get; set; } = "";
}

public sealed class DependencyPlan
{
    public string Token { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Filename { get; set; } = "";
    public string Destination { get; set; } = "";
    public string GameVersion { get; set; } = "";
    public string Loader { get; set; } = "";
    public long Bytes { get; set; }
}
