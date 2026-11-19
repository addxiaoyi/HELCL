using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HelL.Models;

public sealed class TerracottaSnap
{
	public bool Supported { get; set; }

	public bool Installed { get; set; }

	public bool Running { get; set; }

	public string State { get; set; } = "";

	public string Label { get; set; } = "";

	public string Room { get; set; } = "";

	public string Url { get; set; } = "";

	public string Error { get; set; } = "";

	public int Port { get; set; }

	public string Player { get; set; } = "";

	[JsonPropertyName("game_running")]
	public bool GameRunning { get; set; }

	[JsonPropertyName("error_hint")]
	public string ErrorHint { get; set; } = "";

	[JsonPropertyName("difficulty_hint")]
	public string DifficultyHint { get; set; } = "";

	public List<TerracottaProfile> Profiles { get; set; } = new List<TerracottaProfile>();

	public List<string> Nodes { get; set; } = new List<string>();

	public string Copyright { get; set; } = "";

	public string Home { get; set; } = "";
}
