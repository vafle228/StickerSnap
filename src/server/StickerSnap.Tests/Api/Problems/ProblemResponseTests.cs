using System.Net;
using System.Text.Json.Nodes;
using StickerSnap.Application.Media;
using StickerSnap.Core.Errors;

namespace StickerSnap.Tests.Api.Problems;

/// <summary>Whatever goes wrong, the API answers with the contract's Problem.</summary>
public sealed class ProblemResponseTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static async Task<JsonNode> ProblemOf(HttpResponseMessage response)
    {
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync(Cancel))!;
        problem["status"]!.GetValue<int>().Should().Be((int)response.StatusCode);
        problem["traceId"]!.GetValue<string>().Should().NotBeEmpty();
        return problem;
    }

    [Fact]
    public async Task Answers_an_api_path_without_a_handler_with_bad_request()
    {
        using var factory = new ApiFactory();

        var response = await factory.CreateClient().GetAsync("/api/v1/zzz", Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await ProblemOf(response);
        problem["code"]!.GetValue<string>().Should().Be("bad_request");
        problem["type"]!.GetValue<string>().Should().Be("https://stickersnap.app/problems/bad_request");
        problem["detail"]!.GetValue<string>().Should().Be("No endpoint for GET /api/v1/zzz");
        problem["reportable"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public async Task Answers_a_thrown_app_exception_with_its_code_status_and_params()
    {
        using var factory = new ApiFactory();
        factory.Engine.GetInfoAsync(Arg.Any<CancellationToken>()).Returns<Task<MediaEngineInfo>>(
            _ => throw new AppException(ErrorCode.RateLimited, "Too Many Requests: retry after 24")
            {
                Params = new Dictionary<string, object> { ["retryAfterSec"] = 24 },
            });

        var response = await factory.CreateClient().GetAsync("/api/v1/app", Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        var problem = await ProblemOf(response);
        problem["code"]!.GetValue<string>().Should().Be("rate_limited");
        problem["params"]!.ToJsonString().Should().Be("""{"retryAfterSec":24}""");
        problem["reportable"]!.GetValue<bool>().Should().BeFalse("the user waits out a limit alone");
    }

    [Fact]
    public async Task Answers_an_unexpected_exception_with_internal_and_keeps_its_message_in_the_log()
    {
        using var factory = new ApiFactory();
        factory.Engine.GetInfoAsync(Arg.Any<CancellationToken>()).Returns<Task<MediaEngineInfo>>(
            _ => throw new InvalidOperationException("secret path C:\\Users\\someone"));

        var response = await factory.CreateClient().GetAsync("/api/v1/app", Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var problem = await ProblemOf(response);
        problem["code"]!.GetValue<string>().Should().Be("internal");
        problem["reportable"]!.GetValue<bool>().Should().BeTrue();
        problem.ToJsonString().Should().NotContain("secret path");
    }

    [Theory]
    [InlineData("evil.example")]
    [InlineData("192.168.1.10:5180")]
    public async Task Refuses_a_request_for_a_host_other_than_this_machine(string host)
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Host = host;

        var response = await client.GetAsync("/api/v1/app", Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("127.0.0.1:5180")]
    [InlineData("localhost:5173")]
    public async Task Serves_a_request_for_this_machine(string host)
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Host = host;

        var response = await client.GetAsync("/api/v1/app", Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
