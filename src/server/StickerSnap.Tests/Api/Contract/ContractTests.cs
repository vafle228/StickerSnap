using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.OpenApi;
using StickerSnap.Api.Events;
using StickerSnap.Api.Problems;
using StickerSnap.Application.Events;
using StickerSnap.Core.Errors;

namespace StickerSnap.Tests.Api.Contract;

/// <summary>
/// The server against src/contract/openapi.yaml, so hand-written DTOs cannot drift from the
/// contract: operations and their schemas, the error codes with their statuses, the hub's events.
/// A change of the contract goes to the contract's document first, then to the YAML, then here.
/// </summary>
public sealed partial class ContractTests
{
    /// <summary>
    /// The contract's operations the server does not serve yet. The list only shrinks: the PR
    /// that brings an operation strikes it from here, or this test fails.
    /// </summary>
    public static readonly IReadOnlySet<string> NotYetOnServer = new HashSet<string>
    {
        "getAccount", "startAccountLink", "deleteAccountLink", "checkAccount",
        "listPacks", "importPack", "checkPackName", "getPack", "deletePack", "syncPack",
        "deletePackSticker", "getPackStickerMedia",
        "getPublishDraft",
    };

    /// <summary>The events of x-events the hub does not send yet. The list only shrinks, as above.</summary>
    private static readonly IReadOnlySet<string> EventsNotYetSent = new HashSet<string>
    {
        "accountChanged", "packChanged",
    };

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Serves_the_operations_and_schemas_of_the_contract()
    {
        var contract = await ContractDocuments.ContractAsync(Cancel);
        var server = await ContractDocuments.ServerAsync(Cancel);

        var differences = new ContractComparer(contract, server, NotYetOnServer).Compare();

        differences.Should().BeEmpty();
    }

    [Fact]
    public async Task Has_the_error_codes_of_the_contract()
    {
        var contract = await ContractDocuments.ContractAsync(Cancel);

        var codes = Enum.GetValues<ErrorCode>().Select(SnakeCase);

        codes.Should().Equal(contract.Components!.Schemas!["ErrorCode"].Enum!.Select(code => code!.GetValue<string>()));
    }

    [Fact]
    public async Task Gives_each_code_the_status_of_the_contract_s_table()
    {
        var contract = await ContractDocuments.ContractAsync(Cancel);
        var table = StatusRow().Matches(contract.Components!.Schemas!["ErrorCode"].Description!)
            .ToDictionary(row => row.Groups["code"].Value, row => int.Parse(row.Groups["status"].Value));

        var statuses = Enum.GetValues<ErrorCode>().ToDictionary(SnakeCase, ErrorStatus.Of);

        table.Should().NotBeEmpty();
        statuses.Should().Equal(table);
    }

    [Fact]
    public async Task Sends_the_events_of_the_contract_on_its_hub()
    {
        var events = XEvents(await ContractDocuments.ContractAsync(Cancel));

        // An event is a method of IEventPublisher: SettingsChangedAsync sends settingsChanged.
        var sent = typeof(IEventPublisher).GetMethods()
            .Select(method => JsonNamingPolicy.CamelCase.ConvertName(method.Name.Replace("Async", "")))
            .ToHashSet();

        events["hub"]!.GetValue<string>().Should().Be(EventsHub.Path);
        var names = events["events"]!.AsObject().Select(entry => entry.Key).ToHashSet();
        sent.Should().BeSubsetOf(names, "the hub sends only events of x-events");
        sent.Should().NotIntersectWith(EventsNotYetSent, "an event the hub sends is struck from the events not yet sent");
        names.Except(sent).Should().BeEquivalentTo(EventsNotYetSent, "every event of x-events is sent or not yet sent");
    }

    private static JsonNode XEvents(OpenApiDocument contract) =>
        contract.Extensions?["x-events"] is JsonNodeExtension { Node: { } node }
            ? node
            : throw new InvalidDataException("The contract has no x-events");

    private static string SnakeCase(ErrorCode code) => JsonNamingPolicy.SnakeCaseLower.ConvertName(code.ToString());

    /// <summary>A row of the table in ErrorCode's description: `| source_unreadable | 422 |`.</summary>
    [GeneratedRegex(@"^\| (?<code>[a-z_]+) \| (?<status>\d{3}) \|\r?$", RegexOptions.Multiline)]
    private static partial Regex StatusRow();
}
