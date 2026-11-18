using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace HelL.Core;

public static class BridgeRpcResponse
{
    public static JsonElement Read(HttpResponseMessage response, string json, int expectedId)
    {
        if (response == null) throw new ArgumentNullException(nameof(response));
        response.EnsureSuccessStatusCode();
        JsonDocument document;
        try { document = JsonDocument.Parse(json); }
        catch (JsonException error) { throw new InvalidDataException("Bridge JSON-RPC response is not valid JSON.", error); }
        using (document)
        {
            JsonElement envelope = document.RootElement;
            if (envelope.ValueKind != JsonValueKind.Object) throw Invalid("must be an object");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in envelope.EnumerateObject())
                if (!names.Add(property.Name)) throw Invalid("contains duplicate fields");
            if (!envelope.TryGetProperty("jsonrpc", out JsonElement version)
                || version.ValueKind != JsonValueKind.String || version.GetString() != "2.0")
                throw Invalid("must declare JSON-RPC version 2.0");
            if (!envelope.TryGetProperty("id", out JsonElement id) || id.ValueKind != JsonValueKind.Number
                || !id.TryGetInt32(out int requestId) || requestId != expectedId)
                throw Invalid("id does not match its request");

            bool hasResult = envelope.TryGetProperty("result", out JsonElement value);
            bool hasError = envelope.TryGetProperty("error", out JsonElement error);
            if (hasResult == hasError) throw Invalid("must contain exactly one result or error");
            if (hasResult) return value.Clone();
            if (error.ValueKind != JsonValueKind.Object
                || !error.TryGetProperty("code", out JsonElement code) || code.ValueKind != JsonValueKind.Number
                || !code.TryGetInt64(out long errorCode)
                || !error.TryGetProperty("message", out JsonElement message) || message.ValueKind != JsonValueKind.String)
                throw Invalid("error must contain an integer code and a string message");
            throw new RpcError(errorCode, message.GetString()!);
        }
    }

    public static T? Deserialize<T>(JsonElement value, JsonSerializerOptions options)
    {
        if (value.ValueKind == JsonValueKind.Undefined)
            throw new JsonException("Bridge JSON-RPC result is missing.");
        if (value.ValueKind == JsonValueKind.Null && typeof(T).IsValueType && Nullable.GetUnderlyingType(typeof(T)) == null)
            throw new JsonException("Bridge JSON-RPC result cannot be null for " + typeof(T).Name + ".");
        return JsonSerializer.Deserialize<T>(value.GetRawText(), options);
    }

    private static InvalidDataException Invalid(string detail) => new InvalidDataException("Bridge JSON-RPC response " + detail + ".");
}
