using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using StickerSnap.Api;
using StickerSnap.Application.Links;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Links;
using StickerSnap.Tests.Application.Links;

namespace StickerSnap.Tests.Api;

/// <summary>/api/v1/links/probe and /api/v1/downloads with the sites driven by the test, and downloadUpdated on the hub.</summary>
public sealed class LinksEndpointTests : IDisposable
{
    private const string Cat = "https://www.youtube.com/watch?v=dQw4w9WgXcQ";

    private readonly ApiFactory _factory = new();
    private readonly ControlledSites _sites = new();
    private readonly WebApplicationFactory<Program> _app;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public LinksEndpointTests()
    {
        _sites.Probe = link => Videos.Of(link.Url, bytes: 1000) with
        {
            ThumbnailUrl = "https://i.ytimg.com/vi/dQw4w9WgXcQ/hqdefault.jpg",
        };
        _app = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<IVideoSites>(_sites)));
    }

    public void Dispose() => _factory.Dispose();

    private static async Task<JsonNode> Json(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(Cancel))!;

    [Fact]
    public async Task Probes_the_one_video_of_a_link()
    {
        var response = await _app.CreateClient().PostAsJsonAsync("/api/v1/links/probe", new { url = "youtu.be/dQw4w9WgXcQ?t=3" }, Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonNode.DeepEquals(await Json(response), JsonNode.Parse(
            """{"site":"youtube","url":"https://www.youtube.com/watch?v=dQw4w9WgXcQ","title":"Кот смотрит в окно","durationSec":48,"thumbnailUrl":"https://i.ytimg.com/vi/dQw4w9WgXcQ/hqdefault.jpg","bytes":1000,"quality":"720p"}"""))
            .Should().BeTrue();
    }

    [Theory]
    [InlineData("котик", 422, "link_unsupported", "not_link")]
    [InlineData("https://vimeo.com/1", 422, "link_unsupported", "site")]
    [InlineData("https://www.youtube.com/playlist?list=PLx", 422, "video_unavailable", "playlist")]
    public async Task Answers_a_bad_link_with_its_reason(string url, int status, string code, string reason)
    {
        var response = await _app.CreateClient().PostAsJsonAsync("/api/v1/links/probe", new { url }, Cancel);

        ((int)response.StatusCode).Should().Be(status);
        var problem = await Json(response);
        problem["code"]!.GetValue<string>().Should().Be(code);
        problem["params"]!["reason"]!.GetValue<string>().Should().Be(reason);
        problem["reportable"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public async Task Answers_site_unreachable_with_its_site()
    {
        _sites.Probe = _ => throw new AppException(ErrorCode.SiteUnreachable, "offline")
        {
            Params = new Dictionary<string, object> { ["site"] = "youtube" },
        };

        var response = await _app.CreateClient().PostAsJsonAsync("/api/v1/links/probe", new { url = Cat }, Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await Json(response))["params"]!["site"]!.GetValue<string>().Should().Be("youtube");
    }

    [Fact]
    public async Task Starts_lists_and_cancels_a_download_and_tells_the_hub()
    {
        var client = _app.CreateClient();
        await using var hub = await HubAsync(_app.Server);

        var created = await client.PostAsJsonAsync("/api/v1/downloads", new { url = Cat }, Cancel);
        var download = await Json(created);
        var id = download["id"]!.GetValue<string>();
        await _sites.NextAsync();

        created.StatusCode.Should().Be(HttpStatusCode.Accepted);
        download["video"]!["url"]!.GetValue<string>().Should().Be(Cat);
        download["receivedBytes"]!.GetValue<long>().Should().Be(0);
        var listed = await client.GetFromJsonAsync<JsonArray>("/api/v1/downloads", Cancel);
        listed!.Select(d => d!["id"]!.GetValue<string>()).Should().Equal(id);

        var canceled = await client.PostAsync($"/api/v1/downloads/{id}/cancel", null, Cancel);

        canceled.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(canceled))["state"]!.GetValue<string>().Should().Be("canceled");
        (await hub.UntilAsync("canceled")).GetProperty("id").GetString().Should().Be(id);
    }

    [Fact]
    public async Task Fails_a_download_with_a_problem_named_by_its_id()
    {
        var client = _app.CreateClient();
        await using var hub = await HubAsync(_app.Server);
        await client.PostAsJsonAsync("/api/v1/downloads", new { url = Cat }, Cancel);

        (await _sites.NextAsync()).Throw(new AppException(ErrorCode.SiteChanged, "yt-dlp failed")
        {
            Params = new Dictionary<string, object> { ["site"] = "youtube" },
            Log = "ERROR: Unable to extract",
        });
        var failed = await hub.UntilAsync("failed");

        var error = failed.GetProperty("error");
        error.GetProperty("code").GetString().Should().Be("site_changed");
        error.GetProperty("status").GetInt32().Should().Be(502);
        error.GetProperty("reportable").GetBoolean().Should().BeTrue();
        error.GetProperty("log").GetString().Should().Be("ERROR: Unable to extract");
        error.GetProperty("traceId").GetString().Should().Be(failed.GetProperty("id").GetString());
    }

    [Fact]
    public async Task Answers_job_not_found_for_a_download_it_does_not_know()
    {
        var response = await _app.CreateClient().PostAsync("/api/v1/downloads/nope/cancel", null, Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Json(response))["code"]!.GetValue<string>().Should().Be("job_not_found");
    }

    private static async Task<DownloadUpdates> HubAsync(TestServer server)
    {
        var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, "hubs/events"), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
            })
            .Build();
        var updates = new DownloadUpdates(hub);
        hub.On<JsonElement>("downloadUpdated", updates.Add);
        await hub.StartAsync(Cancel);
        return updates;
    }

    /// <summary>The downloadUpdated events of the hub, and a way to await one in a state.</summary>
    private sealed class DownloadUpdates(HubConnection hub) : IAsyncDisposable
    {
        private readonly Lock _gate = new();
        private readonly List<JsonElement> _received = [];
        private readonly List<(string State, TaskCompletionSource<JsonElement> Found)> _awaited = [];

        public Task<JsonElement> UntilAsync(string state)
        {
            lock (_gate)
            {
                if (_received.FirstOrDefault(d => d.GetProperty("state").GetString() == state) is { ValueKind: JsonValueKind.Object } sent)
                    return Task.FromResult(sent);
                var found = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
                _awaited.Add((state, found));
                return found.Task.WaitAsync(TimeSpan.FromSeconds(10), Cancel);
            }
        }

        public void Add(JsonElement download)
        {
            lock (_gate)
            {
                _received.Add(download);
                foreach (var awaited in _awaited.Where(a => a.State == download.GetProperty("state").GetString()).ToList())
                {
                    awaited.Found.TrySetResult(download);
                    _awaited.Remove(awaited);
                }
            }
        }

        public ValueTask DisposeAsync() => hub.DisposeAsync();
    }
}
