using System.Net;
using System.Text.Json;
using StickerSnap.Application.Account;
using StickerSnap.Application.App;
using StickerSnap.Core.Account;
using StickerSnap.Core.Errors;
using StickerSnap.Infrastructure.Cloud;

namespace StickerSnap.Tests.Infrastructure.Cloud;

/// <summary>Linking against a stub of the service: the code, the answers, and the key kept or forgotten.</summary>
public sealed class CloudDeviceLinkingTests : IDisposable
{
    private static readonly LinkInvite Invite = new(
        "code-1", "https://t.me/StickerSnapAppBot?startapp=link_code-1", DateTimeOffset.Parse("2026-10-18T18:00:00Z"), "poll-1");

    private const string DeviceJson = """
        "device":{"id":"dev-1","name":"DESKTOP-7F3K2Q","os":"windows","linkedAt":"2026-10-18T17:51:00Z"},
        "user":{"id":42,"firstName":"Дима","lastName":"К","username":"dmitriu","botBlocked":false}
        """;

    private static readonly LinkedDevice Linked = new(
        "dev-1", "DESKTOP-7F3K2Q", DateTimeOffset.Parse("2026-10-18T17:51:00Z"), new LinkedUser(42, "Дима", "dmitriu"), BotBlocked: false);

    private readonly CloudServiceStub _service = new();

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public void Dispose() => _service.Dispose();

    private CloudDeviceLinking Linking()
    {
        var host = Substitute.For<IHostInfo>();
        host.AppVersion.Returns("0.3.1");
        return new CloudDeviceLinking(_service.Client(), _service.Device, new ThisComputer(host));
    }

    [Fact]
    public async Task Asks_for_a_code_as_this_installation_and_this_computer()
    {
        _service.Answer("POST /v1/links", """
            {"code":"code-1","deepLink":"https://t.me/StickerSnapAppBot?startapp=link_code-1","pollToken":"poll-1","expiresAt":"2026-10-18T18:00:00Z"}
            """, HttpStatusCode.Created);

        var invite = await Linking().InviteAsync(Cancel);

        invite.Should().Be(Invite);
        var body = JsonDocument.Parse(_service.Single("POST /v1/links").Body).RootElement;
        body.GetProperty("installationId").GetGuid().Should().Be(await _service.Device.GetInstallationIdAsync(Cancel));
        body.GetProperty("name").GetString().Should().Be(Environment.MachineName);
        body.GetProperty("os").GetString().Should().BeOneOf("windows", "macos", "linux");
        body.GetProperty("appVersion").GetString().Should().Be("0.3.1");
        body.GetProperty("platform").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Waits_while_the_user_has_not_answered()
    {
        _service.Answer("GET /v1/links/code-1", """{"status":"pending","expiresAt":"2026-10-18T17:59:00Z"}""");

        var answer = await Linking().WaitAsync(Invite, TimeSpan.FromSeconds(25), Cancel);

        answer.Should().Be(new LinkAnswer.Waiting(DateTimeOffset.Parse("2026-10-18T17:59:00Z")));
    }

    [Fact]
    public async Task Keeps_the_key_once_the_user_links_the_computer()
    {
        _service.Answer("GET /v1/links/code-1", $$"""{"status":"linked","deviceKey":"dk_1",{{DeviceJson}}}""");

        var answer = await Linking().WaitAsync(Invite, TimeSpan.FromSeconds(25), Cancel);

        answer.Should().Be(new LinkAnswer.Linked(Linked));
        (await _service.Device.GetKeyAsync(Cancel)).Should().Be("dk_1");
    }

    [Theory]
    [InlineData("declined")]
    [InlineData("expired")]
    public async Task Keeps_no_key_when_the_code_is_not_linked(string status)
    {
        _service.Answer("GET /v1/links/code-1", $$"""{"status":"{{status}}"}""");

        var answer = await Linking().WaitAsync(Invite, TimeSpan.FromSeconds(25), Cancel);

        answer.Should().Be(status == "declined" ? new LinkAnswer.Declined() : new LinkAnswer.Expired());
        (await _service.Device.GetKeyAsync(Cancel)).Should().BeNull();
    }

    [Fact]
    public async Task Calls_a_code_the_service_no_longer_knows_expired()
    {
        _service.Refuse("GET /v1/links/code-1", 404, "link_not_found");

        (await Linking().WaitAsync(Invite, TimeSpan.Zero, Cancel)).Should().Be(new LinkAnswer.Expired());
    }

    [Fact]
    public async Task Calls_a_link_without_its_key_an_unavailable_service()
    {
        _service.Answer("GET /v1/links/code-1", $$"""{"status":"linked",{{DeviceJson}}}""");

        var waiting = () => Linking().WaitAsync(Invite, TimeSpan.Zero, Cancel);

        (await waiting.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.BotUnavailable);
    }

    [Fact]
    public async Task Cancels_a_code_even_one_already_gone()
    {
        _service.Refuse("DELETE /v1/links/code-1", 404, "link_not_found");

        await Linking().CancelAsync(Invite, Cancel);

        _service.Single("DELETE /v1/links/code-1").Authorization.Should().Be("Bearer poll-1");
    }

    [Fact]
    public async Task Asks_the_service_nothing_while_the_computer_is_not_linked()
    {
        (await Linking().GetAsync(Cancel)).Should().BeNull();
        await Linking().UnlinkAsync(Cancel);

        _service.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Tells_whom_the_computer_is_linked_to()
    {
        await _service.LinkAsync(Cancel);
        _service.Answer("GET /v1/device", $$"""{{{DeviceJson}}}""");

        (await Linking().GetAsync(Cancel)).Should().Be(Linked);
        _service.Single("GET /v1/device").Authorization.Should().Be($"Bearer {CloudServiceStub.Key}");
    }

    [Fact]
    public async Task Forgets_a_key_unlinked_from_the_phone()
    {
        await _service.LinkAsync(Cancel);
        _service.Refuse("GET /v1/device", 401, "unauthorized");

        (await Linking().GetAsync(Cancel)).Should().BeNull();
        (await _service.Device.GetKeyAsync(Cancel)).Should().BeNull();
    }

    [Fact]
    public async Task Keeps_the_key_while_the_service_cannot_be_reached()
    {
        await _service.LinkAsync(Cancel);
        _service.Throw("GET /v1/device", new HttpRequestException("No route to host"));

        var checking = () => Linking().GetAsync(Cancel);

        (await checking.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.NetworkUnavailable);
        (await _service.Device.GetKeyAsync(Cancel)).Should().Be(CloudServiceStub.Key);
    }

    [Theory]
    [InlineData(204)]
    [InlineData(401)]
    public async Task Unlinks_and_forgets_the_key_even_one_revoked_already(int status)
    {
        await _service.LinkAsync(Cancel);
        if (status == 401) _service.Refuse("DELETE /v1/device", 401, "unauthorized");

        await Linking().UnlinkAsync(Cancel);

        _service.Single("DELETE /v1/device").Authorization.Should().Be($"Bearer {CloudServiceStub.Key}");
        (await _service.Device.GetKeyAsync(Cancel)).Should().BeNull();
    }
}
