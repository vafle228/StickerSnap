using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace StickerSnap.Api.Json;

/// <summary>
/// Marks the discriminator of a polymorphic type (`mode` of a crop, `kind` of a concession) as
/// required in the schema of each derived type, as the contract has it: System.Text.Json always
/// writes it and needs it to read the body, yet the generated schema leaves it optional.
/// </summary>
public sealed class DiscriminatorTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        var property = context.JsonTypeInfo.Type.BaseType?.GetCustomAttribute<JsonPolymorphicAttribute>()?.TypeDiscriminatorPropertyName;
        if (property is not null && schema.Properties?.ContainsKey(property) == true)
        {
            schema.Required ??= new HashSet<string>();
            schema.Required.Add(property);
        }
        return Task.CompletedTask;
    }
}
