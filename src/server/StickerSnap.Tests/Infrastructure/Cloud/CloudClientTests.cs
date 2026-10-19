using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Packs;
using StickerSnap.Infrastructure.Cloud;

namespace StickerSnap.Tests.Infrastructure.Cloud;

/// <summary>How the client reaches the service, and how a refusal reaches the SPA: as a code of the desktop's contract.</summary>
public sealed class CloudClientTests : IDisposable
{
    private const string Pack = "reactions_call_by_StickerSnapAppBot";

    private const string PackJson = """
        {"name":"reactions_call_by_StickerSnapAppBot","title":"Реакции с созвона","kind":"stickers",
         "stickers":[{"id":"CAAC-1","uniqueId":"AgAD-1","emoji":["😴","💤"]},{"id":"CAAC-2","uniqueId":"AgAD-2","emoji":[]}]}
        """;

    private readonly CloudServiceStub _service = new();

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public void Dispose() => _service.Dispose();

    public static TheoryData<int, string, ErrorCode> Refusals => new()
    {
        { 401, "unauthorized", ErrorCode.AccountNotLinked },
        { 404, "device_not_found", ErrorCode.AccountNotLinked },
        { 403, "bot_blocked", ErrorCode.BotBlocked },
        { 404, "pack_not_found", ErrorCode.PackNotFound },
        { 404, "sticker_not_found", ErrorCode.TelegramError },
        { 409, "pack_name_taken", ErrorCode.PackNameTaken },
        { 409, "pack_full", ErrorCode.PackFull },
        { 422, "pack_name_invalid", ErrorCode.PackNameInvalid },
        { 422, "validation_failed", ErrorCode.ValidationFailed },
        { 502, "telegram_error", ErrorCode.TelegramError },
        { 500, "internal", ErrorCode.BotUnavailable },
        { 400, "bad_request", ErrorCode.Internal },
        { 409, "pack_in_telegram", ErrorCode.Internal },
        { 418, "a_code_of_tomorrow", ErrorCode.Internal },
        { 503, "a_code_of_tomorrow", ErrorCode.BotUnavailable },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task Turns_a_refusal_into_the_desktops_code_with_the_services_words_in_the_log(int status, string code, ErrorCode expected)
    {
        await _service.LinkAsync(Cancel);
        _service.Refuse($"POST /v1/packs/{Pack}/stickers", status, code);

        var error = await ErrorOf(() => _service.Client().AddStickerAsync(Pack, new InputSticker("file-1", ["😴"]), Cancel));

        error.Code.Should().Be(expected);
        error.Log.Should().Be($"{status} {code}: Said the service");
        error.Message.Should().Contain("addSticker");
        error.Context["method"].Should().Be("addSticker");
        error.Context["status"].Should().Be(status.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Says_how_long_to_wait_as_the_problem_says()
    {
        await _service.LinkAsync(Cancel);
        _service.Refuse("POST /v1/packs/reactions_call_by_StickerSnapAppBot/stickers", 429, "rate_limited", """{"retryAfterSec":24}""", retryAfter: 30);

        var error = await ErrorOf(() => _service.Client().AddStickerAsync(Pack, new InputSticker("file-1", ["😴"]), Cancel));

        error.Code.Should().Be(ErrorCode.RateLimited);
        error.Params.Should().Equal(new Dictionary<string, object> { ["retryAfterSec"] = 24 });
    }

    [Fact]
    public async Task Says_how_long_to_wait_by_the_header_when_the_problem_does_not()
    {
        _service.Refuse("POST /v1/links", 429, "rate_limited", retryAfter: 90);

        var error = await ErrorOf(() => _service.Client().CreateLinkAsync(LinkBody(), Cancel));

        error.Params.Should().Equal(new Dictionary<string, object> { ["retryAfterSec"] = 90 });
    }

    [Fact]
    public async Task Calls_a_proxys_page_while_the_service_is_down_an_unavailable_service()
    {
        _service.Answer("POST /v1/links", () => new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("<html>502 Bad Gateway</html>", Encoding.UTF8, "text/html"),
        });

        var error = await ErrorOf(() => _service.Client().CreateLinkAsync(LinkBody(), Cancel));

        error.Code.Should().Be(ErrorCode.BotUnavailable);
        error.Log.Should().Be("HTTP 502");
    }

    [Fact]
    public async Task Calls_a_success_without_the_services_json_an_unavailable_service()
    {
        _service.Answer("POST /v1/links", () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>Welcome to nginx</html>", Encoding.UTF8, "text/html"),
        });

        var error = await ErrorOf(() => _service.Client().CreateLinkAsync(LinkBody(), Cancel));

        error.Code.Should().Be(ErrorCode.BotUnavailable);
    }

    [Fact]
    public async Task Calls_a_failed_connection_a_missing_network()
    {
        _service.Throw("POST /v1/links", new HttpRequestException("No such host is known. (cloud.test:443)"));

        var error = await ErrorOf(() => _service.Client().CreateLinkAsync(LinkBody(), Cancel));

        error.Code.Should().Be(ErrorCode.NetworkUnavailable);
        error.Log.Should().Be("No such host is known. (cloud.test:443)");
    }

    [Fact]
    public async Task Calls_a_request_that_timed_out_a_missing_network()
    {
        _service.Throw("POST /v1/links", new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"));

        var error = await ErrorOf(() => _service.Client().CreateLinkAsync(LinkBody(), Cancel));

        error.Code.Should().Be(ErrorCode.NetworkUnavailable);
    }

    [Fact]
    public async Task Lets_a_cancellation_by_the_caller_be_one()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        _service.Throw("POST /v1/links", new TaskCanceledException());

        var calling = () => _service.Client().CreateLinkAsync(LinkBody(), cancelled.Token);

        await calling.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Calls_a_build_without_the_services_address_an_unavailable_service()
    {
        var client = new CloudClient(
            new HttpClient(_service, disposeHandler: false),
            Microsoft.Extensions.Options.Options.Create(new CloudOptions()),
            _service.Device);

        var error = await ErrorOf(() => client.CreateLinkAsync(LinkBody(), Cancel));

        error.Code.Should().Be(ErrorCode.BotUnavailable);
        _service.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Does_not_publish_before_the_computer_is_linked()
    {
        var error = await ErrorOf(() => _service.Client().GetPackAsync(Pack, Cancel));

        error.Code.Should().Be(ErrorCode.AccountNotLinked);
        _service.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Publishes_with_the_computers_key_and_no_bot_token()
    {
        await _service.LinkAsync(Cancel);
        _service.Answer($"GET /v1/packs/{Pack}", PackJson);

        await _service.Client().GetPackAsync(Pack, Cancel);

        var request = _service.Single($"GET /v1/packs/{Pack}");
        request.Authorization.Should().Be($"Bearer {CloudServiceStub.Key}");
        request.Address.Should().Be(new Uri($"https://cloud.test/stickersnap/v1/packs/{Pack}"));
    }

    [Fact]
    public async Task Reads_a_pack_as_the_domain_has_it()
    {
        await _service.LinkAsync(Cancel);
        _service.Answer($"GET /v1/packs/{Pack}", PackJson);

        var pack = (await _service.Client().GetPackAsync(Pack, Cancel))!.ToPublishedPack();

        pack.Should().Be(new PublishedPack(Pack, "Реакции с созвона", PackKind.Regular, pack.Stickers));
        pack.Stickers.Should().Equal(new PublishedSticker("CAAC-1", "AgAD-1", "😴"), new PublishedSticker("CAAC-2", "AgAD-2", null));
    }

    [Fact]
    public async Task Creates_a_pack_with_the_body_of_the_contract()
    {
        await _service.LinkAsync(Cancel);
        _service.Answer("POST /v1/packs", PackJson, HttpStatusCode.Created);

        await _service.Client().CreatePackAsync(
            new NewPackBody(Pack, "Реакции с созвона", CloudPackKind.Emoji, [new InputSticker("file-1", ["😴", "💤"])]),
            Cancel);

        var request = _service.Single("POST /v1/packs");
        request.ContentType.Should().Be("application/json");
        var expected = JsonNode.Parse(
            """{"name":"reactions_call_by_StickerSnapAppBot","title":"Реакции с созвона","kind":"emoji","stickers":[{"file":"file-1","emoji":["😴","💤"]}]}""");
        JsonNode.DeepEquals(JsonNode.Parse(request.Body), expected).Should().BeTrue(request.Body);
    }

    [Fact]
    public async Task Uploads_the_webm_as_it_is()
    {
        await _service.LinkAsync(Cancel);
        _service.Answer("POST /v1/stickers/upload", """{"id":"file-1"}""", HttpStatusCode.Created);
        using var webm = new MemoryStream("\u001aEß£webm"u8.ToArray());

        var uploaded = await _service.Client().UploadStickerAsync(webm, Cancel);

        uploaded.Should().Be(new UploadedSticker("file-1"));
        _service.Single("POST /v1/stickers/upload").ContentType.Should().Be("video/webm");
    }

    [Fact]
    public async Task Writes_a_downloaded_sticker_into_the_stream()
    {
        await _service.LinkAsync(Cancel);
        _service.Answer("GET /v1/stickers/CAAC-1/file", () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([1, 2, 3]),
        });
        using var destination = new MemoryStream();

        await _service.Client().DownloadStickerAsync("CAAC-1", destination, Cancel);

        destination.ToArray().Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task Escapes_what_goes_into_a_path()
    {
        await _service.LinkAsync(Cancel);

        await _service.Client().RemoveStickerAsync(Pack, "CAAC/1?x", Cancel);

        _service.Requests.Should().ContainSingle().Which.Address.AbsolutePath
            .Should().Be($"/stickersnap/v1/packs/{Pack}/stickers/CAAC%2F1%3Fx");
    }

    [Fact]
    public async Task Waits_for_a_link_no_longer_than_the_service_holds_a_request()
    {
        _service.Answer("GET /v1/links/code-1", """{"status":"pending","expiresAt":"2026-10-18T18:00:00Z"}""");

        await _service.Client().GetLinkStateAsync("code-1", "poll-1", TimeSpan.FromMinutes(1), Cancel);

        var request = _service.Single("GET /v1/links/code-1");
        request.Address.Query.Should().Be("?wait=25");
        request.Authorization.Should().Be("Bearer poll-1");
    }

    private static LinkRequestBody LinkBody() =>
        new("DESKTOP-7F3K2Q", DeviceOs.Windows, Guid.Parse("6f1c2a52-6a9f-4a51-9a51-0c1d3e7a2b11"), "Windows 11", "0.3.1");

    private static async Task<AppException> ErrorOf(Func<Task> call) =>
        (await call.Should().ThrowAsync<AppException>()).Which;
}
