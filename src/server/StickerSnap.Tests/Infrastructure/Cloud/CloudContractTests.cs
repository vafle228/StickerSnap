using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.OpenApi;
using StickerSnap.Infrastructure.Cloud;
using StickerSnap.Tests.Api.Contract;

namespace StickerSnap.Tests.Infrastructure.Cloud;

/// <summary>
/// The client against src/contract/bot-service.yaml: every request it makes is an operation of the
/// contract, every operation a desktop may call is made, and its bodies have the contract's fields.
/// A change of the contract that the client does not follow fails here, not in a user's hands.
/// </summary>
public sealed class CloudContractTests : IDisposable
{
    private const string Pack = "reactions_call_by_StickerSnapAppBot";

    private const string PackJson = """{"name":"reactions_call_by_StickerSnapAppBot","title":"T","kind":"stickers","stickers":[]}""";

    private readonly CloudServiceStub _service = new();

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public void Dispose() => _service.Dispose();

    /// <summary>Each body of the client and the schema it is in the contract.</summary>
    public static TheoryData<Type, string> Bodies => new()
    {
        { typeof(LinkRequestBody), "LinkRequestBody" },
        { typeof(LinkCreated), "LinkCreated" },
        { typeof(LinkState), "LinkState" },
        { typeof(LinkedDeviceBody), "LinkedDevice" },
        { typeof(CloudDevice), "Device" },
        { typeof(CloudUser), "LinkedUser" },
        { typeof(FeedbackRequest), "FeedbackRequest" },
        { typeof(FeedbackErrorBody), "FeedbackError" },
        { typeof(FeedbackCreated), "FeedbackCreated" },
        { typeof(UploadedSticker), "UploadedSticker" },
        { typeof(InputSticker), "InputSticker" },
        { typeof(NewPackBody), "NewPackBody" },
        { typeof(CloudPack), "PublishedPack" },
        { typeof(CloudSticker), "PublishedSticker" },
        { typeof(CloudProblem), "Problem" },
    };

    /// <summary>Each enum of the client and the schema of its values.</summary>
    public static TheoryData<Type, string> Enums => new()
    {
        { typeof(DeviceOs), "DeviceOs" },
        { typeof(FeedbackType), "FeedbackType" },
        { typeof(CloudPackKind), "PackKind" },
        { typeof(LinkStatus), "LinkState.status" },
    };

    [Theory]
    [MemberData(nameof(Bodies))]
    public async Task Writes_and_reads_the_fields_of_the_contract(Type body, string schemaName)
    {
        var schema = (await SchemaAsync(schemaName)).Properties ?? new Dictionary<string, IOpenApiSchema>();
        var required = (await SchemaAsync(schemaName)).Required ?? new HashSet<string>();
        var fields = body.GetProperties().Select(property => JsonNamingPolicy.CamelCase.ConvertName(property.Name)).ToList();

        // Problem reads only what it needs; every other body has all the contract asks for
        if (body != typeof(CloudProblem)) fields.Should().Contain(required, $"{body.Name} must carry what {schemaName} requires");
        schema.Keys.Should().Contain(fields, $"{body.Name} must not invent fields {schemaName} does not have");
    }

    [Theory]
    [MemberData(nameof(Enums))]
    public async Task Names_enum_values_as_the_contract_does(Type type, string schemaName)
    {
        var parts = schemaName.Split('.');
        var schema = await SchemaAsync(parts[0]);
        if (parts.Length > 1) schema = schema.Properties![parts[1]];

        var values = Enum.GetNames(type).Select(name => JsonSerializer.Serialize(Enum.Parse(type, name), CloudClient.Json).Trim('"'));

        values.Should().BeEquivalentTo(schema.Enum!.Select(value => value!.GetValue<string>()));
    }

    [Fact]
    public async Task Calls_only_operations_of_the_contract_and_every_one_a_desktop_may()
    {
        var document = await ContractDocuments.BotServiceAsync(Cancel);
        await MakeEveryCallAsync();

        var operations = (document.Paths ?? [])
            .SelectMany(path => (path.Value.Operations ?? []).Select(operation => (
                Path: path.Key, Method: operation.Key.Method.ToUpperInvariant(), Id: operation.Value.OperationId!)))
            .ToList();
        var called = _service.Requests.Select(request =>
        {
            var (method, path) = (request.Route.Split(' ')[0], request.Address.AbsolutePath["/stickersnap".Length..]);
            var matching = operations.Where(operation => operation.Method == method && Pattern(operation.Path).IsMatch(path)).ToList();
            matching.Should().ContainSingle($"{request.Route} must be an operation of bot-service.yaml");
            return matching[0].Id;
        }).ToHashSet();

        // The Mini App's operations are signed by Telegram; the desktop never makes them
        var forDesktop = operations.Where(operation => !operation.Path.StartsWith("/v1/miniapp/", StringComparison.Ordinal)).Select(operation => operation.Id);
        called.Should().BeEquivalentTo(forDesktop);
    }

