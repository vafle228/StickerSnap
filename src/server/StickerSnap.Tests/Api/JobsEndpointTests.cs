using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using StickerSnap.Application.Jobs;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Jobs;
using StickerSnap.Tests.Application.Jobs;
using StickerSnap.Tests.Infrastructure.Projects;

namespace StickerSnap.Tests.Api;

/// <summary>
/// /api/v1/jobs and the event jobUpdated over the sample project, with executors the test
/// drives in place of the render engine and the publisher.
/// </summary>
public sealed class JobsEndpointTests : IAsyncLifetime
{
    private const string CatId = "6f1c2a4e-8b3d-4f7a-9c21-5d0e7a3b9f12";

    private readonly DirectoryInfo _files = Directory.CreateTempSubdirectory("stickersnap-jobs-");
    private readonly ApiFactory _factory = new(("Mode", "desktop"));
    private readonly ControlledExecutor _exports = new(JobKind.Export);
    private readonly ControlledExecutor _previews = new(JobKind.Preview);
    private readonly ControlledExecutor _publications = new(JobKind.Publish);
    private WebApplicationFactory<StickerSnap.Api.Program> _server = null!;
    private HttpClient _client = null!;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _server = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services
            .AddSingleton<IJobExecutor>(_exports)
            .AddSingleton<IJobExecutor>(_previews)
            .AddSingleton<IJobExecutor>(_publications)));
        _client = _server.CreateClient();

        var project = JsonNode.Parse(SampleProjects.Text(SampleProjects.SamplePath))!;
        project["source"]!["path"] = Path.Combine(_files.FullName, "cat-keyboard.gif");
        var path = Path.Combine(_files.FullName, "cat.ssproj");
        await File.WriteAllTextAsync(path, project.ToJsonString(), Cancel);
        var opened = await _client.PostAsync("/api/v1/projects/open", Body(new JsonObject { ["path"] = path }), Cancel);
        opened.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    public async ValueTask DisposeAsync()
    {
        await _server.DisposeAsync();
        await _factory.DisposeAsync();
        _files.Delete(recursive: true);
    }

    private static StringContent Body(JsonNode body) => new(body.ToJsonString(), Encoding.UTF8, "application/json");

    private static async Task<JsonNode> Json(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(Cancel))!;

    private static string CodeOf(JsonNode problem) => problem["code"]!.GetValue<string>();

    private static JsonObject Export(int revision = 12, params string[] segments) => new()
    {
        ["kind"] = "export",
        ["projectId"] = CatId,
        ["revision"] = revision,
        ["segmentIds"] = new JsonArray([.. (segments.Length > 0 ? segments : ["seg-1", "seg-2"]).Select(id => JsonValue.Create(id))]),
    };

    private Task<HttpResponseMessage> CreateAsync(JsonNode request) => _client.PostAsync("/api/v1/jobs", Body(request), Cancel);

    private async Task<string> CreatedIdAsync(JsonNode request)
    {
        var response = await CreateAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        return (await Json(response))["id"]!.GetValue<string>();
    }

    /// <summary>A client of /hubs/events that keeps every jobUpdated and can await one.</summary>
    private sealed class JobUpdates(HubConnection hub) : IAsyncDisposable
    {
        private readonly Lock _gate = new();
        private readonly List<JsonElement> _received = [];
        private readonly List<(string State, TaskCompletionSource<JsonElement> Found)> _awaited = [];

        public IReadOnlyList<JsonElement> Received
        {
            get
            {
                lock (_gate) return [.. _received];
            }
        }

        public static async Task<JobUpdates> ConnectAsync(TestServer server)
        {
            var hub = new HubConnectionBuilder()
                .WithUrl(new Uri(server.BaseAddress, "hubs/events"), options =>
                {
                    options.Transports = HttpTransportType.LongPolling;
                    options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                })
                .Build();
            var updates = new JobUpdates(hub);
            hub.On<JsonElement>("jobUpdated", updates.Add);
            await hub.StartAsync(Cancel);
            return updates;
        }

        /// <summary>The first job sent in <paramref name="state"/>; a guard of 10 s fails a test that hangs.</summary>
        public Task<JsonElement> UntilAsync(string state)
        {
            lock (_gate)
            {
                var sent = _received.Where(job => job.GetProperty("state").GetString() == state).ToList();
                if (sent.Count > 0) return Task.FromResult(sent[0]);
                var found = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
                _awaited.Add((state, found));
                return found.Task.WaitAsync(TimeSpan.FromSeconds(10), Cancel);
            }
        }

        private void Add(JsonElement job)
        {
            lock (_gate)
            {
                _received.Add(job);
                foreach (var awaited in _awaited.Where(awaited => awaited.State == job.GetProperty("state").GetString()).ToList())
                {
                    awaited.Found.TrySetResult(job);
                    _awaited.Remove(awaited);
                }
            }
        }

        public ValueTask DisposeAsync() => hub.DisposeAsync();
    }

    [Fact]
    public async Task Starts_an_export_and_tells_the_hub_how_it_goes_to_its_end()
    {
        await using var updates = await JobUpdates.ConnectAsync(_server.Server);

        var response = await CreateAsync(Export());
        var call = await _exports.NextAsync();
        call.Run.Item("seg-1", JobItemState.Exporting, 0.5);
        call.Complete();
        var last = await updates.UntilAsync("succeeded");

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var created = await Json(response);
        created["kind"]!.GetValue<string>().Should().Be("export");
        created["projectId"]!.GetValue<string>().Should().Be(CatId);
        created["state"]!.GetValue<string>().Should().Be("running");
        created["progress"]!.ToJsonString().Should().Be("""{"fraction":0,"stage":"probe"}""");
        created["items"]!.ToJsonString().Should().Be(
            """[{"segmentId":"seg-1","state":"queued","fraction":0},{"segmentId":"seg-2","state":"queued","fraction":0}]""");

        var states = updates.Received.Select(job => job.GetProperty("state").GetString()).ToList();
        states.First().Should().Be("queued");
        states.Last().Should().Be("succeeded");
        last.GetProperty("progress").GetProperty("fraction").GetDouble().Should().Be(1);
        var id = created["id"]!.GetValue<string>();
        (await _client.GetStringAsync($"/api/v1/jobs/{id}", Cancel)).Should().Be(last.GetRawText());
    }

    [Fact]
    public async Task Lists_the_jobs_newest_first()
    {
        var export = await CreatedIdAsync(Export());
        var preview = await CreatedIdAsync(new JsonObject
        {
            ["kind"] = "preview",
            ["projectId"] = CatId,
            ["segment"] = JsonNode.Parse(JsonSerializer.Serialize(SampleSegment())),
        });

        var list = JsonNode.Parse(await _client.GetStringAsync("/api/v1/jobs", Cancel))!.AsArray();

        list.Select(job => job!["id"]!.GetValue<string>()).Should().Equal(preview, export);
    }

    [Fact]
    public async Task Takes_a_publication_to_a_new_pack_and_waits_a_second_one_in_the_queue()
    {
        var publication = new JsonObject
        {
            ["kind"] = "publish",
            ["projectId"] = CatId,
            ["revision"] = 12,
            ["items"] = new JsonArray(new JsonObject { ["segmentId"] = "seg-1", ["emoji"] = new JsonArray("🐱") }),
            ["target"] = new JsonObject { ["kind"] = "new", ["title"] = "Коты", ["name"] = "cats", ["type"] = "regular" },
        };

        var first = await CreateAsync(publication);
        await _publications.NextAsync();
        var second = await CreateAsync(publication.DeepClone());

        first.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await Json(second))["state"]!.GetValue<string>().Should().Be("queued");
    }

    [Fact]
    public async Task Cancels_a_running_job_and_retries_it()
    {
        var id = await CreatedIdAsync(Export());
        await _exports.NextAsync();

        var canceled = await _client.PostAsync($"/api/v1/jobs/{id}/cancel", null, Cancel);
        var retried = await _client.PostAsync($"/api/v1/jobs/{id}/retry", null, Cancel);

        canceled.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(canceled))["state"]!.GetValue<string>().Should().Be("canceled");
        retried.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await Json(retried))["state"]!.GetValue<string>().Should().BeOneOf("queued", "running");
    }

    [Fact]
    public async Task Shows_why_a_job_failed_as_a_problem_with_the_job_s_trace_id()
    {
        await using var updates = await JobUpdates.ConnectAsync(_server.Server);
        var id = await CreatedIdAsync(Export());
        (await _exports.NextAsync()).Throw(new AppException(ErrorCode.DiskFull, "No space left on device") { Log = "ENOSPC" });

        await updates.UntilAsync("failed");
        var job = JsonNode.Parse(await _client.GetStringAsync($"/api/v1/jobs/{id}", Cancel))!;

        job["state"]!.GetValue<string>().Should().Be("failed");
        var error = job["error"]!;
        error["code"]!.GetValue<string>().Should().Be("disk_full");
        error["status"]!.GetValue<int>().Should().Be(507);
        error["type"]!.GetValue<string>().Should().Be("https://stickersnap.app/problems/disk_full");
        error["log"]!.GetValue<string>().Should().Be("ENOSPC");
        error["traceId"]!.GetValue<string>().Should().Be(id);
    }

    [Theory]
    [InlineData("GET", "")]
    [InlineData("POST", "/cancel")]
    [InlineData("POST", "/retry")]
    public async Task Says_job_not_found_for_a_job_it_does_not_have(string method, string action)
    {
        var response = await _client.SendAsync(new HttpRequestMessage(new HttpMethod(method), $"/api/v1/jobs/nope{action}"), Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        CodeOf(await Json(response)).Should().Be("job_not_found");
    }

    [Fact]
    public async Task Refuses_a_job_of_a_project_not_on_the_list()
    {
        var request = Export();
        request["projectId"] = Guid.NewGuid().ToString();

        var response = await CreateAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        CodeOf(await Json(response)).Should().Be("project_not_found");
    }

    [Fact]
    public async Task Refuses_an_export_of_another_revision()
    {
        var response = await CreateAsync(Export(revision: 11));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        CodeOf(await Json(response)).Should().Be("revision_conflict");
    }

    [Fact]
    public async Task Refuses_unknown_segments_naming_them()
    {
        var response = await CreateAsync(Export(12, "seg-1", "seg-9"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await Json(response);
        CodeOf(problem).Should().Be("bad_request");
        problem["fields"]!.ToJsonString().Should().Be("""{"segmentIds[1]":["unknown"]}""");
    }

    [Fact]
    public async Task Refuses_a_kind_it_does_not_know()
    {
        var request = Export();
        request["kind"] = "render";

        var response = await CreateAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        CodeOf(await Json(response)).Should().Be("bad_request");
    }

    /// <summary>The first segment of the sample, as the editor sends it.</summary>
    private static object SampleSegment() => new
    {
        id = "seg-1",
        name = "Стикер 1",
        inFrame = 0,
        outFrame = 29,
        excludedFrames = Array.Empty<int>(),
        frame = new { mode = "crop", preset = "square", rect = new { x = 105, y = 0, width = 270, height = 270 } },
        speed = 1,
        loop = "normal",
        preset = "sticker",
        longSticker = false,
        autoFit = true,
    };
}
