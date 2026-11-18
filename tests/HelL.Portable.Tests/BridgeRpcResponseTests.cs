using System.Net;
using System.Text.Json;
using HelL.Core;
using Xunit;

namespace HelL.Portable.Tests;

public sealed class BridgeRpcResponseTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("not-json")]
    [InlineData("{\"jsonrpc\":\"1.0\",\"id\":7,\"result\":true}")]
    [InlineData("{\"jsonrpc\":2,\"id\":7,\"result\":true}")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"result\":true}")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":8,\"result\":true}")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":\"7\",\"result\":true}")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":7}")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":7,\"error\":null}")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":7,\"result\":true,\"error\":{\"code\":-32000,\"message\":\"failed\"}}")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":7,\"error\":{\"code\":\"-32000\",\"message\":\"failed\"}}")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":7,\"error\":{\"code\":-32000,\"message\":3}}")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":7,\"error\":\"failed\"}")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":7,\"id\":7,\"result\":true}")]
    public void RejectsInvalidOrMismatchedEnvelopes(string json)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        Assert.Throws<InvalidDataException>(() => BridgeRpcResponse.Read(response, json, 7));
    }

    [Fact]
    public void HttpFailureCannotMasqueradeAsASuccessfulRpcResult()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        Assert.Throws<HttpRequestException>(() => BridgeRpcResponse.Read(response, "{\"jsonrpc\":\"2.0\",\"id\":7,\"result\":true}", 7));
    }

    [Fact]
    public void ValidResultAndExplicitNullKeepTheirJsonKinds()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        JsonElement value = BridgeRpcResponse.Read(response, "{\"jsonrpc\":\"2.0\",\"id\":7,\"result\":true}", 7);
        Assert.True(value.GetBoolean());
        JsonElement empty = BridgeRpcResponse.Read(response, "{\"jsonrpc\":\"2.0\",\"id\":8,\"result\":null}", 8);
        Assert.Equal(JsonValueKind.Null, empty.ValueKind);
    }

    [Fact]
    public void ValidRpcErrorPreservesItsCodeAndMessage()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        RpcError error = Assert.Throws<RpcError>(() => BridgeRpcResponse.Read(response, "{\"jsonrpc\":\"2.0\",\"id\":7,\"error\":{\"code\":-32602,\"message\":\"invalid parameter\"}}", 7));
        Assert.Equal(-32602, error.Code);
        Assert.Equal("invalid parameter", error.Message);
    }

    [Theory]
    [InlineData("\"wrong type\"")]
    [InlineData("null")]
    [InlineData("{}")]
    public void InvalidTypedResultsNeverBecomeDefaultValues(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Throws<JsonException>(() => BridgeRpcResponse.Deserialize<int>(document.RootElement, new JsonSerializerOptions()));
    }

    [Fact]
    public void ExplicitNullRemainsAvailableForNullableContracts()
    {
        using JsonDocument document = JsonDocument.Parse("null");
        Assert.Null(BridgeRpcResponse.Deserialize<int?>(document.RootElement, new JsonSerializerOptions()));
        Assert.Null(BridgeRpcResponse.Deserialize<string>(document.RootElement, new JsonSerializerOptions()));
        Assert.Throws<JsonException>(() => BridgeRpcResponse.Deserialize<int>(default, new JsonSerializerOptions()));
    }
}
