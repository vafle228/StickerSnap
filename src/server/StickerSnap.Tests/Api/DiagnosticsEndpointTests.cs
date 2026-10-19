using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace StickerSnap.Tests.Api;

/// <summary>/api/v1/diagnostics on the whole server: an answered error is found by its trace id, with the log around it.</summary>
public sealed class DiagnosticsEndpointTests : IDisposable
{
    private readonly ApiFactory _factory = new();

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public void Dispose() => _factory.Dispose();

    private static async Task<JsonNode> Json(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(Cancel))!;

    [Fact]
    public async Task Finds_an_answered_error_by_its_trace_with_the_route_and_the_server_s_log()
    {
        var client = _factory.CreateClient();
        var refused = await client.PostAsJsonAsync(
            "/api/v1/feedback", new { type = "idea", text = new string('a', 2001), canReply = false }, Cancel);
        var problem = await Json(refused);
        problem["reportable"]!.GetValue<bool>().Should().BeTrue();
        var trace = problem["traceId"]!.GetValue<string>();

        var response = await client.GetAsync($"/api/v1/diagnostics?traceId={Uri.EscapeDataString(trace)}", Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var report = await Json(response);
        var text = report["text"]!.GetValue<string>();
        report["bytes"]!.GetValue<int>().Should().Be(System.Text.Encoding.UTF8.GetByteCount(text));
        text.Should().StartWith("StickerSnap ").And.Contain($"Error: trace {trace}\n");
        text.Should().Contain("\n== bad_request at ").And.Contain("Operation: POST /api/v1/feedback\nMessage: Feedback is wrong in text\n");
        text.Should().Contain("== Server log since ")
            .And.Contain($"warn ErrorJournal [{trace}] bad_request in POST /api/v1/feedback, trace {trace}");
        text.Should().NotContain(_factory.DataDir);
    }

    [Fact]
    public async Task Answers_a_trace_it_does_not_know_with_a_report_that_says_so()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/diagnostics?traceId=lost-1", Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(response))["text"]!.GetValue<string>().Should().Contain("Trace lost-1 is not in memory");
    }

    [Fact]
    public async Task Refuses_a_trace_id_over_64_characters_with_the_field()
    {
        var response = await _factory.CreateClient().GetAsync($"/api/v1/diagnostics?traceId={new string('a', 65)}", Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Json(response))["fields"]!["traceId"]![0]!.GetValue<string>().Should().Be("too_long");
    }
}
