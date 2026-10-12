using Microsoft.OpenApi;

namespace StickerSnap.Tests.Api.Contract;

/// <summary>
/// The comparer turns red where it should: the real server against a trimmed contract
/// (TestData/Contract/trimmed.yaml) that renamed a field, lacks two operations and has one the
/// server does not serve.
/// </summary>
public sealed class ContractComparerTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static async Task<IReadOnlyList<string>> CompareWithTrimmed(params string[] notYetOnServer)
    {
        var contract = await ContractDocuments.TestFileAsync("Contract/trimmed.yaml", Cancel);
        var server = await ContractDocuments.ServerAsync(Cancel);
        return new ContractComparer(contract, server, notYetOnServer.ToHashSet()).Compare();
    }

    [Fact]
    public async Task Notices_a_renamed_field()
    {
        var differences = await CompareWithTrimmed("listPacks");

        differences.Should().Contain([
            "getSettings 200 required: missing on the server: recent",
            "getSettings 200 required: not in the contract: recentEmoji",
            "getSettings 200 properties: missing on the server: recent",
            "getSettings 200 properties: not in the contract: recentEmoji",
        ]);
    }

    [Fact]
    public async Task Notices_operations_the_contract_does_not_have()
    {
        var differences = await CompareWithTrimmed("listPacks");

        differences.Should().Contain([
            "getApp (GET /api/v1/app) is on the server but not in the contract",
            "patchSettings (PATCH /api/v1/settings) is on the server but not in the contract",
        ]);
    }

    [Fact]
    public async Task Notices_an_operation_the_server_does_not_serve_and_the_list_does_not_name()
    {
        var differences = await CompareWithTrimmed();

        differences.Should().Contain(
            "listPacks (GET /api/v1/packs) is in the contract but not on the server, nor among the operations not yet on the server");
    }

    [Fact]
    public async Task Notices_an_operation_served_while_still_listed_as_not_yet_on_the_server()
    {
        var differences = await CompareWithTrimmed("listPacks", "getSettings", "renamedLongAgo");

        differences.Should().Contain([
            "getSettings (GET /api/v1/settings) is on the server: strike it from the operations not yet on the server",
            "renamedLongAgo is among the operations not yet on the server but not in the contract",
        ]);
    }

    [Fact]
    public async Task Compares_the_values_of_an_enum_and_the_variants_of_a_one_of()
    {
        var contract = await ContractDocuments.TestFileAsync("Contract/trimmed.yaml", Cancel);
        var server = await ContractDocuments.TestFileAsync("Contract/trimmed.yaml", Cancel);
        ((OpenApiSchema)Property(server, "theme")).Enum!.RemoveAt(2);
        Settings(contract).Properties!["accent"] = Variants("named", "custom");
        Settings(server).Properties!["accent"] = Variants("named");

        var differences = new ContractComparer(contract, server, new HashSet<string>()).Compare();

        differences.Should().Equal(
            "getSettings 200 theme enum: missing on the server: system",
            "getSettings 200 accent variants: missing on the server: custom");
    }

    private static OpenApiSchema Settings(OpenApiDocument document) => (OpenApiSchema)document.Components!.Schemas!["Settings"];

    private static IOpenApiSchema Property(OpenApiDocument document, string name) => Settings(document).Properties![name];

    /// <summary>A oneOf whose branches fix `kind` to each of <paramref name="kinds"/>.</summary>
    private static OpenApiSchema Variants(params string[] kinds) => new()
    {
        OneOf = kinds.Select(kind => (IOpenApiSchema)new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Properties = new Dictionary<string, IOpenApiSchema> { ["kind"] = new OpenApiSchema { Type = JsonSchemaType.String, Const = kind } },
        }).ToList(),
    };
}
