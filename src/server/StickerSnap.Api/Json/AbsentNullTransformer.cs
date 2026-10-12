using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace StickerSnap.Api.Json;

/// <summary>
/// The server's OpenAPI document says what <see cref="ContractJson"/> does with null: a nullable
/// field is left out rather than written as null, so it is optional and never null. Without this,
/// every nullable constructor parameter comes out required with a null type, which the contract
/// («необязательное поле отсутствует, а не null») forbids. A document transformer, so it sees the
/// schemas as they are written, after nullable references became `oneOf` with null.
/// </summary>
public sealed class AbsentNullTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        foreach (var schema in document.Components?.Schemas?.Values ?? []) Visit(schema);
        return Task.CompletedTask;
    }

    private static void Visit(IOpenApiSchema schema)
    {
        // A reference is visited as the component it points to.
        if (schema is not OpenApiSchema own) return;

        foreach (var (name, property) in own.Properties?.ToList() ?? [])
        {
            if (WithoutNull(property) is { } present)
            {
                own.Properties![name] = present;
                own.Required?.Remove(name);
            }
            Visit(own.Properties![name]);
        }
        if (own.Items is not null) Visit(own.Items);
    }

    /// <summary>The property's schema without null, or null when it was not nullable.</summary>
    private static IOpenApiSchema? WithoutNull(IOpenApiSchema property)
    {
        // A string? or an int?: "type": ["null", "string"]
        if (property is OpenApiSchema { Type: { } type } own && type.HasFlag(JsonSchemaType.Null))
        {
            own.Type = type & ~JsonSchemaType.Null;
            return own;
        }

        // A nullable reference to another schema: "oneOf": [{ "type": "null" }, { "$ref": … }]
        if (property.OneOf is { Count: 2 } branches && branches.Any(IsNull))
            return branches.Single(branch => !IsNull(branch));

        return null;
    }

    private static bool IsNull(IOpenApiSchema schema) => schema.Type == JsonSchemaType.Null;
}
