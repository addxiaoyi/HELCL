using System.Text.Json;
using HelL.Models;

namespace HelL.Services;

public sealed class BridgeEvent
{
	public string Event { get; set; } = "";

	public string TaskId { get; set; } = "";

	public string Title { get; set; } = "";

	public string Message { get; set; } = "";

	public string Text { get; set; } = "";

	public int Current { get; set; }

	public int Total { get; set; }

	public int Count { get; set; }

	public bool Success { get; set; }

	public bool Stopped { get; set; }

	public string Code { get; set; } = "";

	public string Uri { get; set; } = "";

    public int ExpiresIn { get; set; }

	public string Kind { get; set; } = "";

	public string Label { get; set; } = "";

	public string Name { get; set; } = "";

	public JsonElement Payload { get; set; }

	public CrashReport? Crash { get; set; }
}
