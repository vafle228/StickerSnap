using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace StickerSnap.Api.Json;

/// <summary>
/// A map whose values are strings or numbers, such as a problem's `params` ({ ext: "avi" },
/// { retryAfterSec: 24 }): held as objects in C#, said so in the server's OpenAPI document
/// through <see cref="StringOrNumberValuesTransformer"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class StringOrNumberValuesAttribute : Attribute;

/// <summary>Gives the values of a map marked <see cref="StringOrNumberValuesAttribute"/> the type `[string, number]`.</summary>
public sealed class StringOrNumberValuesTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        var marked = context.JsonPropertyInfo?.AttributeProvider?
            .GetCustomAttributes(typeof(StringOrNumberValuesAttribute), inherit: false)
            .Length > 0;
        if (marked) schema.AdditionalProperties = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Number };
        return Task.CompletedTask;
    }
}
