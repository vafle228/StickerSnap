using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using StickerSnap.Application.Feedback;
using StickerSnap.Core.Errors;

namespace StickerSnap.Tests.Api;

/// <summary>/api/v1/feedback with the service's inbox substituted, and once as the app has it without a service.</summary>
public sealed class FeedbackEndpointTests : IDisposable
{
    private readonly ApiFactory _factory = new();
    private readonly IFeedbackInbox _inbox = Substitute.For<IFeedbackInbox>();
    private FeedbackLetter? _sent;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public FeedbackEndpointTests() =>
        _inbox.SendAsync(Arg.Do<FeedbackLetter>(letter => _sent = letter), Arg.Any<CancellationToken>()).Returns(41L);

    public void Dispose() => _factory.Dispose();

    private HttpClient Client() => _factory
        .WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton(_inbox)))
        .CreateClient();

    private static async Task<JsonNode> Json(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(Cancel))!;

    [Fact]
    public async Task Sends_an_idea_and_answers_with_its_number()
    {
        var response = await Client().PostAsJsonAsync(
            "/api/v1/feedback", new { type = "idea", text = "Обрезать на таймлайне", canReply = true }, Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await response.Content.ReadAsStringAsync(Cancel)).Should().Be("""{"id":41}""");
        _sent.Should().Be(new FeedbackLetter(FeedbackKind.Idea, "Обрезать на таймлайне", true));
    }

    [Fact]
    public async Task Sends_a_bug_with_the_report_the_server_collects()
    {
        var response = await Client().PostAsJsonAsync(
            "/api/v1/feedback",
            new { type = "bug", text = "", canReply = true, contact = "@dima", error = new { code = "render_failed", traceId = "7f3a-c21e" } },
            Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        _sent!.Kind.Should().Be(FeedbackKind.Bug);
        _sent.Contact.Should().Be("@dima");
        _sent.Error.Should().Be(new FeedbackError("render_failed", "7f3a-c21e"));
        _sent.Diagnostics.Should().StartWith("StickerSnap ").And.Contain("Error: render_failed, trace 7f3a-c21e");
    }

    [Fact]
    public async Task Refuses_an_empty_idea_with_the_field()
    {
        var response = await Client().PostAsJsonAsync("/api/v1/feedback", new { type = "idea", text = " ", canReply = false }, Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await Json(response);
        problem["code"]!.GetValue<string>().Should().Be("bad_request");
        problem["fields"]!["text"]![0]!.GetValue<string>().Should().Be("empty");
    }

    [Fact]
    public async Task Passes_on_the_limit_of_the_service()
    {
        _inbox.SendAsync(Arg.Any<FeedbackLetter>(), Arg.Any<CancellationToken>())
            .Returns<long>(_ => throw new AppException(ErrorCode.RateLimited, "5 an hour")
            {
                Params = new Dictionary<string, object> { ["retryAfterSec"] = 1200 },
            });

        var response = await Client().PostAsJsonAsync("/api/v1/feedback", new { type = "other", text = "Привет", canReply = false }, Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await Json(response))["params"]!["retryAfterSec"]!.GetValue<int>().Should().Be(1200);
    }

    [Fact]
    public async Task Says_the_service_is_unavailable_when_the_app_has_no_address_of_it()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync(
            "/api/v1/feedback", new { type = "idea", text = "Идея", canReply = false }, Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await Json(response))["code"]!.GetValue<string>().Should().Be("bot_unavailable");
    }
}
