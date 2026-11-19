using System.Text.Json.Serialization;

namespace HelL.Models;

public sealed class VersionSettingsDto
{
	public string Isolation { get; set; } = "none";

	[JsonPropertyName("memory_mb")]
	public int? MemoryMb { get; set; }

	[JsonPropertyName("jvm_args")]
	public string JvmArgs { get; set; } = "";

	public string Server { get; set; } = "";

	public string Port { get; set; } = "";

	[JsonPropertyName("pre_launch")]
	public string PreLaunch { get; set; } = "";

	[JsonPropertyName("post_launch")]
	public string PostLaunch { get; set; } = "";

	[JsonPropertyName("login_account")]
	public string LoginAccount { get; set; } = "";

	[JsonPropertyName("nide8_id")]
	public string Nide8Id { get; set; } = "";

	public string Gc { get; set; } = "";

	[JsonPropertyName("window_title")]
	public string WindowTitle { get; set; } = "";

	[JsonPropertyName("window_mode")]
	public string WindowMode { get; set; } = "window";

	[JsonPropertyName("pre_launch_wait")]
	public bool PreLaunchWait { get; set; } = true;

	public bool Hidden { get; set; }

	[JsonPropertyName("offline_skin")]
	public string OfflineSkin { get; set; } = "default";
}
