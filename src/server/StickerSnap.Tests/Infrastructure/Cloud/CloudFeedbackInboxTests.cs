using System.Net;
using System.Text.Json;
using StickerSnap.Application.App;
using StickerSnap.Application.Feedback;
using StickerSnap.Infrastructure.Cloud;

namespace StickerSnap.Tests.Infrastructure.Cloud;

/// <summary>Feedback against a stub of the service, from a computer linked and not.</summary>
public sealed class CloudFeedbackInboxTests : IDisposable
{
    private static readonly FeedbackLetter Bug = new(
        FeedbackKind.Bug, "", CanReply: true, Contact: "me@example.com", new FeedbackError("render_failed", "7f3a-c21e"), "log lines");

    private readonly CloudServiceStub _service = new();

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public CloudFeedbackInboxTests() => _service.Answer("POST /v1/feedback", """{"id":41}""", HttpStatusCode.Created);

    public void Dispose() => _service.Dispose();

    private CloudFeedbackInbox Inbox()
    {
        var host = Substitute.For<IHostInfo>();
        host.AppVersion.Returns("0.3.1");
        return new CloudFeedbackInbox(_service.Client(), _service.Device, new ThisComputer(host));
    }

    [Fact]
    public async Task Sends_feedback_without_a_link_with_the_installation_and_the_contact()
    {
        var id = await Inbox().SendAsync(Bug, Cancel);

        id.Should().Be(41);
        var request = _service.Single("POST /v1/feedback");
        request.Authorization.Should().BeNull();
        var body = JsonDocument.Parse(request.Body).RootElement;
        body.GetProperty("type").GetString().Should().Be("bug");
        body.GetProperty("text").GetString().Should().BeEmpty();
        body.GetProperty("canReply").GetBoolean().Should().BeTrue();
        body.GetProperty("contact").GetString().Should().Be("me@example.com");
        body.GetProperty("installationId").GetGuid().Should().Be(await _service.Device.GetInstallationIdAsync(Cancel));
        body.GetProperty("appVersion").GetString().Should().Be("0.3.1");
        body.GetProperty("error").GetRawText().Should().Be("""{"code":"render_failed","traceId":"7f3a-c21e"}""");
        body.GetProperty("diagnostics").GetString().Should().Be("log lines");
    }

    [Fact]
    public async Task Sends_no_contact_the_user_did_not_allow_to_use()
    {
        await Inbox().SendAsync(Bug with { CanReply = false }, Cancel);

        JsonDocument.Parse(_service.Single("POST /v1/feedback").Body).RootElement.TryGetProperty("contact", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Sends_feedback_from_a_linked_computer_with_its_key_and_no_contact()
    {
        await _service.LinkAsync(Cancel);

        await Inbox().SendAsync(new FeedbackLetter(FeedbackKind.Idea, "Тёмная тема", CanReply: true, Contact: "me@example.com"), Cancel);

        var request = _service.Single("POST /v1/feedback");
        request.Authorization.Should().Be($"Bearer {CloudServiceStub.Key}");
        var body = JsonDocument.Parse(request.Body).RootElement;
        body.GetProperty("type").GetString().Should().Be("idea");
        body.GetProperty("text").GetString().Should().Be("Тёмная тема");
        body.TryGetProperty("contact", out _).Should().BeFalse();
        body.TryGetProperty("error", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Sends_it_again_without_the_key_the_phone_has_revoked()
    {
        await _service.LinkAsync(Cancel);
        var answers = new Queue<HttpResponseMessage>([
            new HttpResponseMessage(HttpStatusCode.Unauthorized),
            new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("""{"id":42}""", System.Text.Encoding.UTF8, "application/json") },
        ]);
        _service.Answer("POST /v1/feedback", answers.Dequeue);

        var id = await Inbox().SendAsync(Bug, Cancel);

        id.Should().Be(42);
        _service.Requests.Select(request => request.Authorization).Should().Equal($"Bearer {CloudServiceStub.Key}", null);
        JsonDocument.Parse(_service.Requests[^1].Body).RootElement.GetProperty("contact").GetString().Should().Be("me@example.com");
    }
}
