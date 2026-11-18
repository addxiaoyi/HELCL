using System.Text.Json;
using HelL.Core.Launcher.Ai;
using Xunit;

namespace HelL.Tests;

public class McpServerTests
{
	private static JsonElement Call(string json)
	{
		string resp = McpServer.Dispatch(json);
		Assert.NotNull(resp);
		return JsonDocument.Parse(resp).RootElement;
	}

	[Fact]
	public void Initialize()
	{
		JsonElement r = Call("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\"}}");
		Assert.Equal(1, r.GetProperty("id").GetInt32());
		JsonElement result = r.GetProperty("result");
		Assert.Equal("2025-06-18", result.GetProperty("protocolVersion").GetString());
		Assert.Equal("tipcl", result.GetProperty("serverInfo").GetProperty("name").GetString());
	}

	[Fact]
	public void ToolsListHidesUiOnlyTools()
	{
		JsonElement tools = Call("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}").GetProperty("result").GetProperty("tools");
		string all = tools.GetRawText();
		Assert.Contains("\"list_mods\"", all);
		Assert.Contains("\"diagnose_lag\"", all);
		foreach (string hidden in new[] { "ask_user", "dispatch_subagent", "update_plan", "remember", "forget" })
		{
			Assert.DoesNotContain("\"" + hidden + "\"", all);
		}
	}

	[Fact]
	public void HiddenToolCallRefused()
	{
		JsonElement r = Call("{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"remember\",\"arguments\":{\"text\":\"x\"}}}").GetProperty("result");
		Assert.True(r.GetProperty("isError").GetBoolean());
	}

	[Fact]
	public void ErrorsAndNotifications()
	{
		Assert.Equal(-32700, Call("{oops").GetProperty("error").GetProperty("code").GetInt32());
		Assert.Equal(-32601, Call("{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"nope\"}").GetProperty("error").GetProperty("code").GetInt32());
		Assert.Null(McpServer.Dispatch("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}"));
	}

	[Fact]
	public void Batch()
	{
		JsonElement r = Call("[{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"},{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"ping\"}]");
		Assert.Equal(JsonValueKind.Array, r.ValueKind);
		Assert.Equal(2, r.GetArrayLength());
	}
}
