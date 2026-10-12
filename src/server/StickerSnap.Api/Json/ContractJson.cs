using System.Text.Json;
using System.Text.Json.Serialization;

namespace StickerSnap.Api.Json;

/// <summary>
/// The contract's JSON (openapi.yaml, «Формат»): camelCase fields, enums as snake_case strings
/// (`too_old`, `custom_emoji`, `pingpong`), numbers only as numbers, and an optional field is left
/// out rather than null.
/// REST and the SignalR hub serialize with the same options.
/// </summary>
public static class ContractJson
{
    public static JsonSerializerOptions Apply(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.NumberHandling = JsonNumberHandling.Strict;
        // A polymorphic body's `kind` may come after its other fields: `{ "path": …, "kind": "path" }`
        options.AllowOutOfOrderMetadataProperties = true;
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false));
        return options;
    }
}
