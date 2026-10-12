using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace StickerSnap.Api.Json;

/// <summary>
/// A field the contract fixes to one value (`const: 1`), such as a scene's schemaVersion. Only the
/// server's OpenAPI document says so, through <see cref="ConstTransformer"/>; the value itself is
/// checked where the field is used.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class ConstAttribute(object value) : Attribute
{
    public object Value { get; } = value;
}

/// <summary>Writes the value of <see cref="ConstAttribute"/> into the schema of its property.</summary>
public sealed class ConstTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        var attribute = context.JsonPropertyInfo?.AttributeProvider?
            .GetCustomAttributes(typeof(ConstAttribute), inherit: false)
            .OfType<ConstAttribute>()
            .FirstOrDefault();
        if (attribute is not null) schema.Const = Convert.ToString(attribute.Value, System.Globalization.CultureInfo.InvariantCulture);
        return Task.CompletedTask;
    }
}
