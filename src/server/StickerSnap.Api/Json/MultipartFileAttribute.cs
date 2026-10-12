using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace StickerSnap.Api.Json;

/// <summary>
/// An action that reads one file from a multipart/form-data body itself, as a stream, instead of
/// binding it: a video must not be buffered whole before the action sees it. Only the server's
/// OpenAPI document learns the body from it, through <see cref="MultipartFileTransformer"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class MultipartFileAttribute(string field) : Attribute
{
    /// <summary>The form field holding the file: `file`.</summary>
    public string Field { get; } = field;
}

/// <summary>Writes the required multipart body of <see cref="MultipartFileAttribute"/> into its operation.</summary>
public sealed class MultipartFileTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var attribute = (context.Description.ActionDescriptor as ControllerActionDescriptor)?.MethodInfo
            .GetCustomAttributes(typeof(MultipartFileAttribute), inherit: false)
            .OfType<MultipartFileAttribute>()
            .FirstOrDefault();
        if (attribute is null) return Task.CompletedTask;

        operation.RequestBody = new OpenApiRequestBody
        {
            Required = true,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["multipart/form-data"] = new()
                {
                    Schema = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Object,
                        Required = new HashSet<string> { attribute.Field },
                        Properties = new Dictionary<string, IOpenApiSchema>
                        {
                            [attribute.Field] = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" },
                        },
                    },
                },
            },
        };
        return Task.CompletedTask;
    }
}