    [Fact]
    public async Task Sends_bodies_of_the_media_type_the_contract_takes()
    {
        var document = await ContractDocuments.BotServiceAsync(Cancel);
        await MakeEveryCallAsync();

        foreach (var request in _service.Requests.Where(request => request.ContentType is not null))
        {
            var path = request.Address.AbsolutePath["/stickersnap".Length..];
            var operation = document.Paths!
                .Where(entry => Pattern(entry.Key).IsMatch(path))
                .SelectMany(entry => entry.Value.Operations!)
                .Single(entry => entry.Key.Method == request.Route.Split(' ')[0]).Value;
            operation.RequestBody!.Content!.Keys.Should().Contain(request.ContentType!, request.Route);
        }
    }

    private async Task MakeEveryCallAsync()
    {
        await _service.LinkAsync(Cancel);
        _service
            .Answer("POST /v1/links", """{"code":"c","deepLink":"https://t.me/b?startapp=link_c","pollToken":"p","expiresAt":"2026-10-18T18:00:00Z"}""")
            .Answer("GET /v1/links/c", """{"status":"pending"}""")
            .Answer("GET /v1/device", """{"device":{"id":"d","name":"n","os":"linux","linkedAt":"2026-10-18T18:00:00Z"},"user":{"id":1,"firstName":"f","botBlocked":false}}""")
            .Answer("POST /v1/feedback", """{"id":1}""", HttpStatusCode.Created)
            .Answer("POST /v1/stickers/upload", """{"id":"f"}""", HttpStatusCode.Created)
            .Answer("POST /v1/packs", PackJson, HttpStatusCode.Created)
            .Answer($"GET /v1/packs/{Pack}", PackJson)
            .Answer($"POST /v1/packs/{Pack}/stickers", PackJson)
            .Answer($"PUT /v1/packs/{Pack}/stickers/s", PackJson)
            .Answer("GET /v1/stickers/s/file", () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1]) });

        var client = _service.Client();
        var sticker = new InputSticker("f", ["😴"]);
        await client.CreateLinkAsync(new LinkRequestBody("n", DeviceOs.Linux, Guid.NewGuid(), null, null), Cancel);
        await client.GetLinkStateAsync("c", "p", TimeSpan.FromSeconds(25), Cancel);
        await client.CancelLinkAsync("c", "p", Cancel);
        await client.GetDeviceAsync(CloudServiceStub.Key, Cancel);
        await client.UnlinkDeviceAsync(CloudServiceStub.Key, Cancel);
        await client.SendFeedbackAsync(new FeedbackRequest(FeedbackType.Idea, "t", false, null, Guid.NewGuid(), null, null, null, null), null, Cancel);
        using (var webm = new MemoryStream([0x1A, 0x45, 0xDF, 0xA3])) await client.UploadStickerAsync(webm, Cancel);
        await client.CreatePackAsync(new NewPackBody(Pack, "T", CloudPackKind.Stickers, [sticker]), Cancel);
        await client.GetPackAsync(Pack, Cancel);
        await client.AddStickerAsync(Pack, sticker, Cancel);
        await client.ReplaceStickerAsync(Pack, "s", sticker, Cancel);
        await client.RemoveStickerAsync(Pack, "s", Cancel);
        using (var file = new MemoryStream()) await client.DownloadStickerAsync("s", file, Cancel);
    }

    private static async Task<IOpenApiSchema> SchemaAsync(string name) =>
        (await ContractDocuments.BotServiceAsync(Cancel)).Components!.Schemas![name];

    /// <summary>/v1/packs/{name}/stickers → ^/v1/packs/[^/]+/stickers$</summary>
    private static Regex Pattern(string template) =>
        new($"^{Regex.Replace(Regex.Escape(template), @"\\\{[^}]+}", "[^/]+")}$");
}
