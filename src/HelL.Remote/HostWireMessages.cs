using System.Text.Json;
using HelL.Contracts;

namespace HelL.Remote;

public static class HostWireMessages
{
    public static HostRequest ReadRequest(byte[] payload)
    {
        HostRequest request = Read<HostRequest>(payload, "Protocol", "RequestId", "Command");
        try { request.Validate(); }
        catch (ArgumentException) { throw Invalid(); }
        return request;
    }

    public static HostReply ReadReply(byte[] payload, HostRequest request)
    {
        HostReply reply = Read<HostReply>(payload, "Protocol", "RequestId", "Ok", "Device", "Tasks");
        try { reply.Validate(request); }
        catch (ArgumentException) { throw Invalid(); }
        return reply;
    }

    private static T Read<T>(byte[] payload, params string[] required) where T : class
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Length is < 1 or > HostProtocol.MaxMessageBytes) throw Invalid();
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 16 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw Invalid();
            ValidateObject(document.RootElement);
            foreach (string field in required)
                if (!document.RootElement.TryGetProperty(field, out _)) throw Invalid();
            return document.RootElement.Deserialize<T>() ?? throw Invalid();
        }
        catch (JsonException) { throw Invalid(); }
    }

    private static void ValidateObject(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var fields = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty field in value.EnumerateObject())
            {
                if (!fields.Add(field.Name)) throw Invalid();
                ValidateObject(field.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (JsonElement item in value.EnumerateArray()) ValidateObject(item);
    }

    private static InvalidDataException Invalid() => new("The host message is malformed, ambiguous, or exceeds its agreed limits.");
}
