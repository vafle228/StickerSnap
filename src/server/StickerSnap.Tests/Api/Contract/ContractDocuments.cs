using System.Text;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using Microsoft.OpenApi.YamlReader;
using StickerSnap.Tests.TestData;

namespace StickerSnap.Tests.Api.Contract;

/// <summary>
/// The two documents the contract test compares: src/contract/openapi.yaml, embedded in the test
/// assembly as `Contract/openapi.yaml`, and the OpenAPI document the server generates from its
/// controllers, the one Development serves at /openapi/v1.json.
/// </summary>
public static class ContractDocuments
{
    private const string ContractResource = "Contract/openapi.yaml";

    /// <summary>src/contract/openapi.yaml.</summary>
    public static Task<OpenApiDocument> ContractAsync(CancellationToken cancellationToken)
    {
        using var stream = typeof(ContractDocuments).Assembly.GetManifestResourceStream(ContractResource)
            ?? throw new FileNotFoundException($"No {ContractResource} among the test assembly's resources");
        using var reader = new StreamReader(stream);
        return ReadAsync(reader.ReadToEnd(), cancellationToken);
    }

    /// <summary>A YAML of TestData/, such as a trimmed contract: `Contract/trimmed.yaml`.</summary>
    public static Task<OpenApiDocument> TestFileAsync(string path, CancellationToken cancellationToken) =>
        ReadAsync(TestFiles.ReadText(path), cancellationToken);

    /// <summary>The server's own document, generated in memory: no Development, no HTTP.</summary>
    public static async Task<OpenApiDocument> ServerAsync(CancellationToken cancellationToken)
    {
        using var factory = new ApiFactory();
        var provider = factory.Services.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1");
        return await provider.GetOpenApiDocumentAsync(cancellationToken);
    }

    private static async Task<OpenApiDocument> ReadAsync(string yaml, CancellationToken cancellationToken)
    {
        var settings = new OpenApiReaderSettings();
        settings.AddYamlReader();

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(yaml));
        var (document, diagnostic) = await OpenApiDocument.LoadAsync(stream, "yaml", settings, cancellationToken);

        diagnostic?.Errors.Should().BeEmpty("the contract must read without errors");
        return document ?? throw new InvalidDataException("The YAML is not an OpenAPI document");
    }
}
